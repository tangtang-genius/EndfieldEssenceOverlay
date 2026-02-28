// src/EndfieldEssenceOverlay/Services/CalibrationService.cs
using System.IO;
using System.Text.Json;

namespace EndfieldEssenceOverlay.Services;

public record CaptureRegion(int Left, int Top, int Width, int Height)
{
    public string? GameWindowTitle { get; init; }
}

public static class CalibrationService
{
    public static void Save(CaptureRegion r)
    {
        var dir = Path.GetDirectoryName(Config.CalibrationPath)!;
        Directory.CreateDirectory(dir);
        var withTitle = r with { GameWindowTitle = Config.GameWindowTitle };
        var json = JsonSerializer.Serialize(withTitle, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Config.CalibrationPath, json);
    }

    public static CaptureRegion? Load()
    {
        if (!File.Exists(Config.CalibrationPath)) return null;
        try
        {
            var json = File.ReadAllText(Config.CalibrationPath);
            return JsonSerializer.Deserialize<CaptureRegion>(json);
        }
        catch { return null; }
    }

    public static void Apply(CaptureRegion r)
    {
        Config.CaptureLeft   = r.Left;
        Config.CaptureTop    = r.Top;
        Config.CaptureWidth  = r.Width;
        Config.CaptureHeight = r.Height;
        if (!string.IsNullOrWhiteSpace(r.GameWindowTitle))
            Config.GameWindowTitle = r.GameWindowTitle;
    }
}
