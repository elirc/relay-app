using System.Diagnostics;

namespace Relay.Api.Middleware;

/// <summary>
/// Logs one structured line per request: method, path, status, and elapsed
/// milliseconds. Cheap and always on (kept out of the health-probe noise).
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task Invoke(HttpContext context)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            await _next(context);
        }
        finally
        {
            var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _logger.LogInformation(
                "HTTP {Method} {Path} -> {Status} in {ElapsedMs:0.0} ms",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                elapsedMs);
        }
    }
}
