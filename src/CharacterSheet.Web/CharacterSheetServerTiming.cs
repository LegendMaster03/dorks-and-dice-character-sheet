using System.Diagnostics;
using System.Globalization;

namespace CharacterSheet.Web;

public static class CharacterSheetServerTiming
{
    public const string HeaderName = "Server-Timing";
    public const string RequestMetricName = "character-sheet";
    public const string AuthenticationMetricName = "character-sheet-auth";
    public const string RulesCoreDependencyMetricName = "character-sheet-rules-core";

    private static readonly object RequestStateKey = new();

    public static void EnsureRequestTiming(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.Items.ContainsKey(RequestStateKey))
        {
            return;
        }

        var startedAt = Stopwatch.GetTimestamp();
        httpContext.Items[RequestStateKey] = startedAt;
        httpContext.Response.OnStarting(() =>
        {
            AppendDuration(
                httpContext,
                RequestMetricName,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
            return Task.CompletedTask;
        });
    }

    public static void AppendDuration(
        HttpContext? httpContext,
        string metricName,
        double durationMilliseconds,
        string? description = null)
    {
        if (httpContext is null
            || httpContext.Response.HasStarted
            || !double.IsFinite(durationMilliseconds)
            || durationMilliseconds < 0)
        {
            return;
        }

        var metric = metricName;
        if (!string.IsNullOrWhiteSpace(description))
        {
            metric += $";desc=\"{description}\"";
        }

        metric += $";dur={durationMilliseconds.ToString("0.###", CultureInfo.InvariantCulture)}";
        httpContext.Response.Headers.Append(HeaderName, metric);
    }

    public static void AppendDownstreamRulesCoreMetrics(
        HttpContext? httpContext,
        HttpResponseMessage downstreamResponse)
    {
        if (httpContext is null
            || httpContext.Response.HasStarted
            || !downstreamResponse.Headers.TryGetValues(HeaderName, out var values))
        {
            return;
        }

        foreach (var value in values)
        {
            foreach (var metric in SplitMetrics(value))
            {
                var separator = metric.IndexOf(';');
                var metricName = (separator < 0 ? metric : metric[..separator]).Trim();
                if (metricName.Equals("rules-core", StringComparison.Ordinal)
                    || metricName.StartsWith("rules-core-", StringComparison.Ordinal))
                {
                    httpContext.Response.Headers.Append(HeaderName, metric);
                }
            }
        }
    }

    private static IEnumerable<string> SplitMetrics(string headerValue)
    {
        var start = 0;
        var inQuotes = false;
        var escaped = false;

        for (var index = 0; index < headerValue.Length; index++)
        {
            var character = headerValue[index];
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (inQuotes && character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (character != ',' || inQuotes)
            {
                continue;
            }

            var metric = headerValue[start..index].Trim();
            if (metric.Length > 0)
            {
                yield return metric;
            }

            start = index + 1;
        }

        var finalMetric = headerValue[start..].Trim();
        if (finalMetric.Length > 0)
        {
            yield return finalMetric;
        }
    }
}
