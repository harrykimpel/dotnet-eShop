using eShop.WebAppComponents.Services;
using Microsoft.Extensions.Options;

namespace eShop.WebApp.Services;

public class ProductImageUrlProvider : IProductImageUrlProvider
{
    private readonly ChaosState _chaosState;
    private readonly IOptionsMonitor<ChaosOptions> _options;

    public ProductImageUrlProvider(ChaosState chaosState, IOptionsMonitor<ChaosOptions> options)
    {
        _chaosState = chaosState;
        _options = options;
    }

    public string GetProductImageUrl(int productId)
    {
        if (_chaosState.IsActive("broken-images") && _chaosState.IsProductInScope(productId))
        {
            return _options.CurrentValue.BrokenImageUrl;
        }
        return $"product-images/{productId}?api-version=2.0";
    }
}
