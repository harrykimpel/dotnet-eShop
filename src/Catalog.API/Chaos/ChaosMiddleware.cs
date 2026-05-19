using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace eShop.Catalog.API.Chaos;

public class ChaosMiddleware
{
    public const string ModeHeaderName = "X-Chaos-Mode";
    public const string ProductsHeaderName = "X-Chaos-Products";
    public const string ModeQueryName = "chaos";
    public const string ProductsQueryName = "chaos-products";

    private static readonly Random _rng = Random.Shared;

    private readonly RequestDelegate _next;
    private readonly ILogger<ChaosMiddleware> _logger;
    private readonly IOptionsMonitor<ChaosOptions> _options;

    public ChaosMiddleware(RequestDelegate next, ILogger<ChaosMiddleware> logger, IOptionsMonitor<ChaosOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = _options.CurrentValue;
        var modes = ResolveModes(context, options);

        if (modes.Length == 0)
        {
            await _next(context);
            return;
        }

        var products = ResolveProducts(context, options);

        // Product scope set + this isn't a per-product request → skip chaos.
        if (products.Length > 0)
        {
            var requestProductId = TryExtractProductId(context.Request.Path);
            if (requestProductId is null || !products.Contains(requestProductId.Value))
            {
                await _next(context);
                return;
            }
            Activity.Current?.SetTag("chaos.products", string.Join(",", products));
        }

        Activity.Current?.SetTag("chaos.modes", string.Join(",", modes));
        _logger.LogWarning("Chaos active for {Path}: {Modes}", context.Request.Path, string.Join(",", modes));

        // Memory pressure: allocate and hold for the duration of the request.
        byte[]? ballast = null;
        if (modes.Contains("memory", StringComparer.OrdinalIgnoreCase))
        {
            var bytes = Math.Max(1, options.MemoryAllocationMb) * 1024 * 1024;
            ballast = new byte[bytes];
            for (var i = 0; i < ballast.Length; i += 4096)
            {
                ballast[i] = 1;
            }
            _logger.LogWarning("Chaos: allocated {Mb} MB of ballast memory", options.MemoryAllocationMb);
        }

        if (modes.Contains("slow", StringComparer.OrdinalIgnoreCase))
        {
            await Task.Delay(options.SlowDelayMs, context.RequestAborted);
        }

        if (modes.Contains("flaky", StringComparer.OrdinalIgnoreCase)
            && _rng.NextDouble() < options.FlakyProbability)
        {
            _logger.LogWarning("Chaos: returning synthetic 500 for {Path}", context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync("""{"title":"Synthetic chaos failure","status":500,"detail":"Injected by ChaosMiddleware (mode=flaky)."}""");
            GC.KeepAlive(ballast);
            return;
        }

        try
        {
            await _next(context);
        }
        finally
        {
            GC.KeepAlive(ballast);
        }
    }

    private static string[] ResolveModes(HttpContext context, ChaosOptions options)
    {
        if (context.Request.Query.TryGetValue(ModeQueryName, out var queryValues) && queryValues.Count > 0)
        {
            var raw = queryValues.ToString();
            if (IsClearValue(raw))
            {
                return [];
            }
            return SplitStrings(raw);
        }

        if (context.Request.Headers.TryGetValue(ModeHeaderName, out var headerValues) && headerValues.Count > 0)
        {
            return SplitStrings(headerValues.ToString());
        }

        return options.DefaultModes ?? [];
    }

    private static int[] ResolveProducts(HttpContext context, ChaosOptions options)
    {
        if (context.Request.Query.TryGetValue(ProductsQueryName, out var queryValues) && queryValues.Count > 0)
        {
            var raw = queryValues.ToString();
            if (IsClearValue(raw))
            {
                return [];
            }
            return SplitInts(raw);
        }

        if (context.Request.Headers.TryGetValue(ProductsHeaderName, out var headerValues) && headerValues.Count > 0)
        {
            return SplitInts(headerValues.ToString());
        }

        return options.Products ?? [];
    }

    // Matches /api/catalog/items/{id} and /api/catalog/items/{id}/pic. Returns null otherwise.
    private static int? TryExtractProductId(PathString path)
    {
        const string prefix = "/api/catalog/items/";
        var raw = path.Value;
        if (string.IsNullOrEmpty(raw) || !raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = raw[prefix.Length..];
        var slashIdx = rest.IndexOf('/');
        var idSegment = slashIdx >= 0 ? rest[..slashIdx] : rest;
        return int.TryParse(idSegment, out var id) ? id : null;
    }

    private static bool IsClearValue(string raw)
        => string.Equals(raw, "off", StringComparison.OrdinalIgnoreCase)
        || string.Equals(raw, "clear", StringComparison.OrdinalIgnoreCase)
        || string.Equals(raw, "none", StringComparison.OrdinalIgnoreCase);

    private static string[] SplitStrings(string raw)
        => raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int[] SplitInts(string raw)
        => SplitStrings(raw)
            .Select(s => int.TryParse(s, out var n) ? n : (int?)null)
            .Where(n => n.HasValue)
            .Select(n => n!.Value)
            .ToArray();
}
