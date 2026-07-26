namespace RadioCompanion;

public sealed record TrackItem(string Title, long? UnixSeconds = null, bool IsRequest = false);

public sealed class AppSettings
{
    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Volume { get; set; } = 0.65;
    public bool AlwaysOnTop { get; set; }
    public bool LockPosition { get; set; }
    public bool StartWithWindows { get; set; }
    public string Theme { get; set; } = "Classic";
}
