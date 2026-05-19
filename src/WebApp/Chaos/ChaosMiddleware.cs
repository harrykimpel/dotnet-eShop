using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace eShop.WebApp.Chaos;

public class ChaosMiddleware
{
    public const string ModesQueryName = "chaos";
    public const string ProductsQueryName = "chaos-products";
    public const string ModesCookieName = "chaos-mode";
    public const string ProductsCookieName = "chaos-products";

    private readonly RequestDelegate _next;
    private readonly ILogger<ChaosMiddleware> _logger;
    private readonly IOptionsMonitor<ChaosOptions> _options;

    public ChaosMiddleware(RequestDelegate next, ILogger<ChaosMiddleware> logger, IOptionsMonitor<ChaosOptions> options)
    {
        _next = next;
        _logger = logger;
        _options = options;
    }

    public Task InvokeAsync(HttpContext context)
    {
        var modes = ResolveModes(context);
        var products = ResolveProducts(context);

        if (modes.Length > 0)
        {
            context.Items[ChaosState.ModesItemKey] = modes;
            Activity.Current?.SetTag("chaos.modes", string.Join(",", modes));

            if (products.Length > 0)
            {
                context.Items[ChaosState.ProductsItemKey] = products;
                Activity.Current?.SetTag("chaos.products", string.Join(",", products));
                _logger.LogWarning("Chaos active for {Path}: {Modes} (products: {Products})",
                    context.Request.Path, string.Join(",", modes), string.Join(",", products));
            }
            else
            {
                _logger.LogWarning("Chaos active for {Path}: {Modes}", context.Request.Path, string.Join(",", modes));
            }
        }

        return _next(context);
    }

    private string[] ResolveModes(HttpContext context)
    {
        if (context.Request.Query.TryGetValue(ModesQueryName, out var queryValues) && queryValues.Count > 0)
        {
            var raw = queryValues.ToString();
            if (IsClearValue(raw))
            {
                context.Response.Cookies.Delete(ModesCookieName);
                context.Response.Cookies.Delete(ProductsCookieName);
                return [];
            }

            var modes = SplitStrings(raw);
            if (modes.Length > 0)
            {
                AppendCookie(context, ModesCookieName, string.Join(",", modes));
            }
            return modes;
        }

        if (context.Request.Cookies.TryGetValue(ModesCookieName, out var cookieValue) && !string.IsNullOrWhiteSpace(cookieValue))
        {
            return SplitStrings(cookieValue);
        }

        return _options.CurrentValue.DefaultModes ?? [];
    }

    private int[] ResolveProducts(HttpContext context)
    {
        if (context.Request.Query.TryGetValue(ProductsQueryName, out var queryValues) && queryValues.Count > 0)
        {
            var raw = queryValues.ToString();
            if (IsClearValue(raw))
            {
                context.Response.Cookies.Delete(ProductsCookieName);
                return [];
            }

            var products = SplitInts(raw);
            if (products.Length > 0)
            {
                AppendCookie(context, ProductsCookieName, string.Join(",", products));
            }
            return products;
        }

        if (context.Request.Cookies.TryGetValue(ProductsCookieName, out var cookieValue) && !string.IsNullOrWhiteSpace(cookieValue))
        {
            return SplitInts(cookieValue);
        }

        return _options.CurrentValue.Products ?? [];
    }

    private static void AppendCookie(HttpContext context, string name, string value)
    {
        context.Response.Cookies.Append(name, value, new CookieOptions
        {
            HttpOnly = false,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            Path = "/",
        });
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
