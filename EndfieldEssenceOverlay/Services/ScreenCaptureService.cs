// src/EndfieldEssenceOverlay/Services/ScreenCaptureService.cs
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace EndfieldEssenceOverlay.Services;

public class ScreenCaptureService
{
    [DllImport("user32.dll")] private static extern IntPtr FindWindow(string? cls, string title);

    /// <summary>원본 크기로 캡처 (전처리 없음).</summary>
    public Bitmap? CaptureRaw() => CaptureRegion();

    private static Bitmap? CaptureRegion()
    {
        var hwnd = FindWindow(null, Config.GameWindowTitle);
        if (hwnd == IntPtr.Zero) return null;

        int w = Config.CaptureWidth;
        int h = Config.CaptureHeight;
        if (w <= 0 || h <= 0) return null;

        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(Config.CaptureLeft, Config.CaptureTop, 0, 0,
                         new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);
        return bmp;
    }
}
