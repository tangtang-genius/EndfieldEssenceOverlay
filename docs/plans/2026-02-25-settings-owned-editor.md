# Settings Window + Owned Editor + UI 개선 구현 계획


**Goal:** 탭 구조 설정창(디버그/스캔/불투명도/캡처), 소유 기질 체크박스 편집기, 기질 칩 UI, idle 로직 개선을 구현한다.

**Architecture:** Config에 새 필드 추가 → TraitMatcherService에 List 순서 보존 + RebuildOwned 추가 → MainWindow 헤더 단순화 + 칩 UI → SettingsWindow 신규 생성. SettingsWindow는 콜백과 TraitMatcherService 참조로 MainWindow와 통신한다.

**Tech Stack:** C# 12 / .NET 8 / WPF, xUnit, FuzzySharp

> **빌드 환경 주의:** `dotnet build` / `dotnet test`는 Windows에서만 실행 가능. WSL(Linux)에서는 실행 불가.

---

## Task 1: Config.cs — 필드 정리 및 추가

**Files:**
- Modify: `src/EndfieldEssenceOverlay/Config.cs`

**Step 1: 다음 항목을 제거한다**

- `public const uint VK_TOGGLE = 0x79;`
- `public const uint MOD_NONE  = 0x0000;`
- `public static bool DebugMode { get; set; } = false;`

**Step 2: 다음 항목을 추가/변경한다**

`const int PollIntervalMs = 500` → `static int` property로 변경:
```csharp
public static int PollIntervalMs { get; set; } = 500;
```

새 필드 추가 (DebugMode 제거 자리에):
```csharp
// 디버그: 각 기능 독립 토글
public static bool ShowDebugText  { get; set; } = false;
public static bool ShowDebugImage { get; set; } = false;

// 오버레이 불투명도 (0.3 ~ 1.0)
public static double OverlayOpacity { get; set; } = 0.8;
```

**Step 3: Windows에서 빌드 확인**
```
dotnet build src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
```
Expected: Build succeeded (에러 있으면 DebugMode/VK_TOGGLE 참조가 다른 파일에 남아있는 것 — Task 4에서 제거)

---

## Task 2: TraitMatcherService — HashSet→List (순서 보존) + 공개 API 추가

**Files:**
- Modify: `src/EndfieldEssenceOverlay/Services/TraitMatcherService.cs`

칩 UI에서 기질1/2/3 색상을 파일 순서 기반으로 구분하려면 내부 저장이 `HashSet` → `List` 로 바뀌어야 한다.

**Step 1: 내부 타입 변경**

```csharp
// 변경 전
private List<(string Name, HashSet<string> Traits)> _valid;
private List<(string Name, HashSet<string> Traits)> _owned;

// 변경 후
private List<(string Name, List<string> Traits)> _valid;
private List<(string Name, List<string> Traits)> _owned;
```

**Step 2: LoadTraitFile 반환 타입 변경**

```csharp
private static List<(string Name, List<string> Traits)> LoadTraitFile(string path)
{
    var result = new List<(string, List<string>)>();
    if (!File.Exists(path)) return result;

    foreach (var raw in File.ReadLines(path))
    {
        var line = raw.Trim();
        if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

        var parts = line.Split(',')
                        .Select(k => k.Trim())
                        .Where(k => !string.IsNullOrEmpty(k))
                        .ToArray();
        if (parts.Length < 2) continue;

        result.Add((parts[0], parts[1..].ToList()));
    }
    return result;
}
```

**Step 3: SetMatch 시그니처 변경**

```csharp
private static bool SetMatch(IList<string> snapped, List<string> target)
    => target.All(t => snapped.Contains(t, StringComparer.OrdinalIgnoreCase));
```

**Step 4: MarkOwned 내부 타입 수정**

```csharp
public void MarkOwned(IList<string> weaponNames, IList<string> keywords)
{
    foreach (var name in weaponNames)
    {
        File.AppendAllText(_ownedPath,
            name + "," + string.Join(",", keywords) + Environment.NewLine);
        _owned.Add((name, keywords.ToList()));   // HashSet → List
    }
    RebuildVocabulary();
}
```

**Step 5: 공개 API 추가** (설정창의 소유 목록 탭에 필요)

```csharp
/// <summary>valid_traits.txt 전체 무기 목록 (파일 순서 보존)</summary>
public IReadOnlyList<(string Name, IReadOnlyList<string> Traits)> AllWeapons =>
    _valid.Select(e => ((string)e.Name, (IReadOnlyList<string>)e.Traits)).ToList();

/// <summary>현재 소유 중인 무기 이름 집합</summary>
public IReadOnlySet<string> OwnedWeaponNames =>
    _owned.Select(e => e.Name)
          .ToHashSet(StringComparer.OrdinalIgnoreCase);

/// <summary>소유 목록을 weaponNames 기준으로 재구성하고 파일에 저장</summary>
public void RebuildOwned(IList<string> weaponNames)
{
    _owned = _valid
        .Where(e => weaponNames.Contains(e.Name, StringComparer.OrdinalIgnoreCase))
        .ToList();

    var lines = _owned.Select(e => e.Name + "," + string.Join(",", e.Traits));
    File.WriteAllLines(_ownedPath,
        new[] { "# 소유 중인 기질 목록 (자동 관리)" }.Concat(lines));

    RebuildVocabulary();
}
```

---

## Task 3: TraitMatcherService 테스트 — RebuildOwned 검증

**Files:**
- Modify: `tests/EndfieldEssenceOverlay.Tests/TraitMatcherServiceTests.cs`

**Step 1: 기존 테스트가 통과하는지 확인**
```
dotnet test tests/EndfieldEssenceOverlay.Tests/
```
Expected: 7 tests PASS (HashSet→List 변경 후 기존 동작 유지 확인)

**Step 2: RebuildOwned 테스트 2개 추가**

```csharp
[Fact]
public void RebuildOwned_MarksSelectedWeaponAsOwned()
{
    var svc = Make(["무기A,민첩,공격력 증가,고통", "무기B,지능,아츠 피해 증가,어둠"], []);

    svc.RebuildOwned(["무기A"]);

    Assert.Equal(MatchStatus.ValidOwned,
        svc.Match(["민첩", "공격력 증가", "고통"]).Status);
    Assert.Equal(MatchStatus.ValidUnowned,
        svc.Match(["지능", "아츠 피해 증가", "어둠"]).Status);
}

[Fact]
public void RebuildOwned_EmptyList_ClearsAllOwned()
{
    var svc = Make(["무기A,민첩,공격력 증가,고통"],
                   ["무기A,민첩,공격력 증가,고통"]);

    svc.RebuildOwned([]);

    Assert.Equal(MatchStatus.ValidUnowned,
        svc.Match(["민첩", "공격력 증가", "고통"]).Status);
}
```

**Step 3: 테스트 실행**
```
dotnet test tests/EndfieldEssenceOverlay.Tests/
```
Expected: 9 tests PASS

**Step 4: Commit**
```
git add src/EndfieldEssenceOverlay/Services/TraitMatcherService.cs
git add tests/EndfieldEssenceOverlay.Tests/TraitMatcherServiceTests.cs
git commit -m "refactor: TraitMatcherService HashSet→List, add RebuildOwned + AllWeapons"
```

---

## Task 4: ScannerService — DebugMode 참조 교체

**Files:**
- Modify: `src/EndfieldEssenceOverlay/Services/ScannerService.cs`

**Step 1: ScanOnce() 내 두 곳 교체**

```csharp
// 변경 전
if (Config.DebugMode)
{
    var pngBytes = BitmapToPng(bitmap);
    Application.Current.Dispatcher.Invoke(
        () => DebugOcrImage?.Invoke(pngBytes));
}
...
if (Config.DebugMode)
    Application.Current.Dispatcher.Invoke(
        () => DebugOcrLines?.Invoke(lines));

// 변경 후
if (Config.ShowDebugImage)
{
    var pngBytes = BitmapToPng(bitmap);
    Application.Current.Dispatcher.Invoke(
        () => DebugOcrImage?.Invoke(pngBytes));
}
...
if (Config.ShowDebugText)
    Application.Current.Dispatcher.Invoke(
        () => DebugOcrLines?.Invoke(lines));
```

**Step 2: 빌드 확인**
```
dotnet build src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
```

---

## Task 5: MainWindow.xaml — 헤더 정리 + TraitsPanel 교체 + 폰트 크기

**Files:**
- Modify: `src/EndfieldEssenceOverlay/MainWindow.xaml`

**Step 1: 헤더 버튼 교체**

```xml
<!-- 변경 전 -->
<Button x:Name="DebugButton" Content="👁" ... Click="DebugButton_Click"/>
<Button Content="📐" ... Click="CalibrationButton_Click"/>
<Button Content="✕" ... Click="CloseButton_Click"/>

<!-- 변경 후 -->
<Button Content="⚙"
        ToolTip="설정"
        Width="24" Height="24"
        Background="Transparent" Foreground="#888888"
        BorderThickness="0" FontSize="12" Cursor="Hand"
        Click="SettingsButton_Click"/>
<Button Content="✕"
        Width="24" Height="24"
        Background="Transparent" Foreground="#666666"
        BorderThickness="0" FontSize="12" Cursor="Hand"
        Click="CloseButton_Click"/>
```

**Step 2: StatusText 폰트 크기 18→20**

```xml
<TextBlock x:Name="StatusText"
           FontFamily="Malgun Gothic"
           FontSize="20" FontWeight="Bold"
           Foreground="#AAAAAA"/>
```

**Step 3: TraitsText TextBlock → TraitsPanel WrapPanel으로 교체**

```xml
<!-- 변경 전 -->
<TextBlock x:Name="TraitsText"
           Text=""
           FontFamily="Malgun Gothic" FontSize="13"
           Foreground="#AAAAAA" TextWrapping="Wrap"
           Visibility="Collapsed"/>

<!-- 변경 후 -->
<WrapPanel x:Name="TraitsPanel"
           Margin="0,3,0,0"
           Visibility="Collapsed"/>
```

**Step 4: DebugPanel TextBlock FontSize 14→15**

```xml
<TextBlock x:Name="DebugText"
           FontFamily="Consolas" FontSize="15"
           .../>
```

---

## Task 6: MainWindow.xaml.cs — 대규모 리팩터링

**Files:**
- Modify: `src/EndfieldEssenceOverlay/MainWindow.xaml.cs`

### Step 1: 제거할 항목

다음을 모두 삭제:
- `private bool _clickThrough = false;` 필드
- `OnSourceInitialized()` 메서드 전체 (or 내부 hotkey 등록만 제거하고 빈 override 유지)
- `OnToggleClickThrough()` 메서드
- `SetClickThrough()` 메서드
- 4개의 `[DllImport("user32.dll")]` 선언 (GetWindowLong32, GetWindowLongPtr, SetWindowLong32, SetWindowLongPtr)
- `DebugButton_Click()` 메서드
- `CalibrationButton_Click()` 메서드
- `_hotkey` 필드 및 `OnClosed`의 `_hotkey.Dispose()` 호출

### Step 2: 설정창 필드 추가

```csharp
private SettingsWindow? _settingsWindow;
```

### Step 3: `_lastKeywords` 필드 타입 변경 (이미 있을 수 있음)

필드 선언부 확인 후 그대로 유지.

### Step 4: InitializeAsync — 디버그 이벤트 구독 추가

```csharp
private async Task InitializeAsync()
{
    try
    {
        _ocr.Initialize();
        _scanner = new ScannerService(_capture, _ocr);
        _scanner.KeywordsDetected += OnKeywordsDetected;
        // 앱 시작 시 현재 Config 값 기준으로 구독
        if (Config.ShowDebugText)  _scanner.DebugOcrLines += OnDebugOcrLines;
        if (Config.ShowDebugImage) _scanner.DebugOcrImage += OnDebugOcrImage;
        _scanner.Start();
        SetStatus("idle", "실시간 스캔 중");
    }
    catch (Exception ex)
    {
        SetStatus("error", ex.Message);
    }
    await Task.CompletedTask;
}
```

### Step 5: 디버그 제어 메서드 추가

```csharp
internal void ApplyDebugText(bool enable)
{
    Config.ShowDebugText = enable;
    Dispatcher.Invoke(() =>
        DebugPanel.Visibility = enable ? Visibility.Visible : Visibility.Collapsed);
    if (_scanner != null)
    {
        _scanner.DebugOcrLines -= OnDebugOcrLines;
        if (enable) _scanner.DebugOcrLines += OnDebugOcrLines;
    }
    UpdateDebugCapture();
}

internal void ApplyDebugImage(bool enable)
{
    Config.ShowDebugImage = enable;
    if (enable)
    {
        if (_debugImage == null) { _debugImage = new DebugImageWindow(); _debugImage.Show(); }
        if (_scanner != null) _scanner.DebugOcrImage += OnDebugOcrImage;
    }
    else
    {
        if (_scanner != null) _scanner.DebugOcrImage -= OnDebugOcrImage;
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
        Background = new SolidColorBrush(Color.FromArgb(alpha, 0x1A, 0x1A, 0x1A)));
}
```

### Step 6: SettingsButton_Click 추가

```csharp
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
```

### Step 7: ApplyMatchResult — 칩 UI + idle 로직 변경

칩 색상 배열 (static):
```csharp
private static readonly Color[] TraitColors =
[
    Color.FromRgb(0x55, 0x99, 0xCC), // 기질1 — 파랑
    Color.FromRgb(0x77, 0xBB, 0x55), // 기질2 — 초록
    Color.FromRgb(0xFF, 0x99, 0x44), // 기질3 — 주황
];
```

칩 생성 헬퍼:
```csharp
private void SetTraitChips(IReadOnlyList<string> traits)
{
    TraitsPanel.Children.Clear();
    for (int i = 0; i < traits.Count; i++)
    {
        var color = TraitColors[Math.Min(i, TraitColors.Length - 1)];
        var border = new System.Windows.Controls.Border
        {
            CornerRadius    = new CornerRadius(3),
            Padding         = new Thickness(6, 2, 6, 2),
            Margin          = new Thickness(0, 0, 4, 2),
            Background      = new SolidColorBrush(Color.FromArgb(0x44, color.R, color.G, color.B)),
            BorderBrush     = new SolidColorBrush(color),
            BorderThickness = new Thickness(1),
            Child = new System.Windows.Controls.TextBlock
            {
                Text       = traits[i],
                FontFamily = new System.Windows.Media.FontFamily("Malgun Gothic"),
                FontSize   = 15,
                Foreground = new SolidColorBrush(Colors.White),
            }
        };
        TraitsPanel.Children.Add(border);
    }
    TraitsPanel.Visibility = traits.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
}
```

ApplyMatchResult 변경:
```csharp
private void ApplyMatchResult(MatchResult result, List<string> keywords)
{
    switch (result.Status)
    {
        case MatchStatus.Invalid:
            // SnappedTraits 없으면 → idle (엉뚱한 단어만 보임)
            if (result.SnappedTraits.Count == 0)
            {
                SetStatus("idle", "실시간 스캔 중");
                SetTraitChips([]);
            }
            else
            {
                SetStatus("invalid", "비유효 기질");
                SetTraitChips(result.SnappedTraits);
            }
            break;
        case MatchStatus.ValidUnowned:
            SetStatus("valid_unowned", string.Join(" / ", result.MatchedNames));
            SetTraitChips(result.MatchedTraits);
            RebuildOwnedButtons(result.MatchedNames, keywords);
            break;
        case MatchStatus.ValidOwned:
            SetStatus("valid_owned",
                $"이미 소유 중: {string.Join(", ", result.MatchedNames)}");
            SetTraitChips(result.MatchedTraits);
            break;
    }
}
```

SetStatus에서 TraitsText 관련 코드 제거 (TraitsPanel로 대체됨):
```csharp
private void SetStatus(string status, string message)
{
    Dispatcher.Invoke(() =>
    {
        var (icon, color) = _styles.GetValueOrDefault(status, ("❓", Colors.White));
        var brush = new SolidColorBrush(color);
        IconText.Text         = icon;
        IconText.Foreground   = brush;
        StatusText.Text       = message;
        StatusText.Foreground = brush;
        DetailText.Visibility        = Visibility.Collapsed;
        OwnedButtonsPanel.Visibility =
            status == "valid_unowned" ? Visibility.Visible : Visibility.Collapsed;
    });
}
```

### Step 8: OnClosed 정리

```csharp
protected override void OnClosed(EventArgs e)
{
    _scanner?.Dispose();
    // _hotkey.Dispose(); ← 제거
    _debugCapture?.Close();
    _debugImage?.Close();
    _settingsWindow?.Close();
    base.OnClosed(e);
}
```

**Step 9: 빌드 확인**
```
dotnet build src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
```

**Step 10: Commit**
```
git add src/EndfieldEssenceOverlay/
git commit -m "refactor: remove F10, split debug flags, add chip UI + idle logic"
```

---

## Task 7: SettingsWindow.xaml — 신규 생성

**Files:**
- Create: `src/EndfieldEssenceOverlay/SettingsWindow.xaml`

```xml
<!-- src/EndfieldEssenceOverlay/SettingsWindow.xaml -->
<Window x:Class="EndfieldEssenceOverlay.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="설정"
        Width="320" SizeToContent="Height"
        WindowStyle="ToolWindow"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        Topmost="True"
        WindowStartupLocation="CenterOwner"
        Background="#FF1E1E1E">
    <Window.Resources>
        <Style TargetType="TextBlock">
            <Setter Property="Foreground" Value="#CCCCCC"/>
            <Setter Property="FontFamily" Value="Malgun Gothic"/>
        </Style>
        <Style TargetType="CheckBox">
            <Setter Property="Foreground" Value="#CCCCCC"/>
            <Setter Property="FontFamily" Value="Malgun Gothic"/>
            <Setter Property="FontSize"   Value="13"/>
            <Setter Property="Margin"     Value="0,4,0,0"/>
        </Style>
        <Style TargetType="Button">
            <Setter Property="FontFamily"       Value="Malgun Gothic"/>
            <Setter Property="FontSize"         Value="13"/>
            <Setter Property="Background"       Value="#FF333333"/>
            <Setter Property="Foreground"       Value="#CCCCCC"/>
            <Setter Property="BorderBrush"      Value="#FF555555"/>
            <Setter Property="BorderThickness"  Value="1"/>
            <Setter Property="Padding"          Value="12,4"/>
            <Setter Property="Cursor"           Value="Hand"/>
        </Style>
    </Window.Resources>

    <TabControl Background="#FF1E1E1E" BorderThickness="0" Margin="0">
        <TabControl.Resources>
            <Style TargetType="TabItem">
                <Setter Property="Foreground" Value="#AAAAAA"/>
                <Setter Property="Background" Value="#FF2A2A2A"/>
                <Setter Property="FontFamily" Value="Malgun Gothic"/>
                <Setter Property="FontSize"   Value="13"/>
                <Setter Property="Padding"    Value="12,6"/>
            </Style>
        </TabControl.Resources>

        <!-- ── 설정 탭 ── -->
        <TabItem Header="설정">
            <StackPanel Margin="14,10,14,14">

                <!-- 디버그 -->
                <TextBlock Text="디버그" FontSize="11" Foreground="#666666" Margin="0,0,0,4"/>
                <CheckBox x:Name="DebugTextCheck"
                          Content="OCR 텍스트 패널 표시"
                          Checked="DebugTextCheck_Changed"
                          Unchecked="DebugTextCheck_Changed"/>
                <CheckBox x:Name="DebugImageCheck"
                          Content="OCR 입력 이미지 표시"
                          Margin="0,4,0,0"
                          Checked="DebugImageCheck_Changed"
                          Unchecked="DebugImageCheck_Changed"/>

                <Separator Margin="0,12,0,8" Background="#FF444444"/>

                <!-- 스캔 -->
                <TextBlock Text="스캔" FontSize="11" Foreground="#666666" Margin="0,0,0,8"/>
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <TextBlock Text="캡처 주기" VerticalAlignment="Center" Margin="0,0,8,0"/>
                    <TextBox x:Name="PollIntervalBox"
                             Width="60" Height="24"
                             Background="#FF2A2A2A" Foreground="#CCCCCC"
                             BorderBrush="#FF555555" CaretBrush="White"
                             VerticalContentAlignment="Center"
                             Padding="4,0"
                             LostFocus="PollIntervalBox_LostFocus"/>
                    <TextBlock Text=" ms" VerticalAlignment="Center"/>
                </StackPanel>

                <Separator Margin="0,12,0,8" Background="#FF444444"/>

                <!-- 화면 -->
                <TextBlock Text="화면" FontSize="11" Foreground="#666666" Margin="0,0,0,8"/>
                <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                    <TextBlock Text="오버레이 불투명도" VerticalAlignment="Center" Margin="0,0,8,0"/>
                    <Slider x:Name="OpacitySlider"
                            Width="100" Minimum="30" Maximum="100" Value="80"
                            VerticalAlignment="Center"
                            ValueChanged="OpacitySlider_ValueChanged"/>
                    <TextBlock x:Name="OpacityLabel"
                               Text="80%" Width="36"
                               VerticalAlignment="Center" Margin="6,0,0,0"/>
                </StackPanel>

                <Separator Margin="0,12,0,8" Background="#FF444444"/>

                <!-- 캡처 영역 -->
                <TextBlock Text="캡처 영역" FontSize="11" Foreground="#666666" Margin="0,0,0,8"/>
                <Button Content="📐  캡처 범위 재설정"
                        HorizontalAlignment="Left"
                        Click="CalibrateButton_Click"/>
            </StackPanel>
        </TabItem>

        <!-- ── 소유 목록 탭 ── -->
        <TabItem Header="소유 목록">
            <Grid>
                <Grid.RowDefinitions>
                    <RowDefinition Height="*"/>
                    <RowDefinition Height="Auto"/>
                </Grid.RowDefinitions>

                <ScrollViewer Grid.Row="0" MaxHeight="400"
                              VerticalScrollBarVisibility="Auto"
                              Margin="14,10,14,0">
                    <StackPanel x:Name="WeaponListPanel"/>
                </ScrollViewer>

                <!-- 저장/취소 버튼 -->
                <StackPanel Grid.Row="1" Orientation="Horizontal"
                            HorizontalAlignment="Right"
                            Margin="14,8,14,12">
                    <Button Content="저장" Margin="0,0,8,0"
                            Click="SaveOwnedButton_Click"/>
                    <Button Content="취소"
                            Click="CancelOwnedButton_Click"/>
                </StackPanel>
            </Grid>
        </TabItem>

        <!-- ── 도움말 탭 ── -->
        <TabItem Header="도움말">
            <ScrollViewer MaxHeight="400" Margin="14,10,14,14"
                          VerticalScrollBarVisibility="Auto">
                <TextBlock x:Name="HelpText"
                           TextWrapping="Wrap"
                           FontFamily="Malgun Gothic" FontSize="13"
                           Foreground="#BBBBBB"
                           LineHeight="22"/>
            </ScrollViewer>
        </TabItem>

    </TabControl>
</Window>
```

---

## Task 8: SettingsWindow.xaml.cs — 신규 생성

**Files:**
- Create: `src/EndfieldEssenceOverlay/SettingsWindow.xaml.cs`

```csharp
// src/EndfieldEssenceOverlay/SettingsWindow.xaml.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EndfieldEssenceOverlay.Services;

namespace EndfieldEssenceOverlay;

public partial class SettingsWindow : Window
{
    private readonly Action<bool>   _onDebugText;
    private readonly Action<bool>   _onDebugImage;
    private readonly Action<double> _onOpacity;
    private readonly Action         _onCalibrate;
    private readonly TraitMatcherService _matcher;

    // 소유 목록 탭: 초기 체크 상태 (취소 시 복원용)
    private IReadOnlySet<string> _initialOwned = new HashSet<string>();

    public SettingsWindow(
        Action<bool>   onDebugText,
        Action<bool>   onDebugImage,
        Action<double> onOpacity,
        Action         onCalibrate,
        TraitMatcherService matcher)
    {
        InitializeComponent();
        _onDebugText  = onDebugText;
        _onDebugImage = onDebugImage;
        _onOpacity    = onOpacity;
        _onCalibrate  = onCalibrate;
        _matcher      = matcher;

        // 설정 탭 초기값
        DebugTextCheck.IsChecked  = Config.ShowDebugText;
        DebugImageCheck.IsChecked = Config.ShowDebugImage;
        PollIntervalBox.Text      = Config.PollIntervalMs.ToString();
        OpacitySlider.Value       = Math.Round(Config.OverlayOpacity * 100);
        OpacityLabel.Text         = $"{(int)OpacitySlider.Value}%";

        // 도움말 텍스트
        HelpText.Text = """
            📌 기본 사용법
            앱을 실행하면 게임 화면을 자동으로 스캔합니다.
            기질 조합이 감지되면 유효 / 소유 / 비유효 여부를 상단 오버레이로 표시합니다.

            📌 캡처 범위 설정
            설정 → [📐 캡처 범위 재설정] 을 클릭한 뒤
            게임 화면에서 기질 3개가 표시되는 패널 영역을 드래그로 선택하세요.
            처음 실행 시 자동으로 이 창이 열립니다.

            📌 소유 기질 관리
            설정 → 소유 목록 탭에서 보유 중인 무기에 체크하고 [저장]을 누르세요.
            또는 오버레이에서 유효 기질 감지 시 [소유] 버튼으로 바로 등록할 수 있습니다.
            """;

        // 소유 목록 탭 빌드
        BuildWeaponList();
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
        if (OpacityLabel == null) return; // InitializeComponent 전 이벤트 방어
        int pct = (int)OpacitySlider.Value;
        OpacityLabel.Text = $"{pct}%";
        _onOpacity(pct / 100.0);
    }

    private void CalibrateButton_Click(object s, RoutedEventArgs e)
        => _onCalibrate();

    // ── 소유 목록 탭 ──────────────────────────────────────────────

    private void BuildWeaponList()
    {
        _initialOwned = _matcher.OwnedWeaponNames;
        WeaponListPanel.Children.Clear();

        // 기질1(인덱스0) 기준으로 그룹핑
        var groups = _matcher.AllWeapons
            .GroupBy(w => w.Traits.Count > 0 ? w.Traits[0] : "기타");

        foreach (var group in groups)
        {
            // 그룹 헤더
            WeaponListPanel.Children.Add(new TextBlock
            {
                Text       = group.Key,
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize   = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x55, 0x99, 0xCC)),
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

                // 무기명 + 기질 부제목
                var panel = new StackPanel { Orientation = Orientation.Vertical };
                panel.Children.Add(new TextBlock
                {
                    Text       = weapon.Name,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize   = 13,
                    Foreground = new SolidColorBrush(Colors.White),
                });
                panel.Children.Add(new TextBlock
                {
                    Text       = string.Join(" · ", weapon.Traits),
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize   = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
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
        _initialOwned = _matcher.OwnedWeaponNames; // 저장 후 초기값 갱신
    }

    private void CancelOwnedButton_Click(object s, RoutedEventArgs e)
    {
        // 체크박스 상태를 초기값으로 복원
        foreach (var cb in WeaponListPanel.Children.OfType<CheckBox>())
            cb.IsChecked = _initialOwned.Contains((string)cb.Tag);
    }
}
```

**Step 1: 빌드 확인**
```
dotnet build src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
```

**Step 2: Commit**
```
git add src/EndfieldEssenceOverlay/SettingsWindow.xaml
git add src/EndfieldEssenceOverlay/SettingsWindow.xaml.cs
git add src/EndfieldEssenceOverlay/MainWindow.xaml
git add src/EndfieldEssenceOverlay/MainWindow.xaml.cs
git add src/EndfieldEssenceOverlay/Services/ScannerService.cs
git add src/EndfieldEssenceOverlay/Config.cs
git commit -m "feat: add SettingsWindow with tabs, chip UI, idle logic fix"
```

---

## Task 9: 전체 테스트 및 최종 검증

**Step 1: 테스트 실행**
```
dotnet test tests/EndfieldEssenceOverlay.Tests/
```
Expected: 9 tests PASS

**Step 2: 릴리즈 빌드**
```
dotnet publish src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj \
  -r win-x64 -c Release --self-contained \
  -p:PublishSingleFile=true -o publish
```

**Step 3: 동작 확인 체크리스트**
- [ ] 앱 실행 → 헤더에 ⚙ + ✕ 만 보임
- [ ] ⚙ 클릭 → 설정창 열림 (탭 3개)
- [ ] ⚙ 재클릭 → 기존 창 Focus (중복 생성 안 됨)
- [ ] OCR 텍스트 체크 → DebugPanel 즉시 표시/숨김
- [ ] OCR 이미지 체크 → DebugImageWindow 즉시 열림/닫힘
- [ ] 불투명도 슬라이더 → 오버레이 배경 실시간 변경
- [ ] 캡처 범위 버튼 → CalibrationWindow 열림
- [ ] 소유 목록 탭 → 전체 무기 체크박스 표시
- [ ] 체크 후 저장 → 재감지 시 ValidOwned 반환
- [ ] 취소 → 변경 내용 롤백
- [ ] 기질 칩 3개 색상 (파랑/초록/주황) 표시
- [ ] 엉뚱한 OCR → idle 상태 유지
- [ ] F10 눌러도 아무 반응 없음

**Step 4: Final Commit**
```
git add .
git commit -m "feat: complete settings window, owned editor, chip UI"
```
