using Microsoft.AspNetCore.Mvc;
using PkManager.Server.Helpers;
using PkManager.Server.Models.Request;
using PkManager.Server.Models.Response;
using PkManager.Server.Services;

namespace PkManager.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SaveFileController : LocalizedControllerBase
{
    private readonly SaveFileService _saveFileService;
    private readonly PokemonEditService _pokemonEditService;
    private readonly UserContext _userContext;

    public SaveFileController(
        SaveFileService saveFileService,
        PokemonEditService pokemonEditService,
        UserContext userContext)
    {
        _saveFileService = saveFileService;
        _pokemonEditService = pokemonEditService;
        _userContext = userContext;
    }

    /// <summary>
    /// 列出当前用户所有存档
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<SaveFileDto>>>> List()
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<List<SaveFileDto>>();

        var saves = await _saveFileService.GetUserSaves(userId.Value);
        return Ok(ApiResponse<List<SaveFileDto>>.Ok(saves));
    }

    /// <summary>
    /// 上传并解析存档文件
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(16 * 1024 * 1024)] // 16 MB
    public async Task<ActionResult<ApiResponse<SaveFileDetailDto>>> Upload(IFormFile file)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<SaveFileDetailDto>();

        if (file == null || file.Length == 0)
            return BadRequest(ErrorMessage<SaveFileDetailDto>(400, "save.uploadFileRequired"));

        if (file.Length > 16 * 1024 * 1024)
            return BadRequest(ErrorMessage<SaveFileDetailDto>(400, "save.fileTooLarge"));

        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var rawData = ms.ToArray();

            var result = await _saveFileService.UploadSave(userId.Value, rawData, file.FileName);
            return Ok(OkMessage(result, "save.uploadSuccess"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<SaveFileDetailDto>(ex));
        }
    }

    /// <summary>
    /// 获取存档详情（含所有箱子数据）
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<SaveFileDetailDto>>> Detail(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<SaveFileDetailDto>();

        try
        {
            var result = await _saveFileService.GetSaveDetail(id, userId.Value);
            return Ok(ApiResponse<SaveFileDetailDto>.Ok(result));
        }
        catch (BusinessException ex)
        {
            return NotFound(FromBusinessException<SaveFileDetailDto>(ex));
        }
    }

    /// <summary>
    /// 存档内部移动/交换宝可梦
    /// </summary>
    [HttpPost("{id:guid}/move-slot")]
    public async Task<ActionResult<ApiResponse<object>>> MoveSlot(Guid id, [FromBody] MoveSlotRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<object>();

        try
        {
            await _saveFileService.MoveSlot(id, userId.Value,
                request.FromBoxIndex, request.FromSlotIndex,
                request.ToBoxIndex, request.ToSlotIndex);

            return Ok(OkMessage(new { }, "save.moveSuccess"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<object>(ex));
        }
    }

    /// <summary>
    /// 从银行拖入宝可梦到存档
    /// </summary>
    [HttpPost("{id:guid}/move-from-bank")]
    public async Task<ActionResult<ApiResponse<object>>> MoveFromBank(Guid id, [FromBody] MoveFromBankRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<object>();

        try
        {
            await _saveFileService.MoveFromBank(id, userId.Value,
                request.BankPokemonId, request.TargetBoxIndex, request.TargetSlotIndex);
            return Ok(OkMessage(new { }, "save.moveFromBankSuccess"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<object>(ex));
        }
    }

    /// <summary>
    /// 删除存档
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<object>();

        try
        {
            await _saveFileService.DeleteSave(id, userId.Value);
            return Ok(OkMessage(new { }, "save.deleteSuccess"));
        }
        catch (BusinessException ex)
        {
            return NotFound(FromBusinessException<object>(ex));
        }
    }

    /// <summary>
    /// 存档备份管理
    /// </summary>
    [HttpPost("{id:guid}/save")]
    public async Task<ActionResult<ApiResponse<object>>> Save(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<object>();
        await _saveFileService.CreateBackup(id, userId.Value, "手动保存");
        return Ok(OkMessage(new { }, "save.backupCreated"));
    }

    [HttpGet("{id:guid}/backups")]
    public async Task<ActionResult<ApiResponse<List<SaveBackupDto>>>> ListBackups(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<List<SaveBackupDto>>();
        try
        {
            var backups = await _saveFileService.ListBackups(id, userId.Value);
            return Ok(ApiResponse<List<SaveBackupDto>>.Ok(backups));
        }
        catch (BusinessException ex) { return NotFound(FromBusinessException<List<SaveBackupDto>>(ex)); }
    }

    /// <summary>下载原始存档二进制（供模拟器使用）</summary>
    [HttpGet("{id:guid}/raw")]
    public async Task<IActionResult> DownloadRaw(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized();
        try
        {
            var (data, _) = await _saveFileService.GetDownloadData(id, userId.Value);
            return File(data, "application/octet-stream", $"save_{id}.sav");
        }
        catch (BusinessException) { return NotFound(); }
    }

    [HttpPost("{id:guid}/backups/{backupId:guid}/restore")]
    public async Task<ActionResult<ApiResponse<object>>> RestoreBackup(Guid id, Guid backupId)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<object>();
        try
        {
            await _saveFileService.RestoreBackup(id, userId.Value, backupId);
            return Ok(OkMessage(new { }, "save.restoreSuccess"));
        }
        catch (BusinessException ex) { return NotFound(FromBusinessException<object>(ex)); }
    }

    /// <summary>
    /// 下载存档文件
    /// </summary>
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized();

        try
        {
            var (data, filename) = await _saveFileService.GetDownloadData(id, userId.Value);
            return File(data, "application/octet-stream", filename);
        }
        catch (BusinessException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// 全存档合法性批量扫描
    /// </summary>
    [HttpPost("{id:guid}/legality-report")]
    public async Task<ActionResult<ApiResponse<BatchLegalityReportDto>>> BatchLegalityReport(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<BatchLegalityReportDto>();

        try
        {
            var report = await _saveFileService.BatchLegalityScan(id, userId.Value, _pokemonEditService);
            return Ok(OkMessage(report, "save.batchLegalityScanComplete",
                report.Total, report.LegalCount, report.FishyCount, report.IllegalCount));
        }
        catch (BusinessException ex)
        {
            return NotFound(FromBusinessException<BatchLegalityReportDto>(ex));
        }
    }

    /// <summary>
    /// 获取存档背包（道具列表）
    /// </summary>
    [HttpGet("{id:guid}/bag")]
    public async Task<ActionResult<ApiResponse<BagDto>>> GetBag(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<BagDto>();

        try
        {
            var bag = await _saveFileService.GetBag(id, userId.Value);
            return Ok(ApiResponse<BagDto>.Ok(bag));
        }
        catch (BusinessException ex)
        {
            return NotFound(FromBusinessException<BagDto>(ex));
        }
    }

    /// <summary>
    /// 保存背包变更
    /// </summary>
    [HttpPut("{id:guid}/bag")]
    public async Task<ActionResult<ApiResponse<object>>> SaveBag(Guid id, [FromBody] BagDto bag)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<object>();

        try
        {
            await _saveFileService.SaveBag(id, userId.Value, bag);
            return Ok(OkMessage(new { }, "save.bagSaved"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<object>(ex));
        }
    }

    /// <summary>
    /// 获取训练家信息
    /// </summary>
    [HttpGet("{id:guid}/trainer")]
    public async Task<ActionResult<ApiResponse<TrainerInfoDto>>> GetTrainer(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<TrainerInfoDto>();

        try
        {
            var info = await _saveFileService.GetTrainerInfo(id, userId.Value);
            return Ok(ApiResponse<TrainerInfoDto>.Ok(info));
        }
        catch (BusinessException ex)
        {
            return NotFound(FromBusinessException<TrainerInfoDto>(ex));
        }
    }

    /// <summary>
    /// 保存训练家信息变更
    /// </summary>
    [HttpPut("{id:guid}/trainer")]
    public async Task<ActionResult<ApiResponse<object>>> SaveTrainer(Guid id, [FromBody] TrainerInfoDto info)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<object>();

        try
        {
            await _saveFileService.SaveTrainerInfo(id, userId.Value, info);
            return Ok(OkMessage(new { }, "save.trainerSaved"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<object>(ex));
        }
    }

    /// <summary>
    /// 获取存档图鉴（seen/caught 条目列表 + 统计数据）
    /// </summary>
    [HttpGet("{id:guid}/pokedex")]
    public async Task<ActionResult<ApiResponse<PokedexDto>>> GetPokedex(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<PokedexDto>();

        try
        {
            var dto = await _saveFileService.GetPokedex(id, userId.Value);
            return Ok(ApiResponse<PokedexDto>.Ok(dto));
        }
        catch (BusinessException ex)
        {
            return NotFound(FromBusinessException<PokedexDto>(ex));
        }
    }

    /// <summary>
    /// 保存图鉴变更（seen/caught 切换）
    /// </summary>
    [HttpPut("{id:guid}/pokedex")]
    public async Task<ActionResult<ApiResponse<object>>> SavePokedex(Guid id, [FromBody] PokedexDto dto)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<object>();

        try
        {
            await _saveFileService.SavePokedex(id, userId.Value, dto);
            return Ok(OkMessage(new { }, "save.pokedexSaved"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<object>(ex));
        }
    }

    /// <summary>
    /// 图鉴批量操作 — seenAll / caughtAll / clearAll
    /// </summary>
    [HttpPost("{id:guid}/pokedex/batch")]
    public async Task<ActionResult<ApiResponse<PokedexDto>>> BatchPokedex(Guid id, [FromBody] PokedexBatchRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<PokedexDto>();

        try
        {
            if (request?.Action == null)
                return BadRequest(ErrorMessage<PokedexDto>(400, "save.batchActionRequired"));
            var result = await _saveFileService.BatchPokedex(id, userId.Value, request.Action);
            return Ok(ApiResponse<PokedexDto>.Ok(result));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<PokedexDto>(ex));
        }
    }

    /// <summary>
    /// 交换两个箱子的全部宝可梦
    /// </summary>
    [HttpPost("{id:guid}/swap-boxes")]
    public async Task<ActionResult<ApiResponse<object>>> SwapBoxes(Guid id, [FromBody] SwapBoxesRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<object>();

        try
        {
            await _saveFileService.SwapBoxes(id, userId.Value, request.BoxIndexA, request.BoxIndexB);
            return Ok(OkMessage(new { }, "save.boxesSwapped"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<object>(ex));
        }
    }

    /// <summary>
    /// 对所有箱子分别执行内部排序
    /// </summary>
    [HttpPost("{id:guid}/sortBoxes")]
    public async Task<ActionResult<ApiResponse<object>>> SortBoxes(Guid id, [FromBody] SortBoxesRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<object>();

        try
        {
            await _saveFileService.SortAllBoxes(id, userId.Value, request.SortBy);
            return Ok(OkMessage(new { }, "save.sortBoxesCompleted", GetSortLabel(request.SortBy)));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<object>(ex));
        }
    }

    /// <summary>
    /// 对单个箱子执行内部排序
    /// </summary>
    [HttpPost("{id:guid}/sortBox")]
    public async Task<ActionResult<ApiResponse<object>>> SortBox(Guid id, [FromBody] SortBoxRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<object>();

        try
        {
            await _saveFileService.SortBox(id, userId.Value, request.BoxIndex, request.SortBy);
            return Ok(OkMessage(new { }, "save.sortBoxCompleted", GetSortLabel(request.SortBy)));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<object>(ex));
        }
    }

    /// <summary>
    /// 创建新游戏空白存档（用于模拟器新游戏入口）
    /// </summary>
    [HttpPost("new-game")]
    public async Task<ActionResult<ApiResponse<SaveFileDetailDto>>> NewGame([FromBody] NewGameRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<SaveFileDetailDto>();

        try
        {
            var result = await _saveFileService.CreateNewGame(userId.Value, request.GameId);
            return Ok(OkMessage(result, "save.newGameCreated"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<SaveFileDetailDto>(ex));
        }
    }

    /// <summary>
    /// 获取存档世代专属工具数据（当前仅 RTC 时钟）
    /// </summary>
    [HttpGet("{id:guid}/gen-tools")]
    public async Task<ActionResult<ApiResponse<GenToolsDto>>> GetGenTools(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<GenToolsDto>();

        try
        {
            var result = await _saveFileService.GetGenTools(id, userId.Value);
            return Ok(ApiResponse<GenToolsDto>.Ok(result));
        }
        catch (BusinessException ex)
        {
            return NotFound(FromBusinessException<GenToolsDto>(ex));
        }
    }

    /// <summary>
    /// 保存世代专属工具数据（当前仅 RTC 时钟）
    /// </summary>
    [HttpPut("{id:guid}/gen-tools")]
    public async Task<ActionResult<ApiResponse<object>>> SaveGenTools(Guid id, [FromBody] GenToolsDto dto)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<object>();

        try
        {
            await _saveFileService.SaveGenTools(id, userId.Value, dto);
            return Ok(OkMessage(new { }, "save.rtcSaved"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(FromBusinessException<object>(ex));
        }
    }

    private static string GetSortLabel(string? sortBy) => sortBy?.Trim().ToLowerInvariant() switch
    {
        "species" => "物种编号",
        "level" => "等级",
        "shiny" => "闪光优先",
        "name" => "名称",
        _ => "指定方式",
    };

    /// <summary>
    /// 高级搜索 — 在当前存档中按多条件筛选宝可梦。
    /// </summary>
    [HttpPost("{id:guid}/search")]
    public async Task<ActionResult<ApiResponse<PokemonSearchResultDto>>> Search(Guid id,
        [FromBody] PokemonSearchRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return UnauthorizedMessage<PokemonSearchResultDto>();

        try
        {
            var result = await _saveFileService.SearchSave(id, userId.Value, request);
            return Ok(ApiResponse<PokemonSearchResultDto>.Ok(result));
        }
        catch (BusinessException ex)
        {
            return ex.ErrorCode == 404
                ? NotFound(FromBusinessException<PokemonSearchResultDto>(ex))
                : BadRequest(FromBusinessException<PokemonSearchResultDto>(ex));
        }
    }
}

public class MoveFromBankRequest
{
    public Guid BankPokemonId { get; set; }
    public int TargetBoxIndex { get; set; }
    public int TargetSlotIndex { get; set; }
}

public class MoveSlotRequest
{
    public int FromBoxIndex { get; set; }
    public int FromSlotIndex { get; set; }
    public int ToBoxIndex { get; set; }
    public int ToSlotIndex { get; set; }
}

public class SwapBoxesRequest
{
    public int BoxIndexA { get; set; }
    public int BoxIndexB { get; set; }
}

public class SortBoxesRequest
{
    public string SortBy { get; set; } = "species";
}

public class SortBoxRequest
{
    public int BoxIndex { get; set; }
    public string SortBy { get; set; } = "species";
}

public class NewGameRequest
{
    public string GameId { get; set; } = "pkm_emerald";
}
