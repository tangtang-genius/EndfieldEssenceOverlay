// src/EndfieldEssenceOverlay/MainWindow.xaml.cs
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using EndfieldEssenceOverlay.Models;
using EndfieldEssenceOverlay.Services;

namespace EndfieldEssenceOverlay;

public partial class MainWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002, SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010;

    private readonly ScreenCaptureService _capture         = new();
    private readonly TemplateMatchService _templateMatcher = new();
    private readonly EssenceMatcherService  _matcher;
    private          ScannerService?      _scanner;
    private          List<string>         _lastKeywords = [];
    private          MatchResult          _lastResult   = new(MatchStatus.Invalid);
    private readonly HotkeyService        _hotkey = new();
    private          SettingsWindow?      _settingsWindow;
    private          DebugCaptureWindow?  _debugCapture;
    private          DebugImageWindow?    _debugImage;
    private          DispatcherTimer?     _topmostTimer;

    public MainWindow()
    {
        InitializeComponent();
        EnsureDataFiles();
        _matcher = new EssenceMatcherService(Config.WeaponsJsonPath, Config.OwnedJsonPath);
        Task.Run(InitializeAsync);
    }

    private async Task InitializeAsync()
    {
        try
        {
            _templateMatcher.Initialize();
            _scanner = new ScannerService(_capture, _templateMatcher);
            _scanner.KeywordsDetected += OnKeywordsDetected;
            if (Config.ShowDebugText)  _scanner.DebugKeywords += OnDebugKeywords;
            if (Config.ShowDebugImage) _scanner.DebugCaptureImage += OnDebugCaptureImage;
            _scanner.Start();
            SetStatus("idle", "스캔중");
        }
        catch (Exception ex)
        {
            SetStatus("error", ex.Message);
        }
        await Task.CompletedTask;
    }

    // ── 스캔 결과 수신 ────────────────────────────────────────────

    private void OnKeywordsDetected(List<string> keywords)
    {
        _lastKeywords = keywords;
        _lastResult   = _matcher.Match(keywords);
        ApplyMatchResult(_lastResult, keywords);
    }

    private static readonly Color[] EssenceColors =
    [
        Color.FromRgb(0xF8, 0xF5, 0x46), // 기질1 — 액센트
        Color.FromRgb(0x65, 0x71, 0x36), // 기질2 — 올리브
        Color.FromRgb(0x7E, 0x80, 0x7C), // 기질3 — 중립
    ];

    private void SetEssenceChips(IReadOnlyList<string> traits)
    {
        EssencesPanel.Children.Clear();
        for (int i = 0; i < traits.Count; i++)
        {
            var color = EssenceColors[Math.Min(i, EssenceColors.Length - 1)];
            var border = new System.Windows.Controls.Border
            {
                CornerRadius    = new CornerRadius(10),
                Padding         = new Thickness(10, 3, 10, 3),
                Margin          = new Thickness(0, 0, 6, 2),
                Background      = new SolidColorBrush(Color.FromArgb(0x33, color.R, color.G, color.B)),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(0x88, color.R, color.G, color.B)),
                BorderThickness = new Thickness(1),
                Child           = new System.Windows.Controls.TextBlock
                {
                    Text       = traits[i],
                    FontFamily = new System.Windows.Media.FontFamily("Malgun Gothic"),
                    FontSize   = 23,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                }
            };
            EssencesPanel.Children.Add(border);
        }
        EssencesPanel.Visibility = traits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ApplyMatchResult(MatchResult result, List<string> keywords)
    {
        switch (result.Status)
        {
            case MatchStatus.Invalid:
                if (result.SnappedEssences.Count == 0)
                {
                    SetStatus("idle", "스캔중");
                    SetEssenceChips([]);
                }
                else
                {
                    SetStatus("invalid", "비유효 기질");
                    SetEssenceChips(result.SnappedEssences);
                }
                break;
            case MatchStatus.ValidUnowned:
                SetStatus("valid_unowned", "유효 기질");
                SetEssenceChips(result.MatchedEssences);
                SetDetailWithOwnership(result);
                RebuildOwnedButtons(result.UnownedNames, keywords);
                break;
            case MatchStatus.ValidOwned:
                SetStatus("valid_owned", "보유 기질");
                SetEssenceChips(result.MatchedEssences);
                SetDetailWithOwnership(result);
                break;
        }
    }

    private void SetDetailWithOwnership(MatchResult result)
    {
        Dispatcher.Invoke(() =>
        {
            DetailText.Inlines.Clear();
            var ownedBrush   = new SolidColorBrush(Color.FromRgb(0x00, 0xE6, 0x76));
            var unownedBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52));
            var sepBrush     = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
            var unownedSet   = result.UnownedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < result.MatchedNames.Count; i++)
            {
                if (i > 0)
                    DetailText.Inlines.Add(new Run(" / ") { Foreground = sepBrush });

                var name    = result.MatchedNames[i];
                bool owned  = !unownedSet.Contains(name);
                var brush   = owned ? ownedBrush : unownedBrush;
                var label   = owned ? "(보유 중)" : "(미보유 중)";

                DetailText.Inlines.Add(new Run(name) { Foreground = brush });
                DetailText.Inlines.Add(new Run(label) { Foreground = brush, FontSize = 16 });
            }
            DetailText.Visibility = result.MatchedNames.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    // ── 무기별 보유 등록 버튼 동적 생성 ──────────────────────────

    private void RebuildOwnedButtons(IReadOnlyList<string> weaponNames, List<string> keywords)
    {
        OwnedButtonsPanel.Children.Clear();
        foreach (var name in weaponNames)
        {
            var weaponName = name;
            var accent = Color.FromRgb(0xF8, 0xF5, 0x46);
            var border = new System.Windows.Controls.Border
            {
                CornerRadius    = new CornerRadius(12),
                Background      = new SolidColorBrush(Color.FromArgb(0x22, accent.R, accent.G, accent.B)),
                BorderBrush     = new SolidColorBrush(Color.FromArgb(0x66, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                Margin          = new Thickness(0, 0, 6, 0),
                Cursor          = System.Windows.Input.Cursors.Hand,
                Child           = new System.Windows.Controls.TextBlock
                {
                    Text       = $"+ 보유: {weaponName}",
                    FontFamily = new System.Windows.Media.FontFamily("Malgun Gothic"),
                    FontSize   = 20,
                    Foreground = new SolidColorBrush(accent),
                    Padding    = new Thickness(10, 4, 10, 4),
                }
            };
            border.MouseLeftButtonDown += (_, _) => RegisterOwned(weaponName, keywords);
            OwnedButtonsPanel.Children.Add(border);
        }
    }

    private void RegisterOwned(string weaponName, List<string> keywords)
    {
        _matcher.MarkOwned([weaponName], keywords);
        _lastResult = _matcher.Match(_lastKeywords);
        ApplyMatchResult(_lastResult, _lastKeywords);
    }

    // ── UI 상태 갱신 ──────────────────────────────────────────────

    private static readonly Dictionary<string, (string Icon, Color Color)> _styles = new()
    {
        ["idle"]          = ("🔄", Color.FromRgb(0x7E, 0x80, 0x7C)),
        ["invalid"]       = ("✅", Color.FromRgb(0x00, 0xE6, 0x76)),
        ["valid_unowned"] = ("⚠️", Color.FromRgb(0xFF, 0x52, 0x52)),
        ["valid_owned"]   = ("✅", Color.FromRgb(0x00, 0xE6, 0x76)),
        ["error"]         = ("⚠️", Color.FromRgb(0xFF, 0x52, 0x52)),
    };

    private void SetStatus(string status, string message, string? detail = null)
    {
        Dispatcher.Invoke(() =>
        {
            var (icon, color) = _styles.GetValueOrDefault(status, ("❓", Colors.White));
            var brush = new SolidColorBrush(color);
            IconText.Text         = icon;
            IconText.Foreground   = brush;
            StatusText.Text       = message;
            StatusText.Foreground = brush;
            AccentBar.Background  = brush;
            if (detail != null)
            {
                DetailText.Text       = detail;
                DetailText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                DetailText.Visibility = Visibility.Visible;
            }
            else
            {
                DetailText.Visibility = Visibility.Collapsed;
            }
            OwnedButtonsPanel.Visibility =
                status == "valid_unowned" ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    // ── 디버그 제어 ──────────────────────────────────────────────

    internal void ApplyDebugText(bool enable)
    {
        Config.ShowDebugText = enable;
        Dispatcher.Invoke(() =>
            DebugPanel.Visibility = enable ? Visibility.Visible : Visibility.Collapsed);
        if (_scanner != null)
        {
            _scanner.DebugKeywords -= OnDebugKeywords;
            if (enable) _scanner.DebugKeywords += OnDebugKeywords;
        }
        UpdateDebugCapture();
    }

    internal void ApplyDebugImage(bool enable)
    {
        Config.ShowDebugImage = enable;
        if (enable)
        {
            if (_debugImage == null) { _debugImage = new DebugImageWindow(); _debugImage.Show(); }
            if (_scanner != null) _scanner.DebugCaptureImage += OnDebugCaptureImage;
        }
        else
        {
            if (_scanner != null) _scanner.DebugCaptureImage -= OnDebugCaptureImage;
            _debugImage?.Close(); _debugImage = null;
        }
        UpdateDebugCapture();
    }

    private void UpdateDebugCapture()
    {
        if (Config.ShowDebugText || Config.ShowDebugImage)
        {
            if (_debugCapture == null)
            {
                _debugCapture = new DebugCaptureWindow();
                _debugCapture.Show();
            }
        }
        else
        {
            _debugCapture?.Close();
            _debugCapture = null;
        }
    }

    internal void ApplyOpacity(double opacity)
    {
        Config.OverlayOpacity = opacity;
        var alpha = (byte)(opacity * 255);
        Dispatcher.Invoke(() =>
        {
            if (Content is System.Windows.Controls.Border border)
                border.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x31, 0x37, 0x39));
        });
    }

    // ── 설정창 ────────────────────────────────────────────────────

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settingsWindow != null && _settingsWindow.IsVisible)
        {
            _settingsWindow.Focus();
            return;
        }
        _settingsWindow = new SettingsWindow(
            onDebugText:  ApplyDebugText,
            onDebugImage: ApplyDebugImage,
            onOpacity:    ApplyOpacity,
            onCalibrate:  RunCalibration,
            matcher:      _matcher);
        _settingsWindow.Owner = this;
        _settingsWindow.Show();
    }

    // ── 캘리브레이션 ──────────────────────────────────────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _hotkey.Initialize(this);
        _hotkey.Register(0, 0x77, RunCalibration); // F8

        // 게임 위에 항상 표시되도록 주기적으로 TOPMOST 재적용
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _topmostTimer.Tick += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        };
        _topmostTimer.Start();

        var region = CalibrationService.Load();
        if (region != null)
            CalibrationService.Apply(region);
        else
            RunCalibration();
    }

    private void RunCalibration()
    {
        _scanner?.Stop();
        var win = new CalibrationWindow();
        if (win.ShowDialog() == true && win.Result is { } r)
        {
            CalibrationService.Save(r);
            CalibrationService.Apply(r);
            _debugCapture?.UpdateBounds();
        }
        _scanner?.Start();
    }

    // ── 기타 ─────────────────────────────────────────────────────

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    private void OnDebugCaptureImage(byte[] pngBytes)
        => _debugImage?.UpdateImage(pngBytes);

    private void OnDebugKeywords(List<string> lines, List<Services.MatchCandidate> top3)
    {
        Dispatcher.Invoke(() =>
        {
            var parts = new List<string>();
            if (lines.Count > 0)
                parts.Add("감지: " + string.Join(", ", lines));
            else
                parts.Add("(감지된 키워드 없음)");
            if (top3.Count > 0)
                parts.Add("Top3: " + string.Join(" | ",
                    top3.Select(c => $"{c.Keyword} {c.Score:F3}")));
            DebugText.Text = string.Join("\n", parts);
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _topmostTimer?.Stop();
        _hotkey.Dispose();
        _scanner?.Dispose();
        _templateMatcher.Dispose();
        _debugCapture?.Close();
        _debugImage?.Close();
        _settingsWindow?.Close();
        base.OnClosed(e);
    }

    private static void EnsureDataFiles()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Config.WeaponsJsonPath)!);
        if (!File.Exists(Config.OwnedJsonPath))
            File.WriteAllText(Config.OwnedJsonPath, "[]");
    }
}
