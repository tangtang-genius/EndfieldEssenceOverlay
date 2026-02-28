// src/EndfieldEssenceOverlay/DebugCaptureWindow.xaml.cs
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace EndfieldEssenceOverlay;

public partial class DebugCaptureWindow : System.Windows.Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    public DebugCaptureWindow()
    {
        InitializeComponent();
        UpdateBounds();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
    }

    public void UpdateBounds()
    {
        Left   = Config.CaptureLeft;
        Top    = Config.CaptureTop;
        Width  = Config.CaptureWidth;
        Height = Config.CaptureHeight;
    }
}
