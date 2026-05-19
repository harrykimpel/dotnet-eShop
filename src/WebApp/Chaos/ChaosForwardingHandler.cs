namespace eShop.WebApp.Chaos;

public class ChaosForwardingHandler : DelegatingHandler
{
    public const string ModeHeaderName = "X-Chaos-Mode";
    public const string ProductsHeaderName = "X-Chaos-Products";

    private readonly ChaosState _state;

    public ChaosForwardingHandler(ChaosState state)
    {
        _state = state;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var modes = _state.ActiveModes;
        if (modes.Count > 0 && !request.Headers.Contains(ModeHeaderName))
        {
            request.Headers.TryAddWithoutValidation(ModeHeaderName, string.Join(",", modes));
        }

        var products = _state.ProductIds;
        if (products.Count > 0 && !request.Headers.Contains(ProductsHeaderName))
        {
            request.Headers.TryAddWithoutValidation(ProductsHeaderName, string.Join(",", products));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
