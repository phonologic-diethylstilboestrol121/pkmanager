using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace PkManager.Server.Middleware;

/// <summary>
/// 捕获所有未处理的异常，写入 backend-errors.jsonl 文件。
/// 放在中间件管道最前面，确保任何异常都能被记录。
/// </summary>
public class ExceptionLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _logDir;

    public ExceptionLoggingMiddleware(RequestDelegate next, IWebHostEnvironment env)
    {
        _next = next;
        _logDir = Path.Combine(env.ContentRootPath, "data", "logs");
        Directory.CreateDirectory(_logDir);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            LogException(context, ex);
            throw; // Let ASP.NET Core's default exception handler also process it
        }
    }

    private void LogException(HttpContext context, Exception ex)
    {
        try
        {
            var entry = new
            {
                timestamp = DateTime.UtcNow.ToString("o"),
                method = context.Request.Method,
                path = context.Request.Path.ToString(),
                query = context.Request.QueryString.ToString(),
                statusCode = context.Response.StatusCode,
                exceptionType = ex.GetType().FullName,
                message = ex.Message,
                stackTrace = ex.StackTrace,
                innerException = ex.InnerException?.Message,
            };

            var line = JsonSerializer.Serialize(entry);
            var filePath = Path.Combine(_logDir, "backend-errors.jsonl");
            File.AppendAllText(filePath, line + "\n");
        }
        catch
        {
            // Logging must never throw — fall back to console
        }
    }
}
