using System.Reflection;

namespace MyTools.Desktop.Services;

public enum DistributionFlavor
{
    Full,
    Lite
}

public static class DistributionInfo
{
    private const string MetadataKey = "MyToolsDistributionFlavor";

    public static DistributionFlavor Current { get; } = ReadCurrentFlavor();

    public static string ResolveVelopackChannel(string updateRing, DistributionFlavor flavor)
    {
        var normalizedRing = updateRing.Equals(UpdateService.BetaChannel, StringComparison.OrdinalIgnoreCase)
            ? UpdateService.BetaChannel
            : UpdateService.DefaultChannel;

        return flavor == DistributionFlavor.Lite
            ? $"lite-{normalizedRing}"
            : normalizedRing;
    }

    internal static DistributionFlavor Parse(string? value) =>
        value?.Equals("Lite", StringComparison.OrdinalIgnoreCase) == true
            ? DistributionFlavor.Lite
            : DistributionFlavor.Full;

    private static DistributionFlavor ReadCurrentFlavor()
    {
        var value = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == MetadataKey)?
            .Value;
        return Parse(value);
    }
}
