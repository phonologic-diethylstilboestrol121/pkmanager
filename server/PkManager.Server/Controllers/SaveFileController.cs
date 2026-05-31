using Microsoft.AspNetCore.Mvc;
using PkManager.Server.Helpers;
using PkManager.Server.Models.Response;
using PkManager.Server.Services;

namespace PkManager.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SaveFileController : ControllerBase
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
        if (userId == null) return Unauthorized(ApiResponse<List<SaveFileDto>>.Error(401, "未登录"));

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
        if (userId == null) return Unauthorized(ApiResponse<SaveFileDetailDto>.Error(401, "未登录"));

        if (file == null || file.Length == 0)
            return BadRequest(ApiResponse<SaveFileDetailDto>.Error(400, "请选择要上传的文件"));

        if (file.Length > 16 * 1024 * 1024)
            return BadRequest(ApiResponse<SaveFileDetailDto>.Error(400, "文件大小不能超过 16MB"));

        try
        {
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var rawData = ms.ToArray();

            var result = await _saveFileService.UploadSave(userId.Value, rawData, file.FileName);
            return Ok(ApiResponse<SaveFileDetailDto>.Ok(result, "存档上传并解析成功"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<SaveFileDetailDto>.Error(ex.ErrorCode, ex.Message));
        }
    }

    /// <summary>
    /// 获取存档详情（含所有箱子数据）
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ApiResponse<SaveFileDetailDto>>> Detail(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<SaveFileDetailDto>.Error(401, "未登录"));

        try
        {
            var result = await _saveFileService.GetSaveDetail(id, userId.Value);
            return Ok(ApiResponse<SaveFileDetailDto>.Ok(result));
        }
        catch (BusinessException ex)
        {
            return NotFound(ApiResponse<SaveFileDetailDto>.Error(ex.ErrorCode, ex.Message));
        }
    }

    /// <summary>
    /// 存档内部移动/交换宝可梦
    /// </summary>
    [HttpPost("{id:guid}/move-slot")]
    public async Task<ActionResult<ApiResponse<object>>> MoveSlot(Guid id, [FromBody] MoveSlotRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));

        try
        {
            await _saveFileService.MoveSlot(id, userId.Value,
                request.FromBoxIndex, request.FromSlotIndex,
                request.ToBoxIndex, request.ToSlotIndex);

            return Ok(ApiResponse<object>.Ok(new { }, "移动成功"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Error(ex.ErrorCode, ex.Message));
        }
    }

    /// <summary>
    /// 从银行拖入宝可梦到存档
    /// </summary>
    [HttpPost("{id:guid}/move-from-bank")]
    public async Task<ActionResult<ApiResponse<object>>> MoveFromBank(Guid id, [FromBody] MoveFromBankRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));

        try
        {
            await _saveFileService.MoveFromBank(id, userId.Value,
                request.BankPokemonId, request.TargetBoxIndex, request.TargetSlotIndex);
            return Ok(ApiResponse<object>.Ok(new { }, "已移入存档"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Error(ex.ErrorCode, ex.Message));
        }
    }

    /// <summary>
    /// 删除存档
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));

        try
        {
            await _saveFileService.DeleteSave(id, userId.Value);
            return Ok(ApiResponse<object>.Ok(new { }, "存档已删除"));
        }
        catch (BusinessException ex)
        {
            return NotFound(ApiResponse<object>.Error(ex.ErrorCode, ex.Message));
        }
    }

    /// <summary>
    /// 存档备份管理
    /// </summary>
    [HttpPost("{id:guid}/save")]
    public async Task<ActionResult<ApiResponse<object>>> Save(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));
        await _saveFileService.CreateBackup(id, userId.Value, "手动保存");
        return Ok(ApiResponse<object>.Ok(new { }, "存档已保存并备份"));
    }

    [HttpGet("{id:guid}/backups")]
    public async Task<ActionResult<ApiResponse<List<SaveBackupDto>>>> ListBackups(Guid id)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<List<SaveBackupDto>>.Error(401, "未登录"));
        try
        {
            var backups = await _saveFileService.ListBackups(id, userId.Value);
            return Ok(ApiResponse<List<SaveBackupDto>>.Ok(backups));
        }
        catch (BusinessException ex) { return NotFound(ApiResponse<List<SaveBackupDto>>.Error(ex.ErrorCode, ex.Message)); }
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
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));
        try
        {
            await _saveFileService.RestoreBackup(id, userId.Value, backupId);
            return Ok(ApiResponse<object>.Ok(new { }, "已从备份恢复"));
        }
        catch (BusinessException ex) { return NotFound(ApiResponse<object>.Error(ex.ErrorCode, ex.Message)); }
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
        if (userId == null) return Unauthorized(ApiResponse<BatchLegalityReportDto>.Error(401, "未登录"));

        try
        {
            var report = await _saveFileService.BatchLegalityScan(id, userId.Value, _pokemonEditService);
            return Ok(ApiResponse<BatchLegalityReportDto>.Ok(report,
                $"扫描完成: {report.Total} 只宝可梦, {report.LegalCount} 合法, {report.FishyCount} 可疑, {report.IllegalCount} 不合法"));
        }
        catch (BusinessException ex)
        {
            return NotFound(ApiResponse<BatchLegalityReportDto>.Error(ex.ErrorCode, ex.Message));
        }
    }

    /// <summary>
    /// 交换两个箱子的全部宝可梦
    /// </summary>
    [HttpPost("{id:guid}/swap-boxes")]
    public async Task<ActionResult<ApiResponse<object>>> SwapBoxes(Guid id, [FromBody] SwapBoxesRequest request)
    {
        var userId = _userContext.UserId;
        if (userId == null) return Unauthorized(ApiResponse<object>.Error(401, "未登录"));

        try
        {
            await _saveFileService.SwapBoxes(id, userId.Value, request.BoxIndexA, request.BoxIndexB);
            return Ok(ApiResponse<object>.Ok(new { }, "箱子已交换"));
        }
        catch (BusinessException ex)
        {
            return BadRequest(ApiResponse<object>.Error(ex.ErrorCode, ex.Message));
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
