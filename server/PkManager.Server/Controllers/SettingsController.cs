using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PkManager.Server.Helpers;
using PkManager.Server.Models.Response;
using PkManager.Server.Services;

namespace PkManager.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class SettingsController : LocalizedControllerBase
{
    private readonly SettingsService _settingsService;
    private readonly UserContext _userContext;

    public SettingsController(SettingsService settingsService, UserContext userContext)
    {
        _settingsService = settingsService;
        _userContext = userContext;
    }

    /// <summary>获取当前设备的模拟器配置</summary>
    [HttpGet("emulators")]
    public async Task<ActionResult<ApiResponse<Dictionary<string, string>>>> GetEmulators()
    {
        var userId = _userContext.UserId!.Value;
        var deviceId = GetDeviceId();

        var settings = await _settingsService.GetEmulatorSettings(userId, deviceId);
        return Ok(ApiResponse<Dictionary<string, string>>.Ok(settings));
    }

    /// <summary>保存模拟器配置</summary>
    [HttpPut("emulators")]
    public async Task<ActionResult<ApiResponse<Dictionary<string, string>>>> SaveEmulators(
        [FromBody] Dictionary<string, string> settings)
    {
        var userId = _userContext.UserId!.Value;
        var deviceId = GetDeviceId();

        await _settingsService.SaveEmulatorSettings(userId, deviceId, settings);

        var updated = await _settingsService.GetEmulatorSettings(userId, deviceId);
        return Ok(OkMessage(updated, "settings.saved"));
    }

    // ── helpers ──────────────────────────────────────────

    private Guid GetDeviceId()
    {
        var header = Request.Headers["X-Device-Id"].FirstOrDefault();
        if (Guid.TryParse(header, out var id)) return id;
        // Fallback: generate a server-side UUID (should not happen in normal flow)
        return Guid.NewGuid();
    }
}
