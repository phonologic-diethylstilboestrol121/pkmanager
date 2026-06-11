using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using PkManager.Server.Models.Response;

namespace PkManager.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DiagnosticsController : ControllerBase
{
    private readonly string _logDir;

    public DiagnosticsController(IWebHostEnvironment env)
    {
        // Store logs under ContentRoot/data/logs/
        _logDir = Path.Combine(env.ContentRootPath, "data", "logs");
        Directory.CreateDirectory(_logDir);
    }

    private string LogFilePath => Path.Combine(_logDir, "client-errors.jsonl");

    /// <summary>
    /// 接收客户端错误日志（sendBeacon POST）
    /// </summary>
    [HttpPost("client-error")]
    public async Task<ActionResult<ApiResponse<object>>> PostClientError()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var body = await reader.ReadToEndAsync();

            if (string.IsNullOrWhiteSpace(body))
                return BadRequest(ApiResponse<object>.Error(400, "empty body"));

            // Validate it's valid JSON
            using var doc = JsonDocument.Parse(body);

            // Append to JSONL file
            var line = JsonSerializer.Serialize(new
            {
                receivedAt = DateTime.UtcNow.ToString("o"),
                payload = doc.RootElement
            });

            await System.IO.File.AppendAllTextAsync(LogFilePath, line + "\n");

            return Ok(ApiResponse<object>.Ok(new { logged = true }, "logged"));
        }
        catch (JsonException)
        {
            return BadRequest(ApiResponse<object>.Error(400, "invalid JSON"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<object>.Error(500, $"failed to write log: {ex.Message}"));
        }
    }

    /// <summary>
    /// 获取最近 N 小时的客户端错误报告
    /// </summary>
    [HttpGet("report")]
    public async Task<ActionResult<ApiResponse<DiagnosticsReportDto>>> GetReport(
        [FromQuery] int hours = 24)
    {
        try
        {
            if (!System.IO.File.Exists(LogFilePath))
            {
                return Ok(ApiResponse<DiagnosticsReportDto>.Ok(new DiagnosticsReportDto
                {
                    TotalErrors = 0,
                    Hours = hours,
                    RecentItems = Array.Empty<object>(),
                }));
            }

            var cutoff = DateTime.UtcNow.AddHours(-hours);
            var lines = await System.IO.File.ReadAllLinesAsync(LogFilePath);
            var items = new List<object>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<JsonElement>(line);
                    var receivedAt = entry.GetProperty("receivedAt").GetString();
                    if (DateTime.TryParse(receivedAt, out var dt) && dt >= cutoff)
                    {
                        items.Add(entry);
                    }
                }
                catch
                {
                    // Skip malformed lines
                }
            }

            return Ok(ApiResponse<DiagnosticsReportDto>.Ok(new DiagnosticsReportDto
            {
                TotalErrors = items.Count,
                Hours = hours,
                RecentItems = items.ToArray(),
            }));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<DiagnosticsReportDto>.Error(500, ex.Message));
        }
    }

    /// <summary>
    /// 清空客户端错误日志
    /// </summary>
    [HttpDelete("clear")]
    public ActionResult<ApiResponse<object>> Clear()
    {
        try
        {
            if (System.IO.File.Exists(LogFilePath))
            {
                System.IO.File.WriteAllText(LogFilePath, "");
            }
            return Ok(ApiResponse<object>.Ok(new { cleared = true }, "cleared"));
        }
        catch (Exception ex)
        {
            return StatusCode(500, ApiResponse<object>.Error(500, ex.Message));
        }
    }

    /// <summary>
    /// 获取后端异常日志
    /// </summary>
    [HttpGet("backend-errors")]
    public async Task<ActionResult<ApiResponse<DiagnosticsReportDto>>> GetBackendErrors(
        [FromQuery] int hours = 24)
    {
        var filePath = Path.Combine(_logDir, "backend-errors.jsonl");
        if (!System.IO.File.Exists(filePath))
        {
            return Ok(ApiResponse<DiagnosticsReportDto>.Ok(new DiagnosticsReportDto
            {
                TotalErrors = 0,
                Hours = hours,
                RecentItems = Array.Empty<object>(),
            }));
        }

        var cutoff = DateTime.UtcNow.AddHours(-hours);
        var lines = await System.IO.File.ReadAllLinesAsync(filePath);
        var items = new List<object>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = JsonSerializer.Deserialize<JsonElement>(line);
                var ts = entry.GetProperty("timestamp").GetString();
                if (DateTime.TryParse(ts, out var dt) && dt >= cutoff)
                {
                    items.Add(entry);
                }
            }
            catch { /* skip malformed lines */ }
        }

        return Ok(ApiResponse<DiagnosticsReportDto>.Ok(new DiagnosticsReportDto
        {
            TotalErrors = items.Count,
            Hours = hours,
            RecentItems = items.ToArray(),
        }));
    }

    /// <summary>
    /// 获取日志文件大小
    /// </summary>
    [HttpGet("stats")]
    public ActionResult<ApiResponse<object>> GetStats()
    {
        if (!System.IO.File.Exists(LogFilePath))
        {
            return Ok(ApiResponse<object>.Ok(new { exists = false, sizeBytes = 0, lineCount = 0 }));
        }

        var info = new FileInfo(LogFilePath);
        return Ok(ApiResponse<object>.Ok(new
        {
            exists = true,
            sizeBytes = info.Length,
            path = LogFilePath,
        }));
    }
}

public class DiagnosticsReportDto
{
    public int TotalErrors { get; set; }
    public int Hours { get; set; }
    public object[] RecentItems { get; set; } = Array.Empty<object>();
}
