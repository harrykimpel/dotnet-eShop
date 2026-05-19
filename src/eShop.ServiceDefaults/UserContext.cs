using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;

namespace eShop.ServiceDefaults;

// OTel semantic convention: https://opentelemetry.io/docs/specs/semconv/registry/attributes/user/
public static class UserContext
{
    public const string UserIdAttribute = "user.id";

    public static string? TryGetUserId(HttpContext? context)
    {
        if (context is null)
        {
            return null;
        }

        // 1. Authenticated principal (cookie auth in WebApp, validated JWT in Ordering/Basket).
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var sub = context.User.FindFirst("sub")?.Value
                   ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!string.IsNullOrEmpty(sub))
            {
                return sub;
            }
        }

        // 2. Fallback: decode the bearer token without validation (for services
        //    that don't validate JWTs locally, e.g. Catalog.API). For telemetry only.
        return TryDecodeBearer(context);
    }

    private static string? TryDecodeBearer(HttpContext context)
    {
        if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return null;
        }

        var raw = authHeader.ToString();
        const string prefix = "Bearer ";
        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var token = raw[prefix.Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        try
        {
            var handler = new JsonWebTokenHandler();
            if (!handler.CanReadToken(token))
            {
                return null;
            }
            var jwt = handler.ReadJsonWebToken(token);
            return jwt.GetClaim("sub")?.Value ?? jwt.Subject;
        }
        catch
        {
            return null;
        }
    }
}
