namespace Lucky5.Api.Middleware;

using System.Net;
using System.Text.Json;
using Lucky5.Application.Dtos;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await WriteErrorResponse(context, ex);
        }
    }

    private async Task WriteErrorResponse(HttpContext context, Exception ex)
    {
        var (status, message) = ex switch
        {
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, ex.Message),
            KeyNotFoundException => (HttpStatusCode.NotFound, ex.Message),
            InvalidOperationException => (HttpStatusCode.BadRequest, ex.Message),
            _ => (HttpStatusCode.InternalServerError, "Unexpected server error")
        };

        logger.LogError(
            ex,
            "Unhandled API request failure {Method} {Path} -> {StatusCode} ({TraceId})",
            context.Request.Method,
            context.Request.Path,
            (int)status,
            context.TraceIdentifier);

        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";

        var payload = ApiResponse<object>.Fail(
            message,
            errors: status == HttpStatusCode.InternalServerError ? [] : [ex.Message],
            traceId: context.TraceIdentifier);

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
