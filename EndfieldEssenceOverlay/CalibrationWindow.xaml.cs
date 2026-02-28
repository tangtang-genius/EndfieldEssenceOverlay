// src/EndfieldEssenceOverlay/CalibrationWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using EndfieldEssenceOverlay.Services;

namespace EndfieldEssenceOverlay;

public partial class CalibrationWindow : Window
{
    private Point _startPoint;
    private bool  _dragging;

    public CaptureRegion? Result { get; private set; }

    public CalibrationWindow()
    {
        InitializeComponent();
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        _startPoint = e.GetPosition(DrawCanvas);
        _dragging   = true;
        CaptureMouse();
        SelectRect.Visibility = Visibility.Visible;
        UpdateSelectionRect(_startPoint, _startPoint);
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        UpdateSelectionRect(_startPoint, e.GetPosition(DrawCanvas));
    }

    private void Window_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();

        var cur    = e.GetPosition(DrawCanvas);
        var left   = Math.Min(_startPoint.X, cur.X);
        var top    = Math.Min(_startPoint.Y, cur.Y);
        var width  = Math.Abs(cur.X - _startPoint.X);
        var height = Math.Abs(cur.Y - _startPoint.Y);

        if (width < 10 || height < 10) return; // 너무 작으면 무시

        // WPF 장치 독립 픽셀 → 화면 물리 픽셀 변환 (DPI 보정)
        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        Result = new CaptureRegion(
            (int)(left   * dpiX),
            (int)(top    * dpiY),
            (int)(width  * dpiX),
            (int)(height * dpiY));

        DialogResult = true;
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            DialogResult = false;
    }

    private void UpdateSelectionRect(Point start, Point current)
    {
        var left   = Math.Min(start.X, current.X);
        var top    = Math.Min(start.Y, current.Y);
        var width  = Math.Abs(current.X - start.X);
        var height = Math.Abs(current.Y - start.Y);

        Canvas.SetLeft(SelectRect, left);
        Canvas.SetTop(SelectRect, top);
        SelectRect.Width  = width;
        SelectRect.Height = height;
    }
}
