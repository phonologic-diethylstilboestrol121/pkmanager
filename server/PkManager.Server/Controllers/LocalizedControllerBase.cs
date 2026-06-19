using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using PkManager.Server.Localization;
using PkManager.Server.Models.Response;
using PkManager.Server.Services;

namespace PkManager.Server.Controllers;

public abstract class LocalizedControllerBase : ControllerBase
{
    private IBackendMessageLocalizer? _messages;

    protected IBackendMessageLocalizer Messages =>
        _messages ??= HttpContext.RequestServices.GetRequiredService<IBackendMessageLocalizer>();

    protected string Text(string key, params object?[] args) =>
        Messages.Get(key, args);

    protected string TextOrFallback(string key, string? fallbackMessage, params object?[] args) =>
        Messages.GetOrFallback(null, key, fallbackMessage, args);

    protected ApiResponse<T> OkMessage<T>(T data, string key, params object?[] args) =>
        ApiResponse<T>.Ok(data, Messages.Get(key, args), key);

    protected ApiResponse<T> OkMessageFallback<T>(T data, string key, string? fallbackMessage, params object?[] args) =>
        ApiResponse<T>.Ok(data, Messages.GetOrFallback(null, key, fallbackMessage, args), key);

    protected ApiResponse<T> ErrorMessage<T>(int code, string key, params object?[] args) =>
        ApiResponse<T>.Error(code, Messages.Get(key, args), key);

    protected ApiResponse<T> ErrorMessageFallback<T>(int code, string key, string? fallbackMessage, params object?[] args) =>
        ApiResponse<T>.Error(code, Messages.GetOrFallback(null, key, fallbackMessage, args), key);

    protected ActionResult<ApiResponse<T>> UnauthorizedMessage<T>() =>
        Unauthorized(ErrorMessage<T>(401, "common.unauthorized"));

    protected ActionResult<ApiResponse<T>> FromBusinessExceptionResult<T>(BusinessException ex) =>
        StatusCode(MapStatusCode(ex.ErrorCode), FromBusinessException<T>(ex));

    protected ApiResponse<T> FromBusinessException<T>(BusinessException ex)
    {
        var message = !string.IsNullOrWhiteSpace(ex.MessageKey)
            ? Messages.GetOrFallback(null, ex.MessageKey!, ex.FallbackMessage, ex.MessageArgs ?? [])
            : ex.Message;

        return ApiResponse<T>.Error(ex.ErrorCode, message, ex.MessageKey);
    }

    private static int MapStatusCode(int errorCode) =>
        errorCode is >= 100 and < 600 ? errorCode : 400;
}
