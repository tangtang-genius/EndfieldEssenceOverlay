// src/EndfieldEssenceOverlay/SettingsWindow.xaml.cs
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EndfieldEssenceOverlay.Services;

namespace EndfieldEssenceOverlay;

public partial class SettingsWindow : MahApps.Metro.Controls.MetroWindow
{
    // ── Win32 P/Invoke ──
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_APPWINDOW  = 0x00040000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const uint GW_OWNER = 4;

    private readonly Action<bool>        _onDebugText;
    private readonly Action<bool>        _onDebugImage;
    private readonly Action<double>      _onOpacity;
    private readonly Action              _onCalibrate;
    private readonly EssenceMatcherService _matcher;

    private IReadOnlySet<string> _initialOwned = new HashSet<string>();

    public SettingsWindow(
        Action<bool>        onDebugText,
        Action<bool>        onDebugImage,
        Action<double>      onOpacity,
        Action              onCalibrate,
        EssenceMatcherService matcher)
    {
        InitializeComponent();
        _onDebugText  = onDebugText;
        _onDebugImage = onDebugImage;
        _onOpacity    = onOpacity;
        _onCalibrate  = onCalibrate;
        _matcher      = matcher;

        DebugTextCheck.IsChecked  = Config.ShowDebugText;
        DebugImageCheck.IsChecked = Config.ShowDebugImage;
        PollIntervalBox.Text      = Config.PollIntervalMs.ToString();
        OpacitySlider.Value       = Math.Round(Config.OverlayOpacity * 100);
        OpacityLabel.Text         = $"{(int)OpacitySlider.Value}%";

        HelpText.Text =
            "📌 기본 사용법\n" +
            "앱을 실행하면 게임 화면을 자동으로 스캔합니다.\n" +
            "기질 조합이 감지되면 유효 / 보유 / 비유효 여부를 상단 오버레이로 표시합니다.\n\n" +
            "📌 캡처 범위 설정\n" +
            "설정 → [📐 캡처 범위 재설정 (F8)] 을 클릭하거나 F8 키를 누르세요.\n" +
            "게임 화면에서 기질 3개가 표시되는 패널 영역을 드래그로 선택하세요.\n" +
            "처음 실행 시 자동으로 이 창이 열립니다.\n\n" +
            "📌 보유 기질 관리\n" +
            "설정 → 보유 목록 탭에서 보유 중인 무기에 체크하고 [저장]을 누르세요.\n" +
            "또는 오버레이에서 유효 기질 감지 시 [보유] 버튼으로 바로 등록할 수 있습니다.\n\n" +
            $"앱 v{Config.AppVersion} | 엔드필드 v{Config.GameVersion} 대응";

        BuildWeaponList();
        LoadWindowList();
    }

    // ── 설정 탭 ──────────────────────────────────────────────────

    private void DebugTextCheck_Changed(object s, RoutedEventArgs e)
        => _onDebugText(DebugTextCheck.IsChecked == true);

    private void DebugImageCheck_Changed(object s, RoutedEventArgs e)
        => _onDebugImage(DebugImageCheck.IsChecked == true);

    private void PollIntervalBox_LostFocus(object s, RoutedEventArgs e)
    {
        if (int.TryParse(PollIntervalBox.Text, out int val) && val >= 100 && val <= 5000)
            Config.PollIntervalMs = val;
        else
            PollIntervalBox.Text = Config.PollIntervalMs.ToString();
    }

    private void OpacitySlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (OpacityLabel == null) return;
        int pct = (int)OpacitySlider.Value;
        OpacityLabel.Text = $"{pct}%";
        _onOpacity(pct / 100.0);
    }

    private void CalibrateButton_Click(object s, RoutedEventArgs e)
        => _onCalibrate();

    // ── 캡처 대상 ──────────────────────────────────────────────────

    private void LoadWindowList()
    {
        var titles = new List<string>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            int len = GetWindowTextLength(hWnd);
            if (len <= 0) return true;

            // Alt+Tab 기준 필터: WS_EX_APPWINDOW → 표시, WS_EX_TOOLWINDOW → 제외,
            // owner 없는 top-level → 표시
            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
            bool isAppWindow  = (exStyle & WS_EX_APPWINDOW) != 0;
            bool isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
            bool hasOwner     = GetWindow(hWnd, GW_OWNER) != IntPtr.Zero;

            if (isToolWindow && !isAppWindow) return true;
            if (hasOwner && !isAppWindow) return true;

            var sb = new StringBuilder(len + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            titles.Add(sb.ToString());
            return true;
        }, IntPtr.Zero);

        titles.Sort(StringComparer.CurrentCultureIgnoreCase);

        WindowCombo.SelectionChanged -= WindowCombo_SelectionChanged;
        WindowCombo.ItemsSource = titles;
        WindowCombo.Text = Config.GameWindowTitle;

        int idx = titles.IndexOf(Config.GameWindowTitle);
        if (idx >= 0) WindowCombo.SelectedIndex = idx;

        WindowCombo.SelectionChanged += WindowCombo_SelectionChanged;
    }

    private void RefreshWindowsBtn_Click(object s, RoutedEventArgs e)
        => LoadWindowList();

    private void WindowCombo_SelectionChanged(object s, SelectionChangedEventArgs e)
    {
        if (WindowCombo.SelectedItem is string title && !string.IsNullOrWhiteSpace(title))
            ApplyGameWindowTitle(title);
    }

    private void WindowCombo_LostFocus(object s, RoutedEventArgs e)
    {
        var text = WindowCombo.Text;
        if (!string.IsNullOrWhiteSpace(text))
            ApplyGameWindowTitle(text);
    }

    private static void ApplyGameWindowTitle(string title)
    {
        Config.GameWindowTitle = title;
        var region = new CaptureRegion(
            Config.CaptureLeft, Config.CaptureTop,
            Config.CaptureWidth, Config.CaptureHeight);
        CalibrationService.Save(region);
    }

    // ── 보유 목록 탭 ──────────────────────────────────────────────

    private void BuildWeaponList()
    {
        _initialOwned = _matcher.OwnedWeaponNames;
        WeaponListPanel.Children.Clear();

        var groups = _matcher.AllWeapons
            .GroupBy(w => w.Essences.Count > 0 ? w.Essences[0] : "기타");

        foreach (var group in groups)
        {
            WeaponListPanel.Children.Add(new TextBlock
            {
                Text       = group.Key,
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize   = 18,
                Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0xF5, 0x46)),
                Margin     = new Thickness(0, 10, 0, 4),
                FontWeight = FontWeights.Bold,
            });

            foreach (var weapon in group)
            {
                var cb = new CheckBox
                {
                    Tag       = weapon.Name,
                    IsChecked = _initialOwned.Contains(weapon.Name),
                    Margin    = new Thickness(4, 2, 0, 2),
                };

                var starLabel = weapon.Star >= 6 ? "★6" : "★5";
                var panel = new StackPanel { Orientation = Orientation.Vertical };
                panel.Children.Add(new TextBlock
                {
                    Text       = $"{starLabel} {weapon.Name}",
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize   = 18,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)),
                });
                panel.Children.Add(new TextBlock
                {
                    Text       = string.Join(" · ", weapon.Essences),
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize   = 16,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0)),
                });

                cb.Content = panel;
                WeaponListPanel.Children.Add(cb);
            }
        }
    }

    private void SaveOwnedButton_Click(object s, RoutedEventArgs e)
    {
        var selected = WeaponListPanel.Children
            .OfType<CheckBox>()
            .Where(cb => cb.IsChecked == true)
            .Select(cb => (string)cb.Tag)
            .ToList();

        _matcher.RebuildOwned(selected);
        _initialOwned = _matcher.OwnedWeaponNames;
    }

    private void CancelOwnedButton_Click(object s, RoutedEventArgs e)
    {
        foreach (var cb in WeaponListPanel.Children.OfType<CheckBox>())
            cb.IsChecked = _initialOwned.Contains((string)cb.Tag);
    }

    private void ResetOwnedButton_Click(object s, RoutedEventArgs e)
    {
        if (MessageBox.Show("보유 목록을 전부 초기화할까요?", "초기화 확인",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        foreach (var cb in WeaponListPanel.Children.OfType<CheckBox>())
            cb.IsChecked = false;
    }
}
