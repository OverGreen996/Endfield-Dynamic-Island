namespace EndfieldChargePlus.Assistant;

public static class IslandGeometry
{
    public static double Width(double measuredText, double available) =>
        Math.Clamp(Math.Max(420, measuredText + 86), Math.Min(280, available), Math.Min(840, available));

    // Rounded transparent corners are mouse-through as well as the outer margin.
    public static bool Contains(double x, double y, double width, double height, double radius = 24)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return false;
        var r = Math.Min(radius, Math.Min(width, height) / 2);
        var cx = Math.Clamp(x, r, width - r);
        var cy = Math.Clamp(y, r, height - r);
        return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
    }
}
