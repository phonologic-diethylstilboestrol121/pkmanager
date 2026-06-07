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
    private readonly UserContext _userContext;

    public PokemonController(
        NpgsqlConnection db,
        ParseService parseService,
        PokemonEditService editService,
        SaveFileService saveFileService,
        UserContext userContext)
    {
        _db = db;
        _parseService = parseService;
        _editService = editService;
        _saveFileService = saveFileService;
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
        // 获取存档原始二进制
        var saveFile = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>(
            "SELECT * FROM save_files WHERE id = @Id AND user_id = @UserId",
            new { Id = saveFileId, UserId = userId });
        if (saveFile == null) return null;

        byte[] rawData;
        if (!string.IsNullOrEmpty(saveFile.SavePath) && System.IO.File.Exists(saveFile.SavePath))
            rawData = await System.IO.File.ReadAllBytesAsync(saveFile.SavePath);
        else
            rawData = saveFile.RawSaveData;

        var sav = SaveUtil.GetVariantSAV((byte[])rawData.Clone());
        if (sav == null) return null;

        // 写入 Party 槽位
        if (slotIndex >= 0 && slotIndex < 6)
        {
            var compatible = sav.GetCompatiblePKM(editedPkm);
            sav.SetPartySlotAtIndex(compatible, slotIndex);
        }

        // 重写存档二进制
        var updatedData = sav.Write();
        var reparsed = SaveUtil.GetVariantSAV((byte[])updatedData.Clone());
        if (reparsed == null)
            throw new BusinessException("保存后的存档无法重新解析，已中止写入");

        if (!string.IsNullOrEmpty(saveFile.SavePath))
        {
            await System.IO.File.WriteAllBytesAsync(saveFile.SavePath, updatedData);
            await _db.ExecuteAsync(
                "UPDATE save_files SET file_size = @Size, is_modified = TRUE, updated_at = NOW() WHERE id = @Id",
                new { Id = saveFileId, Size = updatedData.Length });
        }
        else
        {
            await _db.ExecuteAsync(
                "UPDATE save_files SET raw_save_data = @Data, file_size = @Size, is_modified = TRUE, updated_at = NOW() WHERE id = @Id",
                new { Id = saveFileId, Data = updatedData, Size = updatedData.Length });
        }

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

    // ── 私有方法 ────────────────────────────────────────

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
