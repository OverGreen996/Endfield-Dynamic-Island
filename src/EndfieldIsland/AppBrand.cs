namespace EndfieldChargePlus;

/// <summary>Visible branding; internal IDs and data locations remain stable for upgrades.</summary>
public static class AppBrand
{
    public const string Repository = "https://github.com/OverGreen996/Endfield-Dynamic-Island";
    public static string Name => LocalizationManager.Text("終末地 靈動島", "Endfield Dynamic Island");
}
