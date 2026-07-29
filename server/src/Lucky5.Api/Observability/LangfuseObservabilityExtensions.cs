using System.Diagnostics;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Routing;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Lucky5.Api.Observability;

public static class LangfuseObservabilityExtensions
{
    private const string ServiceName = "lucky5-api";

    private static readonly string[] BaggageKeysToPromote =
    [
        "langfuse.user.id",
        "langfuse.session.id",
        "langfuse.release",
        "langfuse.environment",
        "langfuse.trace.metadata.feature",
        "langfuse.trace.metadata.machine_id",
        "langfuse.trace.metadata.http_route"
    ];

    public static IServiceCollection AddLangfuseObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        IWebHostEnvironment environment)
    {
        var publicKey = FirstConfigured(configuration, "LANGFUSE_PUBLIC_KEY", "Langfuse:PublicKey");
        var secretKey = FirstConfigured(configuration, "LANGFUSE_SECRET_KEY", "Langfuse:SecretKey");
        var baseUrl = FirstConfigured(configuration, "LANGFUSE_BASE_URL", "Langfuse:BaseUrl");

        if (string.IsNullOrWhiteSpace(publicKey)
            || string.IsNullOrWhiteSpace(secretKey)
            || string.IsNullOrWhiteSpace(baseUrl)
            || IsExplicitlyDisabled(configuration))
        {
            return services;
        }

        if (!Uri.TryCreate(baseUrl.TrimEnd('/'), UriKind.Absolute, out var langfuseBaseUri))
        {
            throw new InvalidOperationException("LANGFUSE_BASE_URL must be an absolute URL such as https://cloud.langfuse.com.");
        }

        var tracesEndpoint = new Uri(langfuseBaseUri, "/api/public/otel/v1/traces");
        var authHeader = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{publicKey}:{secretKey}"));
        var release = FirstConfigured(configuration, "LANGFUSE_RELEASE", "Langfuse:Release")
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "unknown";
        var deploymentEnvironment = FirstConfigured(configuration, "LANGFUSE_ENVIRONMENT", "Langfuse:Environment")
            ?? environment.EnvironmentName;
        var sampleRatio = GetSampleRatio(configuration);

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: FirstConfigured(configuration, "LANGFUSE_SERVICE_NAME", "Langfuse:ServiceName") ?? ServiceName,
                    serviceVersion: release,
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes(
                [
                    new KeyValuePair<string, object>("deployment.environment.name", deploymentEnvironment),
                    new KeyValuePair<string, object>("langfuse.environment", deploymentEnvironment),
                    new KeyValuePair<string, object>("langfuse.release", release)
                ]))
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new TraceIdRatioBasedSampler(sampleRatio))
                    .AddProcessor(new LangfuseBaggageSpanProcessor(BaggageKeysToPromote))
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = ShouldTraceRequest;
                        options.RecordException = true;
                    })
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = tracesEndpoint;
                        options.Protocol = OtlpExportProtocol.HttpProtobuf;
                        options.Headers = $"Authorization=Basic {authHeader},x-langfuse-ingestion-version=4";
                    });
            });

        return services;
    }

    public static IApplicationBuilder UseLangfuseTraceContext(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            try
            {
                var activity = Activity.Current;
                if (activity is not null && ShouldTraceRequest(context))
                {
                    AnnotateRequestSpan(context, activity);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LangfuseTrace] Pre-next exception: {ex.Message}");
            }

            await next();

            try
            {
                var activity = Activity.Current;
                if (activity is not null && ShouldTraceRequest(context))
                {
                    activity.SetTag("langfuse.observation.metadata.http_status_code", context.Response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LangfuseTrace] Post-next exception: {ex.Message}");
            }
        });
    }

    private static bool IsExplicitlyDisabled(IConfiguration configuration)
    {
        var value = FirstConfigured(configuration, "LANGFUSE_ENABLED", "Langfuse:Enabled");
        return bool.TryParse(value, out var enabled) && !enabled;
    }

    private static string? FirstConfigured(IConfiguration configuration, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static double GetSampleRatio(IConfiguration configuration)
    {
        var raw = FirstConfigured(configuration, "LANGFUSE_TRACE_SAMPLE_RATIO", "Langfuse:TraceSampleRatio");
        if (!double.TryParse(raw, out var value))
        {
            return 1.0d;
        }

        return Math.Clamp(value, 0.0d, 1.0d);
    }

    private static bool ShouldTraceRequest(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        return IsPath(path, "/api") || IsPath(path, "/CarrePokerGameHub");
    }

    private static bool IsPath(string requestPath, string prefix)
    {
        return requestPath.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || requestPath.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
    }

    private static void AnnotateRequestSpan(HttpContext context, Activity activity)
    {
        var traceName = BuildTraceName(context);
        var feature = InferFeature(context.Request.Path.Value ?? string.Empty);
        var release = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";

        activity.SetTag("langfuse.trace.name", traceName);
        activity.SetTag("langfuse.trace.tags", string.Join(",", "lucky5", "api", feature));
        activity.SetTag("langfuse.release", release);
        activity.SetTag("langfuse.trace.metadata.feature", feature);
        activity.SetTag("langfuse.display-name", traceName);

        AddBaggage(activity, "langfuse.release", release);
        AddBaggage(activity, "langfuse.trace.metadata.feature", feature);

        var environmentName = context.RequestServices.GetService<IWebHostEnvironment>()?.EnvironmentName;
        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            SetTagAndBaggage(activity, "langfuse.environment", environmentName);
        }

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId))
        {
            SetTagAndBaggage(activity, "langfuse.user.id", userId);
        }

        var sessionId = FirstRequestValue(context, "sessionId", "X-Lucky5-Session-Id", "X-Session-Id", "X-Langfuse-Session-Id");
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            SetTagAndBaggage(activity, "langfuse.session.id", sessionId);
        }

        var machineId = FirstRequestValue(context, "machineId", "X-Lucky5-Machine-Id");
        if (!string.IsNullOrWhiteSpace(machineId))
        {
            SetTagAndBaggage(activity, "langfuse.trace.metadata.machine_id", machineId);
        }

        var route = TryGetRoutePattern(context);
        if (!string.IsNullOrWhiteSpace(route))
        {
            SetTagAndBaggage(activity, "langfuse.trace.metadata.http_route", route);
        }
    }

    private static string BuildTraceName(HttpContext context)
    {
        var route = TryGetRoutePattern(context);
        return string.IsNullOrWhiteSpace(route)
            ? $"{context.Request.Method} {context.Request.Path.Value}"
            : $"{context.Request.Method} {route}";
    }

    private static string InferFeature(string path)
    {
        if (path.StartsWith("/CarrePokerGameHub", StringComparison.OrdinalIgnoreCase))
        {
            return "realtime";
        }

        if (path.Contains("/auth", StringComparison.OrdinalIgnoreCase))
        {
            return "auth";
        }

        if (path.Contains("/admin", StringComparison.OrdinalIgnoreCase))
        {
            return "admin";
        }

        if (path.Contains("/game", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/cabinet", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/machine", StringComparison.OrdinalIgnoreCase))
        {
            return "gameplay";
        }

        return "api";
    }

    private static string? TryGetRoutePattern(HttpContext context)
    {
        return context.GetEndpoint() is RouteEndpoint endpoint
            ? endpoint.RoutePattern.RawText
            : null;
    }

    private static string? FirstRequestValue(
        HttpContext context,
        string routeOrQueryKey,
        params string[] headerKeys)
    {
        if (context.Request.RouteValues.TryGetValue(routeOrQueryKey, out var routeValue)
            && routeValue is not null)
        {
            return routeValue.ToString();
        }

        if (context.Request.Query.TryGetValue(routeOrQueryKey, out var queryValue)
            && !string.IsNullOrWhiteSpace(queryValue.ToString()))
        {
            return queryValue.ToString();
        }

        foreach (var headerKey in headerKeys)
        {
            if (context.Request.Headers.TryGetValue(headerKey, out var headerValue)
                && !string.IsNullOrWhiteSpace(headerValue.ToString()))
            {
                return headerValue.ToString();
            }
        }

        return null;
    }

    private static void SetTagAndBaggage(Activity activity, string key, string value)
    {
        activity.SetTag(key, value);
        AddBaggage(activity, key, value);
    }

    private static void AddBaggage(Activity activity, string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            activity.AddBaggage(key, value);
        }
    }

    private sealed class LangfuseBaggageSpanProcessor(IEnumerable<string> baggageKeys) : BaseProcessor<Activity>
    {
        private readonly HashSet<string> _baggageKeys = new(baggageKeys, StringComparer.Ordinal);

        public override void OnStart(Activity activity)
        {
            foreach (var item in activity.Baggage)
            {
                if (item.Value is not null && _baggageKeys.Contains(item.Key))
                {
                    activity.SetTag(item.Key, item.Value);
                }
            }
        }
    }
}
