using System.Text.Json;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PKHeX.Core;
using PkManager.Server.Helpers;
using PkManager.Server.Models.Entity;
using PkManager.Server.Models.Request;
using PkManager.Server.Models.Response;
using PkManager.Server.Services;

namespace PkManager.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PokemonController : ControllerBase
{
    private readonly NpgsqlConnection _db;
    private readonly ParseService _parseService;
    private readonly PokemonEditService _editService;
    private readonly SaveFileService _saveFileService;
    private readonly LegalizationService _legalizationService;
    private readonly LegalityCacheService _legalityCache;
    private readonly UserContext _userContext;

    public PokemonController(
        NpgsqlConnection db,
        ParseService parseService,
        PokemonEditService editService,
        SaveFileService saveFileService,
        LegalizationService legalizationService,
        LegalityCacheService legalityCache,
        UserContext userContext)
    {
        _db = db;
        _parseService = parseService;
        _editService = editService;
        _saveFileService = saveFileService;
        _legalizationService = legalizationService;
        _legalityCache = legalityCache;
        _userContext = userContext;
    }

    /// <summary>
    /// 获取银行宝可梦完整可编辑数据
    /// </summary>
    [HttpGet("bank/{id:guid}")]
    public async Task<ActionResult<ApiResponse<PokemonDto>>> GetBankPokemon(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<PokemonDto>.Error(401, "未登录"));

        var bank = await _db.QueryFirstOrDefaultAsync<BankPokemon>(
            "SELECT * FROM bank_pokemon WHERE id = @Id AND user_id = @UserId",
            new { Id = id, UserId = userId.Value });

        if (bank != null && !string.IsNullOrEmpty(bank.PkmDataBase64))
        {
            var pokemon = _parseService.ParseSinglePokemon(Convert.FromBase64String(bank.PkmDataBase64));
            pokemon.Id = bank.Id;
            return Ok(ApiResponse<PokemonDto>.Ok(pokemon));
        }
        return NotFound(ApiResponse<PokemonDto>.Error(404, "宝可梦不存在"));
    }

    /// <summary>
    /// 编辑存档 Box 槽位宝可梦（直接修改 raw_save_data 二进制）
    /// </summary>
    [HttpPut("save-slot")]
    public async Task<ActionResult<ApiResponse<EditResultDto>>> EditSaveSlot([FromBody] SaveSlotEditRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<EditResultDto>.Error(401, "未登录"));

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ApiResponse<EditResultDto>.Error(400, "缺少宝可梦数据"));
        if (request.SaveFileId == Guid.Empty)
            return BadRequest(ApiResponse<EditResultDto>.Error(400, "缺少存档ID"));

        try
        {
            var pkm = _parseService.RebuildPkm(request.PkmDataBase64);
            var result = _editService.ApplyEdits(pkm, request);

            // 自动备份（编辑前）
            await _saveFileService.CreateBackup(request.SaveFileId, userId.Value, "编辑前自动备份");

            // 直接写入 raw_save_data
            PKM? persisted;
            if (request.IsParty)
                persisted = await PersistPartyEdit(request.SaveFileId, userId.Value, request.SlotIndex, pkm);
            else
            {
                await _saveFileService.WriteBoxSlot(request.SaveFileId, userId.Value, request.BoxIndex, request.SlotIndex, pkm);
                persisted = _saveFileService.ReadBoxSlot(request.SaveFileId, userId.Value, request.BoxIndex, request.SlotIndex);
            }

            if (persisted != null)
                result.UpdatedPokemon = ParseService.MapToPokemonDto(persisted);

            return Ok(ApiResponse<EditResultDto>.Ok(result,
                result.IsValid ? "修改已保存" : "已保存（⚠️ 不合法）"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<EditResultDto>.Error(ex.ErrorCode, ex.Message));
        }
    }

    /// <summary>
    /// 验证 Party 宝可梦合法性（不保存）
    /// </summary>
    [HttpPost("validate-party")]
    public async Task<ActionResult<ApiResponse<LegalityReportDto>>> ValidateParty([FromBody] PartyPokemonEditRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<LegalityReportDto>.Error(401, "未登录"));

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ApiResponse<LegalityReportDto>.Error(400, "缺少宝可梦数据"));

        try
        {
            var pkm = _parseService.RebuildPkm(request.PkmDataBase64);
            _editService.ApplyEditsToPkm(pkm, request);
            var report = _editService.ValidateOnly(pkm);
            return Ok(ApiResponse<LegalityReportDto>.Ok(report));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<LegalityReportDto>.Error(400, ex.Message));
        }
    }

    /// <summary>
    /// 编辑随行宝可梦 — 直接修改存档原始二进制中的 Party 槽位
    /// </summary>
    [HttpPut("party")]
    public async Task<ActionResult<ApiResponse<EditResultDto>>> EditParty([FromBody] PartyPokemonEditRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<EditResultDto>.Error(401, "未登录"));

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ApiResponse<EditResultDto>.Error(400, "缺少宝可梦数据"));

        try
        {
            var pkm = _parseService.RebuildPkm(request.PkmDataBase64);
            var result = _editService.ApplyEdits(pkm, request);

            // 始终持久化：直接写入存档原始二进制的 Party 槽位
            if (request.SaveFileId != Guid.Empty)
            {
                await PersistPartyEdit(request.SaveFileId, userId.Value, request.SlotIndex, pkm);
            }

            return Ok(ApiResponse<EditResultDto>.Ok(result,
                result.IsValid ? "修改已保存到随行宝可梦" : "已保存（⚠️ 不合法）"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<EditResultDto>.Error(ex.ErrorCode, ex.Message));
        }
    }

    /// <summary>
    /// 将编辑后的 PKM 写回存档原始二进制的 Party 槽位
    /// </summary>
    private async Task<PKM?> PersistPartyEdit(Guid saveFileId, Guid userId, int slotIndex, PKM editedPkm)
    {
        // 获取存档实体
        var saveFile = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>(
            "SELECT * FROM save_files WHERE id = @Id AND user_id = @UserId",
            new { Id = saveFileId, UserId = userId });
        if (saveFile == null) return null;

        var rawData = _saveFileService.ReadSaveBytes(saveFile, userId);

        PKHeX.Core.SaveFile sav;
        try
        {
            sav = ParseService.OpenSaveFile(rawData, saveFile.Filename);
        }
        catch (BusinessException)
        {
            return null;
        }

        // 写入 Party 槽位
        if (slotIndex >= 0 && slotIndex < 6)
        {
            var compatible = sav.GetCompatiblePKM(editedPkm);
            sav.SetPartySlotAtIndex(compatible, slotIndex);
        }

        // 重写存档二进制
        var updatedData = ParseService.FinalizeSaveBytes(sav, rawData);
        try
        {
            ParseService.OpenSaveFile(updatedData, saveFile.Filename);
        }
        catch (BusinessException)
        {
            throw new BusinessException("保存后的存档无法重新解析，已中止写入");
        }

        await _saveFileService.WriteSaveBytes(saveFile, userId, updatedData);

        _legalityCache.InvalidateSave(saveFileId);

        return _saveFileService.ReadPartySlot(saveFileId, userId, slotIndex);
    }

    /// <summary>
    /// 生成宝可梦 QR 码文本（用于实体3DS游戏机扫码注入）
    /// </summary>
    [HttpPost("qr")]
    public ActionResult<ApiResponse<string>> GenerateQR([FromBody] QrGenerateRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<string>.Error(401, "未登录"));

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ApiResponse<string>.Error(400, "缺少宝可梦数据"));

        try
        {
            var pkm = _parseService.RebuildPkm(request.PkmDataBase64);
            var qrMessage = QRMessageUtil.GetMessage(pkm);
            return Ok(ApiResponse<string>.Ok(qrMessage, "QR码已生成"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<string>.Error(400, $"QR生成失败: {ex.Message}"));
        }
    }

    /// <summary>
    /// 仅校验，不保存
    /// </summary>
    [HttpPost("{id:guid}/validate")]
    public async Task<ActionResult<ApiResponse<LegalityReportDto>>> Validate(Guid id, [FromBody] PokemonEditRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<LegalityReportDto>.Error(401, "未登录"));

        var pkm = await FindPkm(id, userId.Value);
        if (pkm == null)
            return NotFound(ApiResponse<LegalityReportDto>.Error(404, "宝可梦不存在"));

        // 仅应用临时修改用于校验
        ApplyEditsTemp(pkm, request);
        var report = _editService.ValidateOnly(pkm);

        return Ok(ApiResponse<LegalityReportDto>.Ok(report));
    }

    /// <summary>
    /// 上传单个 .pk* 文件解析
    /// </summary>
    [HttpPost("parse-single")]
    [RequestSizeLimit(1 * 1024 * 1024)] // 1 MB
    public async Task<ActionResult<ApiResponse<PokemonDto>>> ParseSingle(IFormFile file)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<PokemonDto>.Error(401, "未登录"));

        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<PokemonDto>.Error(400, "请选择文件"));

        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var pokemon = _parseService.ParseSinglePokemon(ms.ToArray());
            return Ok(ApiResponse<PokemonDto>.Ok(pokemon, "解析成功"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<PokemonDto>.Error(ex.ErrorCode, ex.Message));
        }
    }

    /// <summary>
    /// 导出单只宝可梦 (.pk*)
    /// </summary>
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized();

        var pkm = await FindPkm(id, userId.Value);
        if (pkm == null) return NotFound();

        var data = _editService.ExportSinglePkm(pkm);
        return File(data, "application/octet-stream", $"pokemon_{id}.pk{Math.Max((int)1, (int)pkm.Format)}");
    }

    // ── F.2 合法性引擎升级: 生成 + 修复 ─────────────────────

    /// <summary>
    /// 从模板自动生成合法宝可梦。
    /// </summary>
    [HttpPost("legalize")]
    public async Task<ActionResult<ApiResponse<LegalizationResultDto>>> Legalize(
        [FromBody] LegalizationRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<LegalizationResultDto>.Error(401, "未登录"));

        if (request.Species < 1 || request.Species > 1025)
            return BadRequest(ApiResponse<LegalizationResultDto>.Error(400, "物种ID无效"));

        try
        {
            var trainerInfo = await ResolveTrainerInfo(request.TrainerSaveFileId,
                (GameVersion)request.TargetGameVersion);

            var (pkm, error, changes) = _legalizationService.GenerateFromTemplate(request, trainerInfo);

            if (pkm == null)
                return Ok(ApiResponse<LegalizationResultDto>.Ok(
                    new LegalizationResultDto { Success = false, Error = error },
                    error ?? "生成失败"));

            var dto = ParseService.MapToPokemonDto(pkm);
            var base64 = new byte[pkm.SIZE_PARTY];
            pkm.WriteDecryptedDataParty(base64);

            return Ok(ApiResponse<LegalizationResultDto>.Ok(
                new LegalizationResultDto
                {
                    Success = true,
                    Pokemon = dto,
                    PkmDataBase64 = Convert.ToBase64String(base64),
                    Changes = changes
                }, "合法宝可梦已生成"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<LegalizationResultDto>.Error(ex.ErrorCode, ex.Message));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<LegalizationResultDto>.Error(400, ex.Message));
        }
    }

    /// <summary>
    /// 从 Showdown 文本导入并生成合法宝可梦。
    /// </summary>
    [HttpPost("legalize-showdown")]
    public async Task<ActionResult<ApiResponse<LegalizationResultDto>>> LegalizeShowdown(
        [FromBody] ShowdownImportRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<LegalizationResultDto>.Error(401, "未登录"));

        if (string.IsNullOrWhiteSpace(request.ShowdownText))
            return BadRequest(ApiResponse<LegalizationResultDto>.Error(400, "Showdown文本为空"));

        try
        {
            var trainerInfo = await ResolveTrainerInfo(request.TrainerSaveFileId,
                (GameVersion)request.TargetGameVersion);

            var (pkm, error, encounterType) = _legalizationService.GenerateFromShowdown(request, trainerInfo);

            if (pkm == null)
                return Ok(ApiResponse<LegalizationResultDto>.Ok(
                    new LegalizationResultDto { Success = false, Error = error },
                    error ?? "Showdown导入失败"));

            var dto = ParseService.MapToPokemonDto(pkm);
            var base64 = new byte[pkm.SIZE_PARTY];
            pkm.WriteDecryptedDataParty(base64);

            return Ok(ApiResponse<LegalizationResultDto>.Ok(
                new LegalizationResultDto
                {
                    Success = true,
                    Pokemon = dto,
                    PkmDataBase64 = Convert.ToBase64String(base64),
                    EncounterType = encounterType,
                    Changes = { "Generated from Showdown" }
                }, "Showdown导入成功"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<LegalizationResultDto>.Error(ex.ErrorCode, ex.Message));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<LegalizationResultDto>.Error(400, ex.Message));
        }
    }

    /// <summary>
    /// 仅解析 Showdown 文本预览（不执行遭遇搜索生成）。
    /// </summary>
    [HttpPost("parse-showdown")]
    public ActionResult<ApiResponse<ShowdownParseResultDto>> ParseShowdown(
        [FromBody] ShowdownParseRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<ShowdownParseResultDto>.Error(401, "未登录"));

        if (string.IsNullOrWhiteSpace(request.ShowdownText))
            return BadRequest(ApiResponse<ShowdownParseResultDto>.Error(400, "Showdown文本为空"));

        try
        {
            var sets = _legalizationService.ParseShowdownText(request.ShowdownText);
            return Ok(ApiResponse<ShowdownParseResultDto>.Ok(
                new ShowdownParseResultDto { Success = true, Sets = sets },
                $"解析到 {sets.Count} 套配置"));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<ShowdownParseResultDto>.Error(400, ex.Message));
        }
    }

    /// <summary>
    /// 对非法宝可梦应用自动修复（仅更新面板临时状态，不持久化）。
    /// </summary>
    [HttpPost("auto-fix")]
    public async Task<ActionResult<ApiResponse<AutoFixResultDto>>> AutoFix(
        [FromBody] AutoFixRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<AutoFixResultDto>.Error(401, "未登录"));

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ApiResponse<AutoFixResultDto>.Error(400, "缺少宝可梦数据"));

        try
        {
            var pkm = _parseService.RebuildPkm(request.PkmDataBase64);

            var targetVersion = request.TargetGameVersion.HasValue
                ? (GameVersion)request.TargetGameVersion.Value
                : pkm.Version;

            var trainerInfo = await ResolveTrainerInfo(request.TrainerSaveFileId, targetVersion);

            var result = _legalizationService.AutoFix(pkm, request.EditSnapshot,
                request.FixActions, trainerInfo);

            return Ok(ApiResponse<AutoFixResultDto>.Ok(result,
                result.Fixed ? $"修复完成: {string.Join(", ", result.AppliedFixes)}" : "无需修复"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<AutoFixResultDto>.Error(ex.ErrorCode, ex.Message));
        }
        catch (Exception ex)
        {
            return BadRequest(ApiResponse<AutoFixResultDto>.Error(400, ex.Message));
        }
    }

    // ── 私有方法 ────────────────────────────────────────

    /// <summary>
    /// 解析 ITrainerInfo: 优先从存档提取（OT/TID/SID/Language/Gender），版本用目标版本覆盖。
    /// 无存档时退化为 SimpleTrainerInfo(targetVersion)。
    /// </summary>
    private async Task<ITrainerInfo> ResolveTrainerInfo(Guid? trainerSaveFileId, GameVersion targetVersion)
    {
        if (trainerSaveFileId.HasValue)
        {
            var userId = _userContext.UserId;
            if (userId == null) return new SimpleTrainerInfo(targetVersion);

            var saveFile = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>(
                "SELECT * FROM save_files WHERE id = @Id AND user_id = @UserId",
                new { Id = trainerSaveFileId.Value, UserId = userId.Value });

            if (saveFile != null)
            {
                var rawData = _saveFileService.ReadSaveBytes(saveFile, userId.Value);
                var sav = ParseService.OpenSaveFile(rawData, saveFile.Filename);
                return new SimpleTrainerInfo(sav, targetVersion);
            }
        }

        return new SimpleTrainerInfo(targetVersion);
    }

    private async Task<PKM?> FindPkm(Guid id, Guid userId)
    {
        // 从存档查找
        var slot = await _db.QueryFirstOrDefaultAsync<SaveBoxPokemon>(
            "SELECT * FROM save_box_pokemon WHERE id = @Id", new { Id = id });
        if (slot != null && !string.IsNullOrEmpty(slot.PokemonJson))
        {
            var owner = await _db.QueryFirstOrDefaultAsync<Guid>(
                "SELECT user_id FROM save_files WHERE id = @Id", new { Id = slot.SaveFileId });
            if (owner == userId)
            {
                var pokemonDto = JsonSerializer.Deserialize<PokemonDto>(slot.PokemonJson);
                if (pokemonDto?.PkmDataBase64 != null)
                    return _parseService.RebuildPkm(pokemonDto.PkmDataBase64);
            }
        }

        // 从银行查找
        var bank = await _db.QueryFirstOrDefaultAsync<BankPokemon>(
            "SELECT * FROM bank_pokemon WHERE id = @Id AND user_id = @UserId",
            new { Id = id, UserId = userId });
        if (bank != null && !string.IsNullOrEmpty(bank.PkmDataBase64))
            return _parseService.RebuildPkm(bank.PkmDataBase64);

        return null;
    }

    /// <summary>
    /// 临时应用编辑（用于校验，不持久化）
    /// </summary>
    private void ApplyEditsTemp(PKM pkm, PokemonEditRequest request)
    {
        // Reuse the full ApplyEditsToPkm from the edit service
        _editService.ApplyEditsToPkm(pkm, request);
    }

    /// <summary>
    /// 从 PokemonJson (JSON) 中提取 PkmDataBase64（Base64 二进制数据）
    /// </summary>
    private static string? ExtractPkmDataBase64(string pokemonJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(pokemonJson);
            if (doc.RootElement.TryGetProperty("pkmDataBase64", out var prop))
                return prop.GetString();
        }
        catch { }
        return null;
    }
}
