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
public class PokemonController : LocalizedControllerBase
{
    private readonly NpgsqlConnection _db;
    private readonly ParseService _parseService;
    private readonly PokemonEditService _editService;
    private readonly SaveFileService _saveFileService;
    private readonly LegalizationService _legalizationService;
    private readonly LegalityCacheService _legalityCache;
    private readonly EvolutionService _evolutionService;
    private readonly UserContext _userContext;

    public PokemonController(
        NpgsqlConnection db,
        ParseService parseService,
        PokemonEditService editService,
        SaveFileService saveFileService,
        LegalizationService legalizationService,
        LegalityCacheService legalityCache,
        EvolutionService evolutionService,
        UserContext userContext)
    {
        _db = db;
        _parseService = parseService;
        _editService = editService;
        _saveFileService = saveFileService;
        _legalizationService = legalizationService;
        _legalityCache = legalityCache;
        _evolutionService = evolutionService;
        _userContext = userContext;
    }

    /// <summary>
    /// 获取银行宝可梦完整可编辑数据
    /// </summary>
    [HttpGet("bank/{id:guid}")]
    public async Task<ActionResult<ApiResponse<PokemonDto>>> GetBankPokemon(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<PokemonDto>();

        var bank = await _db.QueryFirstOrDefaultAsync<BankPokemon>(
            "SELECT * FROM bank_pokemon WHERE id = @Id AND user_id = @UserId",
            new { Id = id, UserId = userId.Value });

        if (bank != null && !string.IsNullOrEmpty(bank.PkmDataBase64))
        {
            var pokemon = _parseService.ParseSinglePokemon(Convert.FromBase64String(bank.PkmDataBase64));
            pokemon.Id = bank.Id;
            return Ok(ApiResponse<PokemonDto>.Ok(pokemon));
        }
        return NotFound(ErrorMessage<PokemonDto>(404, "common.pokemonNotFound"));
    }

    /// <summary>
    /// 编辑存档 Box 槽位宝可梦（直接修改 raw_save_data 二进制）
    /// </summary>
    [HttpPut("save-slot")]
    public async Task<ActionResult<ApiResponse<EditResultDto>>> EditSaveSlot([FromBody] SaveSlotEditRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<EditResultDto>();

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ErrorMessage<EditResultDto>(400, "pokemon.missingPokemonData"));
        if (request.SaveFileId == Guid.Empty)
            return BadRequest(ErrorMessage<EditResultDto>(400, "pokemon.missingSaveFileId"));

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
                result.UpdatedPokemon = _parseService.MapToPokemonDto(persisted);

            return Ok(OkMessage(result, result.IsValid ? "pokemon.editSaved" : "pokemon.editSavedInvalid"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<EditResultDto>(ex));
        }
    }

    /// <summary>
    /// 验证 Party 宝可梦合法性（不保存）
    /// </summary>
    [HttpPost("validate-party")]
    public async Task<ActionResult<ApiResponse<LegalityReportDto>>> ValidateParty([FromBody] PartyPokemonEditRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<LegalityReportDto>();

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ErrorMessage<LegalityReportDto>(400, "pokemon.missingPokemonData"));

        try
        {
            var pkm = _parseService.RebuildPkm(request.PkmDataBase64);
            _editService.ApplyEditsToPkm(pkm, request);
            var report = _editService.ValidateOnly(pkm);
            return Ok(ApiResponse<LegalityReportDto>.Ok(report));
        }
        catch (Exception ex)
        {
            return BadRequest(ErrorMessageFallback<LegalityReportDto>(400, "common.unexpectedError", ex.Message));
        }
    }

    /// <summary>
    /// 编辑随行宝可梦 — 直接修改存档原始二进制中的 Party 槽位
    /// </summary>
    [HttpPut("party")]
    public async Task<ActionResult<ApiResponse<EditResultDto>>> EditParty([FromBody] PartyPokemonEditRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<EditResultDto>();

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ErrorMessage<EditResultDto>(400, "pokemon.missingPokemonData"));

        try
        {
            var pkm = _parseService.RebuildPkm(request.PkmDataBase64);
            var result = _editService.ApplyEdits(pkm, request);

            // 始终持久化：直接写入存档原始二进制的 Party 槽位
            if (request.SaveFileId != Guid.Empty)
            {
                await PersistPartyEdit(request.SaveFileId, userId.Value, request.SlotIndex, pkm);
            }

            return Ok(OkMessage(result, result.IsValid ? "pokemon.editPartySaved" : "pokemon.editSavedInvalid"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<EditResultDto>(ex));
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
            throw BusinessException.FromKey("save.saveReparseFailed", 400);
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
        if (userId == null) return UnauthorizedMessage<string>();

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ErrorMessage<string>(400, "pokemon.missingPokemonData"));

        try
        {
            var pkm = _parseService.RebuildPkm(request.PkmDataBase64);
            var qrMessage = QRMessageUtil.GetMessage(pkm);
            return Ok(OkMessage(qrMessage, "pokemon.qrGenerated"));
        }
        catch (Exception ex)
        {
            return BadRequest(ErrorMessageFallback<string>(400, "pokemon.qrGenerateFailed", ex.Message));
        }
    }

    /// <summary>
    /// 导出宝可梦为 Showdown 对战配置文本。
    /// </summary>
    [HttpPost("export-showdown")]
    public ActionResult<ApiResponse<string>> ExportShowdown([FromBody] ShowdownExportRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<string>();

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ErrorMessage<string>(400, "pokemon.missingPokemonData"));

        try
        {
            var pkm = _parseService.RebuildPkm(request.PkmDataBase64);
            var showdownText = _legalizationService.ExportShowdown(pkm, request.EditSnapshot);
            return Ok(OkMessage(showdownText, "pokemon.showdownExportSuccess"));
        }
        catch (Exception ex)
        {
            return BadRequest(ErrorMessageFallback<string>(400, "pokemon.exportFailed", ex.Message));
        }
    }

    /// <summary>
    /// 仅校验，不保存
    /// </summary>
    [HttpPost("{id:guid}/validate")]
    public async Task<ActionResult<ApiResponse<LegalityReportDto>>> Validate(Guid id, [FromBody] PokemonEditRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<LegalityReportDto>();

        var pkm = await FindPkm(id, userId.Value);
        if (pkm == null)
            return NotFound(ErrorMessage<LegalityReportDto>(404, "common.pokemonNotFound"));

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
        if (userId == null) return UnauthorizedMessage<PokemonDto>();

        if (file == null || file.Length == 0)
            return BadRequest(ErrorMessage<PokemonDto>(400, "pokemon.fileRequired"));

        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var pokemon = _parseService.ParseSinglePokemon(ms.ToArray());
            return Ok(OkMessage(pokemon, "pokemon.parseSuccess"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<PokemonDto>(ex));
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
        if (userId == null) return UnauthorizedMessage<LegalizationResultDto>();

        if (request.Species < 1 || request.Species > 1025)
            return BadRequest(ErrorMessage<LegalizationResultDto>(400, "pokemon.invalidSpecies"));

        try
        {
            var trainerInfo = await ResolveTrainerInfo(request.TrainerSaveFileId,
                (GameVersion)request.TargetGameVersion);

            var (pkm, error, changes) = _legalizationService.GenerateFromTemplate(request, trainerInfo);

            if (pkm == null)
                return Ok(OkMessageFallback(
                    new LegalizationResultDto { Success = false, Error = error },
                    "pokemon.legalizeGenerateFailed",
                    error ?? Text("pokemon.legalizeGenerateFailed")));

            var dto = _parseService.MapToPokemonDto(pkm);
            var base64 = new byte[pkm.SIZE_PARTY];
            pkm.WriteDecryptedDataParty(base64);

            return Ok(OkMessage(
                new LegalizationResultDto
                {
                    Success = true,
                    Pokemon = dto,
                    PkmDataBase64 = Convert.ToBase64String(base64),
                    Changes = changes
                }, "pokemon.legalizeGenerated"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<LegalizationResultDto>(ex));
        }
        catch (Exception ex)
        {
            return BadRequest(ErrorMessageFallback<LegalizationResultDto>(400, "common.unexpectedError", ex.Message));
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
        if (userId == null) return UnauthorizedMessage<LegalizationResultDto>();

        if (string.IsNullOrWhiteSpace(request.ShowdownText))
            return BadRequest(ErrorMessage<LegalizationResultDto>(400, "pokemon.showdownTextRequired"));

        try
        {
            var trainerInfo = await ResolveTrainerInfo(request.TrainerSaveFileId,
                (GameVersion)request.TargetGameVersion);

            var (pkm, error, encounterType) = _legalizationService.GenerateFromShowdown(request, trainerInfo);

            if (pkm == null)
                return Ok(OkMessageFallback(
                    new LegalizationResultDto { Success = false, Error = error },
                    "pokemon.showdownImportFailed",
                    error ?? Text("pokemon.showdownImportFailed")));

            var dto = _parseService.MapToPokemonDto(pkm);
            var base64 = new byte[pkm.SIZE_PARTY];
            pkm.WriteDecryptedDataParty(base64);

            return Ok(OkMessage(
                new LegalizationResultDto
                {
                    Success = true,
                    Pokemon = dto,
                    PkmDataBase64 = Convert.ToBase64String(base64),
                    EncounterType = encounterType,
                    Changes = { "Generated from Showdown" }
                }, "pokemon.showdownImportSuccess"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<LegalizationResultDto>(ex));
        }
        catch (Exception ex)
        {
            return BadRequest(ErrorMessageFallback<LegalizationResultDto>(400, "common.unexpectedError", ex.Message));
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
        if (userId == null) return UnauthorizedMessage<ShowdownParseResultDto>();

        if (string.IsNullOrWhiteSpace(request.ShowdownText))
            return BadRequest(ErrorMessage<ShowdownParseResultDto>(400, "pokemon.showdownTextRequired"));

        try
        {
            var sets = _legalizationService.ParseShowdownText(request.ShowdownText);
            return Ok(OkMessage(
                new ShowdownParseResultDto { Success = true, Sets = sets },
                "pokemon.showdownParseSuccess", sets.Count));
        }
        catch (Exception ex)
        {
            return BadRequest(ErrorMessageFallback<ShowdownParseResultDto>(400, "common.unexpectedError", ex.Message));
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
        if (userId == null) return UnauthorizedMessage<AutoFixResultDto>();

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ErrorMessage<AutoFixResultDto>(400, "pokemon.missingPokemonData"));

        try
        {
            var pkm = _parseService.RebuildPkm(request.PkmDataBase64);

            var targetVersion = request.TargetGameVersion.HasValue
                ? (GameVersion)request.TargetGameVersion.Value
                : pkm.Version;

            var trainerInfo = await ResolveTrainerInfo(request.TrainerSaveFileId, targetVersion);

            var result = _legalizationService.AutoFix(pkm, request.EditSnapshot,
                request.FixActions, trainerInfo);

            return Ok(OkMessage(result,
                result.Fixed ? "pokemon.autoFixCompleted" : "pokemon.autoFixNotNeeded",
                string.Join(", ", result.AppliedFixes)));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<AutoFixResultDto>(ex));
        }
        catch (Exception ex)
        {
            return BadRequest(ErrorMessageFallback<AutoFixResultDto>(400, "common.unexpectedError", ex.Message));
        }
    }

    // ── D.2 遭遇数据库 ──────────────────────────────────

    /// <summary>
    /// 搜索合法遭遇模板。
    /// </summary>
    [HttpPost("search-encounters")]
    public async Task<ActionResult<ApiResponse<EncounterSearchResultDto>>> SearchEncounters(
        [FromBody] EncounterSearchRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<EncounterSearchResultDto>();

        if (request.Species < 1 || request.Species > 1025)
            return BadRequest(ErrorMessage<EncounterSearchResultDto>(400, "pokemon.invalidSpecies"));

        try
        {
            // 加载目标存档
            var saveFile = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>(
                "SELECT * FROM save_files WHERE id = @Id AND user_id = @UserId",
                new { Id = request.SaveFileId, UserId = userId.Value });
            if (saveFile == null)
                return NotFound(ErrorMessage<EncounterSearchResultDto>(404, "save.notFound"));

            var rawData = _saveFileService.ReadSaveBytes(saveFile, userId.Value);
            var sav = ParseService.OpenSaveFile(rawData, saveFile.Filename);

            // 优先使用 DB 归一化后的具体版本号
            var targetVersion = saveFile.GameVersion.HasValue
                ? (GameVersion)saveFile.GameVersion.Value
                : sav.Version;

            var trainerInfo = new SimpleTrainerInfo(sav, targetVersion);

            var result = _legalizationService.SearchEncounters(request, trainerInfo);
            return Ok(OkMessage(result, "pokemon.encounterSearchCompleted", result.TotalCount));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<EncounterSearchResultDto>(ex));
        }
        catch (Exception ex)
        {
            return BadRequest(ErrorMessageFallback<EncounterSearchResultDto>(400, "common.unexpectedError", ex.Message));
        }
    }

    /// <summary>
    /// 将遭遇模板的约束字段应用到当前编辑中的宝可梦（不写盘）。
    /// </summary>
    [HttpPost("apply-encounter")]
    public async Task<ActionResult<ApiResponse<EncounterApplyResultDto>>> ApplyEncounter(
        [FromBody] EncounterApplyRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<EncounterApplyResultDto>();

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ErrorMessage<EncounterApplyResultDto>(400, "pokemon.missingPokemonData"));
        if (string.IsNullOrEmpty(request.RecomputeToken))
            return BadRequest(ErrorMessage<EncounterApplyResultDto>(400, "pokemon.encounterTokenRequired"));
        if (request.EditSnapshot == null)
            return BadRequest(ErrorMessage<EncounterApplyResultDto>(400, "pokemon.editSnapshotRequired"));

        try
        {
            // 加载目标存档以构造 ITrainerInfo
            var saveFile = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>(
                "SELECT * FROM save_files WHERE id = @Id AND user_id = @UserId",
                new { Id = request.SaveFileId, UserId = userId.Value });
            if (saveFile == null)
                return NotFound(ErrorMessage<EncounterApplyResultDto>(404, "save.notFound"));

            var rawData = _saveFileService.ReadSaveBytes(saveFile, userId.Value);
            var sav = ParseService.OpenSaveFile(rawData, saveFile.Filename);

            var targetVersion = saveFile.GameVersion.HasValue
                ? (GameVersion)saveFile.GameVersion.Value
                : sav.Version;

            var trainerInfo = new SimpleTrainerInfo(sav, targetVersion);

            var result = _legalizationService.ApplyEncounter(request, trainerInfo, sav);

            if (!result.Success)
                return Ok(OkMessageFallback(result, "pokemon.encounterApplyFailed", result.Error ?? Text("pokemon.encounterApplyFailed")));

            return Ok(OkMessage(result, "pokemon.encounterApplied", string.Join(", ", result.AppliedFields)));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<EncounterApplyResultDto>(ex));
        }
        catch (Exception ex)
        {
            return BadRequest(ErrorMessageFallback<EncounterApplyResultDto>(400, "common.unexpectedError", ex.Message));
        }
    }

    /// <summary>
    /// 从遭遇模板生成全新宝可梦并写入存档槽位。
    /// </summary>
    [HttpPost("generate-from-encounter")]
    public async Task<ActionResult<ApiResponse<EncounterGenerateResultDto>>> GenerateFromEncounter(
        [FromBody] EncounterGenerateRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<EncounterGenerateResultDto>();

        if (string.IsNullOrEmpty(request.RecomputeToken))
            return BadRequest(ErrorMessage<EncounterGenerateResultDto>(400, "pokemon.encounterTokenRequired"));

        try
        {
            // 加载目标存档
            var saveFile = await _db.QueryFirstOrDefaultAsync<Models.Entity.SaveFile>(
                "SELECT * FROM save_files WHERE id = @Id AND user_id = @UserId",
                new { Id = request.SaveFileId, UserId = userId.Value });
            if (saveFile == null)
                return NotFound(ErrorMessage<EncounterGenerateResultDto>(404, "save.notFound"));

            var rawData = _saveFileService.ReadSaveBytes(saveFile, userId.Value);
            var sav = ParseService.OpenSaveFile(rawData, saveFile.Filename);

            var targetVersion = saveFile.GameVersion.HasValue
                ? (GameVersion)saveFile.GameVersion.Value
                : sav.Version;

            var trainerInfo = new SimpleTrainerInfo(sav, targetVersion);

            // 生成 PKM
            var (pkm, error) = _legalizationService.GenerateFromEncounter(request, trainerInfo);
            if (pkm == null)
                return Ok(OkMessageFallback(
                    new EncounterGenerateResultDto { Success = false, Error = error },
                    "pokemon.encounterGenerateFailed",
                    error ?? Text("pokemon.encounterGenerateFailed")));

            // 兼容转换
            var compat = sav.GetCompatiblePKM(pkm);
            if (compat == null)
                return Ok(OkMessage(
                    new EncounterGenerateResultDto { Success = false, Error = "宝可梦格式与目标存档不兼容" },
                    "pokemon.encounterGenerateFailed"));

            // 在兼容实体上跑合法性分析
            var la = new LegalityAnalysis(compat);
            var isLegal = la.Valid;
            var report = isLegal ? null : la.Report();
            if (!isLegal)
            {
                return Ok(ApiResponse<EncounterGenerateResultDto>.Ok(
                    new EncounterGenerateResultDto
                    {
                        Success = false,
                        Error = Text("pokemon.encounterIllegalResult"),
                        Pokemon = _parseService.MapToPokemonDto(compat),
                        PkmDataBase64 = ParseService.GetPkmBase64(compat),
                        IsLegal = false,
                        LegalityReport = report
                    },
                    Text("pokemon.encounterIllegalGenerateFailed"),
                    "pokemon.encounterIllegalGenerateFailed"));
            }

            // 写入槽位（空槽检查 + 兼容转换）
            try
            {
                _saveFileService.WritePkmToBoxSlot(sav, request.BoxIndex, request.SlotIndex,
                    pkm, allowOverwrite: request.AllowOverwrite);
            }
            catch (BusinessException ex)
            {
                return Ok(OkMessageFallback(
                    new EncounterGenerateResultDto { Success = false, Error = ex.Message },
                    ex.MessageKey ?? "common.unexpectedError",
                    ex.Message));
            }

            // 持久化存档（WriteBackSave 内置自动备份 + 预校验 + 缓存失效）
            await _saveFileService.WriteBackSave(saveFile, userId.Value, sav);

            // 从磁盘回读以确保返回实际落盘的数据
            var persisted = _saveFileService.ReadBoxSlot(request.SaveFileId, userId.Value,
                request.BoxIndex, request.SlotIndex);
            var dto = persisted != null ? _parseService.MapToPokemonDto(persisted) : _parseService.MapToPokemonDto(compat);
            var base64 = new byte[(persisted ?? compat).SIZE_PARTY];
            (persisted ?? compat).WriteDecryptedDataParty(base64);

            return Ok(OkMessage(
                new EncounterGenerateResultDto
                {
                    Success = true,
                    Pokemon = dto,
                    PkmDataBase64 = Convert.ToBase64String(base64),
                    IsLegal = isLegal,
                    LegalityReport = report
                }, isLegal ? "pokemon.encounterGenerated" : "pokemon.editSavedInvalid"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<EncounterGenerateResultDto>(ex));
        }
        catch (Exception ex)
        {
            return BadRequest(ErrorMessageFallback<EncounterGenerateResultDto>(400, "common.unexpectedError", ex.Message));
        }
    }

    // ── D.4 一键进化 ──────────────────────────────────

    /// <summary>
    /// 获取进化路径（含 TryEvolve 可用性判定，基于当前编辑态）。
    /// 使用 POST 以支持 editSnapshot body。
    /// </summary>
    [HttpPost("evolutions")]
    public ActionResult<ApiResponse<EvolutionPathDto>> GetEvolutions(
        [FromBody] GetEvolutionsRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<EvolutionPathDto>();

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ErrorMessage<EvolutionPathDto>(400, "pokemon.missingPokemonData"));

        try
        {
            var pkm = _parseService.RebuildPkm(request.PkmDataBase64);
            var paths = _evolutionService.GetEvolutionPaths(pkm, request.EditSnapshot);
            return Ok(ApiResponse<EvolutionPathDto>.Ok(paths));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<EvolutionPathDto>(ex));
        }
        catch (Exception ex)
        {
            return BadRequest(ErrorMessageFallback<EvolutionPathDto>(400, "common.unexpectedError", ex.Message));
        }
    }

    /// <summary>
    /// 执行进化（含当前编辑态应用、昵称/等级同步、脱壳忍者生成）。
    /// 基于编辑器当前未保存修改执行进化并持久化到存档。
    /// </summary>
    [HttpPost("evolve")]
    public async Task<ActionResult<ApiResponse<EvolveResultDto>>> Evolve(
        [FromBody] EvolveRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<EvolveResultDto>();

        if (string.IsNullOrEmpty(request.PkmDataBase64))
            return BadRequest(ErrorMessage<EvolveResultDto>(400, "pokemon.missingPokemonData"));
        if (request.SaveFileId == Guid.Empty)
            return BadRequest(ErrorMessage<EvolveResultDto>(400, "pokemon.missingSaveFileId"));
        if (request.TargetSpecies < 1 || request.TargetSpecies > 1025)
            return BadRequest(ErrorMessage<EvolveResultDto>(400, "pokemon.invalidTargetSpecies"));

        try
        {
            // 加载存档
            var (sf, sav) = await _saveFileService.LoadSave(request.SaveFileId, userId.Value);

            // 重建 PKM
            var pkm = _parseService.RebuildPkm(request.PkmDataBase64);

            // 执行进化（含 editSnapshot 应用、状态同步、写回槽位）
            var result = _evolutionService.ExecuteEvolve(pkm, sav, request);
            if (!result.Success)
                return Ok(OkMessageFallback(result, "pokemon.evolutionFailed", result.Error ?? Text("pokemon.evolutionFailed")));

            // 持久化（WriteBackSave 内部自带备份 + 缓存失效）
            await _saveFileService.WriteBackSave(sf, userId.Value, sav);

            return Ok(OkMessage(result, "pokemon.evolutionSuccess"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<EvolveResultDto>(ex));
        }
        catch (Exception ex)
        {
            return BadRequest(ErrorMessageFallback<EvolveResultDto>(400, "common.unexpectedError", ex.Message));
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
