namespace eShop.WebApp.Chaos;

public class ChaosOptions
{
    public int SlowDelayMs { get; set; } = 2500;

    public string BrokenImageUrl { get; set; } = "product-images/0/missing?api-version=2.0";

    public string[] DefaultModes { get; set; } = [];

    public int[] Products { get; set; } = [];
}
