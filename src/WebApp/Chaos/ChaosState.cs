namespace eShop.WebApp.Chaos;

public class ChaosState
{
    public const string ModesItemKey = "chaos.modes";
    public const string ProductsItemKey = "chaos.products";

    private readonly IHttpContextAccessor _accessor;

    public ChaosState(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public IReadOnlyList<string> ActiveModes
    {
        get
        {
            var ctx = _accessor.HttpContext;
            if (ctx is not null && ctx.Items.TryGetValue(ModesItemKey, out var raw) && raw is string[] modes)
            {
                return modes;
            }
            return Array.Empty<string>();
        }
    }

    public IReadOnlyList<int> ProductIds
    {
        get
        {
            var ctx = _accessor.HttpContext;
            if (ctx is not null && ctx.Items.TryGetValue(ProductsItemKey, out var raw) && raw is int[] ids)
            {
                return ids;
            }
            return Array.Empty<int>();
        }
    }

    public bool IsActive(string mode)
        => ActiveModes.Any(m => string.Equals(m, mode, StringComparison.OrdinalIgnoreCase));

    // Empty product scope means "all products" (legacy broad behavior).
    public bool IsProductInScope(int productId)
        => ProductIds.Count == 0 || ProductIds.Contains(productId);
}
