namespace eShop.Catalog.API.Chaos;

public class ChaosOptions
{
    public int SlowDelayMs { get; set; } = 2500;

    public double FlakyProbability { get; set; } = 0.25;

    public int MemoryAllocationMb { get; set; } = 50;

    public string[] DefaultModes { get; set; } = [];

    public int[] Products { get; set; } = [];
}
