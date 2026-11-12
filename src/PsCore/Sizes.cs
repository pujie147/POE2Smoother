namespace PsCore;

/// <summary>
/// Size formatting shared by the CLI and the GUI. It used to live in PsCli only, which the GUI
/// cannot reference (PsCli is the executable), and duplicating it would let the two frontends
/// report the same plan differently.
/// </summary>
public static class Sizes
{
    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.##} {units[unit]}";
    }
}
