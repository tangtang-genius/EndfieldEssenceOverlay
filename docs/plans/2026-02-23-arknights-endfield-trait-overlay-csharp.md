# Arknights Endfield Trait Overlay Tool — C# Implementation Plan


**Goal:** 단일 `.exe`로 배포 가능한 Windows 기질 오버레이 툴. 화면 고정 위치를 실시간 폴링하여 변화 감지 시 Windows 내장 OCR로 기질 키워드를 인식하고, 유효/소유 여부를 WPF 오버레이로 자동 표시.

**Architecture:** WPF 단일 창 앱. MainWindow가 오버레이 역할. ScannerService가 백그라운드에서 500ms마다 화면을 캡처하여 픽셀 변화를 감지하고, 변화가 있을 때만 Windows.Media.Ocr 실행 → TraitMatcherService 대조 → UI 자동 갱신. F10으로 클릭 투과 토글.

**Tech Stack:** C# 12, .NET 8 WPF, `net8.0-windows10.0.17763.0`, FuzzySharp (NuGet), Windows.Media.Ocr (WinRT 내장), User32 P/Invoke

**사전 요구사항:**
- Windows 10 1809+ (Build 17763+)
- .NET 8 SDK (https://dotnet.microsoft.com/download)
- 한국어 Windows OCR 언어팩 (한국어 Windows라면 기본 설치됨)

**배포:**
```bash
dotnet publish src/EndfieldEssenceOverlay -r win-x64 --self-contained -p:PublishSingleFile=true -c Release
# → 단일 .exe (~15-30MB)
```

---

## 실시간 스캔 흐름

```
앱 시작
  └─ ScannerService.Start()
       └─ 백그라운드 루프 (500ms 간격)
            ├─ 화면 캡처 (GDI)
            ├─ 픽셀 차이 계산
            │   ├─ 변화 없음 → 스킵 (OCR 안 돌림)
            │   └─ 변화 있음 ──────────────────────────┐
            │                                          ↓
            │                               Windows.Media.Ocr 실행
            │                                          ↓
            │                               키워드 3개 파싱
            │                                          ↓
            │                               TraitMatcherService 대조
            │                                          ↓
            └────────────────────────────── 오버레이 UI 자동 갱신
```

---

## 디렉터리 구조

```
arknights/
├── EndfieldEssenceOverlay.sln
├── src/
│   └── EndfieldEssenceOverlay/
│       ├── EndfieldEssenceOverlay.csproj
│       ├── App.xaml
│       ├── App.xaml.cs
│       ├── MainWindow.xaml
│       ├── MainWindow.xaml.cs
│       ├── Config.cs
│       ├── Models/
│       │   └── MatchResult.cs
│       ├── Services/
│       │   ├── ScreenCaptureService.cs
│       │   ├── OcrService.cs
│       │   ├── ScannerService.cs       ← 핵심: 폴링 + 변화 감지
│       │   ├── TraitMatcherService.cs
│       │   └── HotkeyService.cs        ← F10 투과 토글 전용
│       └── Data/
│           ├── valid_traits.txt
│           └── owned_traits.txt
└── tests/
    └── EndfieldEssenceOverlay.Tests/
        ├── EndfieldEssenceOverlay.Tests.csproj
        └── TraitMatcherServiceTests.cs
```

---

## Task 1: 솔루션 & 프로젝트 파일 생성

**Files:**
- Create: `EndfieldEssenceOverlay.sln`
- Create: `src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj`
- Create: `tests/EndfieldEssenceOverlay.Tests/EndfieldEssenceOverlay.Tests.csproj`

**Step 1: 솔루션 및 프로젝트 생성 (Windows에서 실행)**

```bash
cd arknights
dotnet new sln -n EndfieldEssenceOverlay
dotnet new wpf -n EndfieldEssenceOverlay -o src/EndfieldEssenceOverlay --framework net8.0-windows
dotnet new xunit -n EndfieldEssenceOverlay.Tests -o tests/EndfieldEssenceOverlay.Tests --framework net8.0-windows
dotnet sln add src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
dotnet sln add tests/EndfieldEssenceOverlay.Tests/EndfieldEssenceOverlay.Tests.csproj
```

**Step 2: csproj 수정 (WinRT OCR + FuzzySharp)**

`src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj` 교체:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.17763.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>EndfieldEssenceOverlay</AssemblyName>
    <RootNamespace>EndfieldEssenceOverlay</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FuzzySharp" Version="2.0.2" />
  </ItemGroup>
  <ItemGroup>
    <Content Include="Data\*.txt">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
</Project>
```

**Step 3: Tests csproj 수정**

`tests/EndfieldEssenceOverlay.Tests/EndfieldEssenceOverlay.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FuzzySharp" Version="2.0.2" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.9.0" />
    <PackageReference Include="xunit" Version="2.7.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.7">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <ProjectReference Include="..\..\src\EndfieldEssenceOverlay\EndfieldEssenceOverlay.csproj" />
  </ItemGroup>
</Project>
```

**Step 4: 빌드 확인**

```bash
dotnet restore && dotnet build
```

Expected: `Build succeeded. 0 Error(s)`

**Step 5: Commit**

```bash
git init
git add EndfieldEssenceOverlay.sln src/ tests/
git commit -m "feat: initialize C# WPF solution structure"
```

---

## Task 2: Config + 데이터 파일

**Files:**
- Create: `src/EndfieldEssenceOverlay/Config.cs`
- Create: `src/EndfieldEssenceOverlay/Data/valid_traits.txt`
- Create: `src/EndfieldEssenceOverlay/Data/owned_traits.txt`

**Step 1: Config.cs 작성**

```csharp
// src/EndfieldEssenceOverlay/Config.cs
namespace EndfieldEssenceOverlay;

public static class Config
{
    // F10: 클릭 투과 토글 (Virtual Key Code)
    public const uint VK_TOGGLE = 0x79;
    public const uint MOD_NONE  = 0x0000;

    // 캡처 영역 (픽셀 좌표) — 게임 해상도에 맞게 조정
    public const int CaptureLeft   = 50;
    public const int CaptureTop    = 200;
    public const int CaptureWidth  = 400;
    public const int CaptureHeight = 300;

    // OCR 이미지 업스케일 배율 (정확도 향상, 1 = 비활성)
    public const int UpscaleFactor = 2;

    // 실시간 스캔 설정
    public const int    PollIntervalMs  = 500;   // 폴링 주기 (ms)
    public const double ChangeThreshold = 10.0;  // 픽셀 평균 차이 임계값 (0~255)

    // 퍼지 매칭 임계값 (0~100)
    public const int FuzzyThreshold = 85;

    // 오버레이 창 초기 위치 & 크기
    public const int OverlayLeft   = 10;
    public const int OverlayTop    = 10;
    public const int OverlayWidth  = 460;
    public const int OverlayHeight = 130;

    // 데이터 파일 경로
    private static readonly string _baseDir =
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    public static string ValidTraitsPath =>
        Path.Combine(_baseDir, "Data", "valid_traits.txt");

    public static string OwnedTraitsPath =>
        Path.Combine(_baseDir, "Data", "owned_traits.txt");
}
```

**Step 2: valid_traits.txt 예시 데이터**

```
# 유효 기질 목록
# 형식: 키워드1,키워드2,키워드3  (순서 무관)
# # 으로 시작하는 줄은 주석
민첩 증가,치명타 확률 증가,고통
공격 강화,화염,치유
방어 관통,독,집중
속도 증가,냉기,재생
```

**Step 3: owned_traits.txt 초기 파일**

```
# 소유 중인 기질 목록 (앱 자동 관리)
```

**Step 4: Commit**

```bash
git add src/EndfieldEssenceOverlay/Config.cs src/EndfieldEssenceOverlay/Data/
git commit -m "feat: add Config with polling settings and trait data files"
```

---

## Task 3: Models + TraitMatcherService (TDD)

**Files:**
- Create: `src/EndfieldEssenceOverlay/Models/MatchResult.cs`
- Create: `src/EndfieldEssenceOverlay/Services/TraitMatcherService.cs`
- Create: `tests/EndfieldEssenceOverlay.Tests/TraitMatcherServiceTests.cs`

**Step 1: MatchResult 모델**

```csharp
// src/EndfieldEssenceOverlay/Models/MatchResult.cs
namespace EndfieldEssenceOverlay.Models;

public enum MatchStatus { Invalid, ValidUnowned, ValidOwned }

public record MatchResult(
    MatchStatus Status,
    string? MatchedName = null
);
```

**Step 2: 실패하는 테스트 작성**

```csharp
// tests/EndfieldEssenceOverlay.Tests/TraitMatcherServiceTests.cs
using EndfieldEssenceOverlay.Models;
using EndfieldEssenceOverlay.Services;

namespace EndfieldEssenceOverlay.Tests;

public class TraitMatcherServiceTests : IDisposable
{
    private readonly string _validPath = Path.GetTempFileName();
    private readonly string _ownedPath = Path.GetTempFileName();

    public void Dispose()
    {
        File.Delete(_validPath);
        File.Delete(_ownedPath);
    }

    private TraitMatcherService Make(string[] validLines, string[] ownedLines)
    {
        File.WriteAllLines(_validPath, validLines);
        File.WriteAllLines(_ownedPath, ownedLines);
        return new TraitMatcherService(_validPath, _ownedPath);
    }

    [Fact]
    public void Match_ExactKeywords_ReturnsValidUnowned()
    {
        var svc = Make(["민첩 증가,치명타 확률 증가,고통"], []);
        Assert.Equal(MatchStatus.ValidUnowned,
            svc.Match(["민첩 증가", "치명타 확률 증가", "고통"]).Status);
    }

    [Fact]
    public void Match_OrderIndependent_ReturnsValidUnowned()
    {
        var svc = Make(["민첩 증가,치명타 확률 증가,고통"], []);
        Assert.Equal(MatchStatus.ValidUnowned,
            svc.Match(["고통", "민첩 증가", "치명타 확률 증가"]).Status);
    }

    [Fact]
    public void Match_OwnedKeywords_ReturnsValidOwned()
    {
        var svc = Make(["민첩 증가,치명타 확률 증가,고통"],
                       ["민첩 증가,치명타 확률 증가,고통"]);
        Assert.Equal(MatchStatus.ValidOwned,
            svc.Match(["민첩 증가", "치명타 확률 증가", "고통"]).Status);
    }

    [Fact]
    public void Match_UnknownKeywords_ReturnsInvalid()
    {
        var svc = Make(["민첩 증가,치명타 확률 증가,고통"], []);
        Assert.Equal(MatchStatus.Invalid,
            svc.Match(["전혀", "다른", "키워드"]).Status);
    }

    [Fact]
    public void Match_FuzzyTypo_StillMatchesValidUnowned()
    {
        // OCR 오인식 시뮬레이션: '확률' -> '확율'
        var svc = Make(["민첩 증가,치명타 확률 증가,고통"], []);
        Assert.Equal(MatchStatus.ValidUnowned,
            svc.Match(["민첩 증가", "치명타 확율 증가", "고통"]).Status);
    }

    [Fact]
    public void Match_CommentsAndBlanksIgnored()
    {
        var svc = Make(["# 주석", "", "민첩 증가,치명타 확률 증가,고통", ""], []);
        Assert.Equal(MatchStatus.ValidUnowned,
            svc.Match(["민첩 증가", "치명타 확률 증가", "고통"]).Status);
    }

    [Fact]
    public void MarkOwned_AppendToFileAndMemory()
    {
        var svc = Make(["민첩 증가,치명타 확률 증가,고통"], []);
        svc.MarkOwned(["민첩 증가", "치명타 확률 증가", "고통"]);

        Assert.Contains("민첩 증가", File.ReadAllText(_ownedPath));
        Assert.Equal(MatchStatus.ValidOwned,
            svc.Match(["민첩 증가", "치명타 확률 증가", "고통"]).Status);
    }
}
```

**Step 3: 테스트 실행 (실패 확인)**

```bash
dotnet test tests/EndfieldEssenceOverlay.Tests
```

Expected: `error CS0246: 'TraitMatcherService' 형식을 찾을 수 없습니다`

**Step 4: TraitMatcherService 구현**

```csharp
// src/EndfieldEssenceOverlay/Services/TraitMatcherService.cs
using EndfieldEssenceOverlay.Models;
using FuzzySharp;

namespace EndfieldEssenceOverlay.Services;

public class TraitMatcherService
{
    private readonly string _ownedPath;
    private List<HashSet<string>> _valid;
    private List<HashSet<string>> _owned;

    public TraitMatcherService(string validPath, string ownedPath)
    {
        _ownedPath = ownedPath;
        _valid = LoadTraitFile(validPath);
        _owned = LoadTraitFile(ownedPath);
    }

    public MatchResult Match(IList<string> keywords)
    {
        foreach (var set in _owned)
            if (FuzzySetMatch(keywords, set))
                return new MatchResult(MatchStatus.ValidOwned, FormatName(set));

        foreach (var set in _valid)
            if (FuzzySetMatch(keywords, set))
                return new MatchResult(MatchStatus.ValidUnowned, FormatName(set));

        return new MatchResult(MatchStatus.Invalid);
    }

    public void MarkOwned(IList<string> keywords)
    {
        File.AppendAllText(_ownedPath, string.Join(",", keywords) + Environment.NewLine);
        _owned.Add(new HashSet<string>(keywords, StringComparer.OrdinalIgnoreCase));
    }

    private static List<HashSet<string>> LoadTraitFile(string path)
    {
        var result = new List<HashSet<string>>();
        if (!File.Exists(path)) return result;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith('#')) continue;

            var keywords = line.Split(',')
                               .Select(k => k.Trim())
                               .Where(k => !string.IsNullOrEmpty(k))
                               .ToArray();
            if (keywords.Length > 0)
                result.Add(new HashSet<string>(keywords, StringComparer.OrdinalIgnoreCase));
        }
        return result;
    }

    private static bool FuzzySetMatch(IList<string> scanned, HashSet<string> target)
    {
        if (scanned.Count != target.Count) return false;

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var keyword in scanned)
        {
            foreach (var candidate in target)
            {
                if (matched.Contains(candidate)) continue;
                if (Fuzz.Ratio(keyword, candidate) >= Config.FuzzyThreshold)
                {
                    matched.Add(candidate);
                    break;
                }
            }
        }
        return matched.Count == target.Count;
    }

    private static string FormatName(HashSet<string> set) =>
        string.Join(", ", set.OrderBy(k => k));
}
```

**Step 5: 테스트 통과 확인**

```bash
dotnet test tests/EndfieldEssenceOverlay.Tests -v normal
```

Expected: `7 passed, 0 failed`

**Step 6: Commit**

```bash
git add src/EndfieldEssenceOverlay/Models/ src/EndfieldEssenceOverlay/Services/TraitMatcherService.cs tests/
git commit -m "feat: add TraitMatcherService with fuzzy matching (TDD, 7 tests)"
```

---

## Task 4: ScreenCaptureService + OcrService

**Files:**
- Create: `src/EndfieldEssenceOverlay/Services/ScreenCaptureService.cs`
- Create: `src/EndfieldEssenceOverlay/Services/OcrService.cs`

**Step 1: ScreenCaptureService 구현**

```csharp
// src/EndfieldEssenceOverlay/Services/ScreenCaptureService.cs
using System.Drawing;
using System.Drawing.Imaging;

namespace EndfieldEssenceOverlay.Services;

public class ScreenCaptureService
{
    /// <summary>
    /// Config 영역을 캡처하여 업스케일된 Bitmap 반환.
    /// 호출자가 Dispose 책임.
    /// </summary>
    public Bitmap Capture()
    {
        var src = new Bitmap(Config.CaptureWidth, Config.CaptureHeight,
                             PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(src);
        g.CopyFromScreen(Config.CaptureLeft, Config.CaptureTop, 0, 0,
                         new Size(Config.CaptureWidth, Config.CaptureHeight),
                         CopyPixelOperation.SourceCopy);

        if (Config.UpscaleFactor <= 1) return src;

        int newW = Config.CaptureWidth  * Config.UpscaleFactor;
        int newH = Config.CaptureHeight * Config.UpscaleFactor;
        var dst = new Bitmap(newW, newH, PixelFormat.Format32bppArgb);
        using var gDst = Graphics.FromImage(dst);
        gDst.InterpolationMode =
            System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        gDst.DrawImage(src, 0, 0, newW, newH);
        src.Dispose();
        return dst;
    }

    /// <summary>
    /// 변화 감지용: 업스케일 없이 원본 크기로 빠르게 캡처.
    /// </summary>
    public Bitmap CaptureRaw()
    {
        var bmp = new Bitmap(Config.CaptureWidth, Config.CaptureHeight,
                             PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(Config.CaptureLeft, Config.CaptureTop, 0, 0,
                         new Size(Config.CaptureWidth, Config.CaptureHeight),
                         CopyPixelOperation.SourceCopy);
        return bmp;
    }
}
```

**Step 2: OcrService 구현**

```csharp
// src/EndfieldEssenceOverlay/Services/OcrService.cs
using System.Drawing;
using System.Drawing.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace EndfieldEssenceOverlay.Services;

public class OcrService
{
    private OcrEngine? _engine;

    public void Initialize()
    {
        var language = new Language("ko");
        if (!OcrEngine.IsLanguageSupported(language))
            throw new InvalidOperationException(
                "한국어 OCR 언어팩이 없습니다.\n" +
                "Windows 설정 > 시간 및 언어 > 언어 > 한국어 추가 후 재시작하세요.");

        _engine = OcrEngine.TryCreateFromLanguage(language)
            ?? throw new InvalidOperationException("OcrEngine 초기화 실패");
    }

    /// <summary>
    /// 업스케일된 Bitmap을 OCR하여 키워드 최대 3개 반환.
    /// 3개 미만이면 빈 리스트.
    /// </summary>
    public async Task<List<string>> ExtractKeywordsAsync(Bitmap bitmap)
    {
        if (_engine is null) throw new InvalidOperationException("Initialize() 먼저 호출하세요.");

        var softBitmap = await ToSoftwareBitmapAsync(bitmap);
        var result     = await _engine.RecognizeAsync(softBitmap);

        var lines = result.Lines
            .Select(l => l.Text.Trim())
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        return lines.Count >= 3 ? lines.Take(3).ToList() : [];
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        bitmap.Save(ms, ImageFormat.Bmp);
        ms.Position = 0;

        using var ras = ms.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras);
        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}
```

**Step 3: 빌드 확인**

```bash
dotnet build src/EndfieldEssenceOverlay
```

Expected: `Build succeeded. 0 Error(s)`

**Step 4: Commit**

```bash
git add src/EndfieldEssenceOverlay/Services/ScreenCaptureService.cs \
        src/EndfieldEssenceOverlay/Services/OcrService.cs
git commit -m "feat: add screen capture and Windows OCR service"
```

---

## Task 5: ScannerService (실시간 폴링 + 변화 감지)

**Files:**
- Create: `src/EndfieldEssenceOverlay/Services/ScannerService.cs`

핵심 로직:
1. 백그라운드 루프에서 `PollIntervalMs`마다 `CaptureRaw()` 실행
2. 이전 프레임과 픽셀 평균 차이 계산
3. 차이 > `ChangeThreshold` 일 때만 OCR 실행
4. 키워드 추출 성공 시 `KeywordsDetected` 이벤트 발생

**Step 1: ScannerService 구현**

```csharp
// src/EndfieldEssenceOverlay/Services/ScannerService.cs
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;

namespace EndfieldEssenceOverlay.Services;

public class ScannerService : IDisposable
{
    private readonly ScreenCaptureService _capture;
    private readonly OcrService           _ocr;
    private CancellationTokenSource?      _cts;
    private byte[]?                       _previousFrame;

    /// <summary>키워드 3개가 성공적으로 감지될 때 발생 (UI 스레드에서 호출됨)</summary>
    public event Action<List<string>>? KeywordsDetected;

    public ScannerService(ScreenCaptureService capture, OcrService ocr)
    {
        _capture = capture;
        _ocr     = ocr;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        Task.Run(() => ScanLoop(_cts.Token));
    }

    public void Stop() => _cts?.Cancel();

    // ── 백그라운드 루프 ───────────────────────────────────────────

    private async Task ScanLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ScanOnce();
                await Task.Delay(Config.PollIntervalMs, ct);
            }
            catch (OperationCanceledException) { break; }
            catch { /* 일시적 오류 무시, 루프 유지 */ }
        }
    }

    private async Task ScanOnce()
    {
        // 1. 변화 감지용 빠른 캡처 (업스케일 없음)
        byte[] currentFrame;
        using (var raw = _capture.CaptureRaw())
            currentFrame = ToGrayscaleBytes(raw);

        // 2. 이전 프레임과 비교
        if (_previousFrame is not null &&
            !HasSignificantChange(currentFrame, _previousFrame))
            return; // 변화 없음 → OCR 스킵

        _previousFrame = currentFrame;

        // 3. OCR용 업스케일 캡처
        using var bitmap  = _capture.Capture();
        var keywords = await _ocr.ExtractKeywordsAsync(bitmap);

        if (keywords.Count >= 3)
            Application.Current.Dispatcher.Invoke(
                () => KeywordsDetected?.Invoke(keywords));
    }

    // ── 픽셀 유틸 ────────────────────────────────────────────────

    private static byte[] ToGrayscaleBytes(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly,
                                   PixelFormat.Format32bppArgb);

        int   stride    = Math.Abs(data.Stride);
        int   pixelCount = bitmap.Width * bitmap.Height;
        var   raw        = new byte[stride * bitmap.Height];
        Marshal.Copy(data.Scan0, raw, 0, raw.Length);
        bitmap.UnlockBits(data);

        var gray = new byte[pixelCount];
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width;  x++)
        {
            int src = y * stride + x * 4;
            // 가중 평균 그레이스케일 (정수 연산)
            gray[y * bitmap.Width + x] = (byte)(
                (raw[src + 2] * 77 + raw[src + 1] * 150 + raw[src] * 29) >> 8);
        }
        return gray;
    }

    private static bool HasSignificantChange(byte[] current, byte[] previous)
    {
        if (current.Length != previous.Length) return true;

        long sum = 0;
        for (int i = 0; i < current.Length; i++)
            sum += Math.Abs(current[i] - previous[i]);

        double meanDiff = (double)sum / current.Length;
        return meanDiff > Config.ChangeThreshold;
    }

    public void Dispose() => Stop();
}
```

**Step 2: 빌드 확인**

```bash
dotnet build src/EndfieldEssenceOverlay
```

Expected: `Build succeeded. 0 Error(s)`

**Step 3: Commit**

```bash
git add src/EndfieldEssenceOverlay/Services/ScannerService.cs
git commit -m "feat: add real-time ScannerService with pixel change detection"
```

---

## Task 6: HotkeyService (F10 클릭 투과 전용)

**Files:**
- Create: `src/EndfieldEssenceOverlay/Services/HotkeyService.cs`

**Step 1: HotkeyService 구현**

```csharp
// src/EndfieldEssenceOverlay/Services/HotkeyService.cs
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace EndfieldEssenceOverlay.Services;

public class HotkeyService : IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    private IntPtr _hwnd;
    private HwndSource? _source;
    private readonly Dictionary<int, Action> _callbacks = [];
    private int _nextId = 9000;

    public void Initialize(Window window)
    {
        _hwnd   = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_hwnd);
        _source.AddHook(WndProc);
    }

    public void Register(uint modifiers, uint vk, Action callback)
    {
        int id = _nextId++;
        RegisterHotKey(_hwnd, id, modifiers, vk);
        _callbacks[id] = callback;
    }

    public void Dispose()
    {
        foreach (var id in _callbacks.Keys)
            UnregisterHotKey(_hwnd, id);
        _source?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
                           IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY &&
            _callbacks.TryGetValue(wParam.ToInt32(), out var cb))
        {
            cb();
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(
        IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
```

**Step 2: Commit**

```bash
git add src/EndfieldEssenceOverlay/Services/HotkeyService.cs
git commit -m "feat: add HotkeyService for F10 click-through toggle"
```

---

## Task 7: WPF 오버레이 UI (MainWindow)

**Files:**
- Modify: `src/EndfieldEssenceOverlay/MainWindow.xaml`
- Modify: `src/EndfieldEssenceOverlay/MainWindow.xaml.cs`
- Modify: `src/EndfieldEssenceOverlay/App.xaml`

**Step 1: MainWindow.xaml 작성**

```xml
<!-- src/EndfieldEssenceOverlay/MainWindow.xaml -->
<Window x:Class="EndfieldEssenceOverlay.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="기질 오버레이"
        Width="460" Height="130"
        Left="10" Top="10"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="#CC1A1A1A"
        Topmost="True"
        ResizeMode="NoResize"
        ShowInTaskbar="False"
        MouseLeftButtonDown="Window_MouseLeftButtonDown">
    <Grid>
        <StackPanel Orientation="Horizontal" Margin="4,0">
            <!-- 상태 아이콘 -->
            <TextBlock x:Name="IconText"
                       Text="⏳" FontSize="32"
                       VerticalAlignment="Center"
                       Margin="12,0,8,0"/>

            <!-- 텍스트 영역 -->
            <StackPanel VerticalAlignment="Center" MaxWidth="360">
                <TextBlock x:Name="StatusText"
                           Text="초기화 중..."
                           FontFamily="Malgun Gothic"
                           FontSize="14" FontWeight="Bold"
                           Foreground="#AAAAAA"/>
                <TextBlock x:Name="DetailText"
                           Text=""
                           FontFamily="Malgun Gothic" FontSize="11"
                           Foreground="#888888" TextWrapping="Wrap"
                           Visibility="Collapsed"/>
                <Button x:Name="OwnedButton"
                        Content="[소유 중] 클릭하여 등록"
                        FontFamily="Malgun Gothic" FontSize="11"
                        Background="#333333" Foreground="#44FF88"
                        BorderBrush="#44FF88" BorderThickness="1"
                        Padding="8,3" Margin="0,4,0,0" Cursor="Hand"
                        Visibility="Collapsed"
                        Click="OwnedButton_Click"/>
            </StackPanel>
        </StackPanel>

        <!-- 닫기 버튼 -->
        <Button Content="✕"
                HorizontalAlignment="Right" VerticalAlignment="Top"
                Width="24" Height="24"
                Background="Transparent" Foreground="#666666"
                BorderThickness="0" FontSize="12" Cursor="Hand"
                Click="CloseButton_Click"/>
    </Grid>
</Window>
```

**Step 2: MainWindow.xaml.cs 작성**

```csharp
// src/EndfieldEssenceOverlay/MainWindow.xaml.cs
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EndfieldEssenceOverlay.Models;
using EndfieldEssenceOverlay.Services;

namespace EndfieldEssenceOverlay;

public partial class MainWindow : Window
{
    private readonly ScreenCaptureService _capture  = new();
    private readonly OcrService           _ocr      = new();
    private readonly TraitMatcherService  _matcher;
    private readonly HotkeyService        _hotkey   = new();
    private          ScannerService?      _scanner;
    private          bool                 _clickThrough = false;
    private          List<string>         _lastKeywords = [];

    public MainWindow()
    {
        InitializeComponent();
        EnsureDataFiles();
        _matcher = new TraitMatcherService(Config.ValidTraitsPath,
                                           Config.OwnedTraitsPath);
        Task.Run(InitializeAsync);
    }

    private async Task InitializeAsync()
    {
        try
        {
            _ocr.Initialize();
            _scanner = new ScannerService(_capture, _ocr);
            _scanner.KeywordsDetected += OnKeywordsDetected;
            _scanner.Start();
            SetStatus("idle", "실시간 스캔 중 | F10 = 투과 토글");
        }
        catch (Exception ex)
        {
            SetStatus("error", ex.Message);
        }
        await Task.CompletedTask;
    }

    // ── 단축키 등록 ───────────────────────────────────────────────
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hotkey.Initialize(this);
        _hotkey.Register(Config.MOD_NONE, Config.VK_TOGGLE, OnToggleClickThrough);
    }

    // ── 스캔 결과 수신 (ScannerService → UI 스레드) ──────────────
    private void OnKeywordsDetected(List<string> keywords)
    {
        _lastKeywords = keywords;
        var result = _matcher.Match(keywords);
        ApplyMatchResult(result, keywords);
    }

    private void ApplyMatchResult(MatchResult result, List<string> keywords)
    {
        switch (result.Status)
        {
            case MatchStatus.Invalid:
                SetStatus("invalid", "비유효 기질");
                break;
            case MatchStatus.ValidUnowned:
                SetStatus("valid_unowned",
                    result.MatchedName ?? string.Join(", ", keywords));
                break;
            case MatchStatus.ValidOwned:
                SetStatus("valid_owned",
                    $"이미 소유 중: {result.MatchedName}");
                break;
        }
    }

    // ── [소유 중] 버튼 ────────────────────────────────────────────
    private void OwnedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_lastKeywords.Count == 0) return;
        _matcher.MarkOwned(_lastKeywords);
        SetStatus("valid_owned",
            $"이미 소유 중: {string.Join(", ", _lastKeywords)}");
    }

    // ── F10: 클릭 투과 토글 ───────────────────────────────────────
    private void OnToggleClickThrough()
    {
        _clickThrough = !_clickThrough;
        Dispatcher.Invoke(() => SetClickThrough(_clickThrough));
    }

    // ── UI 상태 갱신 ──────────────────────────────────────────────
    private static readonly Dictionary<string, (string Icon, Color Color)> _styles = new()
    {
        ["idle"]          = ("🔄", Colors.DarkGray),
        ["invalid"]       = ("❌", Color.FromRgb(0xFF, 0x44, 0x44)),
        ["valid_unowned"] = ("✅", Color.FromRgb(0x44, 0xFF, 0x88)),
        ["valid_owned"]   = ("⚠️", Color.FromRgb(0xFF, 0xDD, 0x44)),
        ["error"]         = ("⚠️", Color.FromRgb(0xFF, 0x88, 0x00)),
    };

    private void SetStatus(string status, string message)
    {
        Dispatcher.Invoke(() =>
        {
            var (icon, color) = _styles.GetValueOrDefault(status, ("❓", Colors.White));
            var brush = new SolidColorBrush(color);

            IconText.Text       = icon;
            IconText.Foreground = brush;
            StatusText.Text     = message;
            StatusText.Foreground = brush;

            bool isUnowned = status == "valid_unowned";
            DetailText.Visibility  = isUnowned ? Visibility.Visible   : Visibility.Collapsed;
            OwnedButton.Visibility = isUnowned ? Visibility.Visible   : Visibility.Collapsed;
            if (isUnowned) DetailText.Text = message;
        });
    }

    // ── 클릭 투과 (Win32) ────────────────────────────────────────
    private void SetClickThrough(bool enable)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        const int GWL_EXSTYLE       = -20;
        const int WS_EX_LAYERED     = 0x00080000;
        const int WS_EX_TRANSPARENT = 0x00000020;

        int style = GetWindowLong(hwnd, GWL_EXSTYLE);
        style = enable
            ? style | WS_EX_LAYERED | WS_EX_TRANSPARENT
            : style & ~WS_EX_TRANSPARENT;
        SetWindowLong(hwnd, GWL_EXSTYLE, style);
    }

    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int n);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int n, int v);

    // ── 기타 ─────────────────────────────────────────────────────
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Application.Current.Shutdown();

    protected override void OnClosed(EventArgs e)
    {
        _scanner?.Dispose();
        _hotkey.Dispose();
        base.OnClosed(e);
    }

    private static void EnsureDataFiles()
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(Config.ValidTraitsPath)!);

        if (!File.Exists(Config.ValidTraitsPath))
            File.WriteAllText(Config.ValidTraitsPath,
                "# 유효 기질 목록 (키워드1,키워드2,키워드3)\n");
        if (!File.Exists(Config.OwnedTraitsPath))
            File.WriteAllText(Config.OwnedTraitsPath,
                "# 소유 중인 기질 목록 (자동 관리)\n");
    }
}
```

**Step 3: App.xaml 확인**

```xml
<!-- src/EndfieldEssenceOverlay/App.xaml -->
<Application x:Class="EndfieldEssenceOverlay.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             StartupUri="MainWindow.xaml">
    <Application.Resources/>
</Application>
```

**Step 4: 빌드 확인**

```bash
dotnet build src/EndfieldEssenceOverlay
```

Expected: `Build succeeded. 0 Error(s)`

**Step 5: Commit**

```bash
git add src/EndfieldEssenceOverlay/MainWindow.xaml \
        src/EndfieldEssenceOverlay/MainWindow.xaml.cs \
        src/EndfieldEssenceOverlay/App.xaml
git commit -m "feat: add WPF overlay UI with real-time scan integration"
```

---

## Task 8: 최종 빌드 & 배포 & 수동 검증

**Step 1: 단위 테스트 전체 확인**

```bash
dotnet test tests/EndfieldEssenceOverlay.Tests -v normal
```

Expected: `7 passed, 0 failed`

**Step 2: Release 실행 테스트**

```bash
dotnet run --project src/EndfieldEssenceOverlay -c Release
```

Expected: 오버레이 창 표시, "실시간 스캔 중 | F10 = 투과 토글"

**Step 3: 단일 .exe 퍼블리시**

```bash
dotnet publish src/EndfieldEssenceOverlay \
  -r win-x64 \
  --self-contained \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -c Release \
  -o publish/
```

Expected: `publish/EndfieldEssenceOverlay.exe` (~15-30MB)

**Step 4: 수동 검증 체크리스트**

```
□ exe 더블클릭 → "실시간 스캔 중" 표시
□ 오버레이가 항상 최상위 유지
□ 드래그로 위치 이동 가능
□ 게임에서 기질 패널 열기 → 0.5~1초 내 자동 결과 표시
□ valid_traits.txt 기질 → ✅ 초록 표시
□ 없는 기질 → ❌ 빨간 표시
□ [소유 중] 클릭 → ⚠️ 노란 + owned_traits.txt 기록
□ 앱 재시작 후 owned_traits.txt 유지
□ 기질 패널 닫기 → 오버레이 변화 없음 (마지막 결과 유지)
□ F10 → 마우스 클릭 투과 (게임에 전달)
□ [✕] 클릭 → 정상 종료
```

**Step 5: 캡처 영역 캘리브레이션**

```
1. 게임 실행, 기질 패널 오픈
2. 스니핑 도구(Win+Shift+S)로 텍스트 3개 영역 좌표 확인
3. Config.cs의 CaptureLeft/Top/Width/Height 수정
4. 앱 재빌드 후 결과 확인
```

**Step 6: Final Commit**

```bash
git add .
git commit -m "feat: complete trait overlay v1.0 - real-time scanning, single exe"
git tag v1.0.0
```

---

## 배포 패키지

```
EndfieldEssenceOverlay_v1.0.zip
├── EndfieldEssenceOverlay.exe   ← 더블클릭 실행
└── Data/
    ├── valid_traits.txt        ← 유저가 편집
    └── owned_traits.txt        ← 앱 자동 관리
```
