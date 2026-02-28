# Template Matching 기질 인식 구현 계획


**Goal:** OCR(Tesseract) 대신 OpenCvSharp4 이미지 템플릿 매칭으로 기질 키워드를 인식한다 — 게임 폰트 의존성 없이 사용자가 제공한 PNG 크롭으로 안정적으로 매칭.

**Architecture:** `TemplateMatchService`가 `OcrService`와 동일한 인터페이스(`Initialize()` + `ExtractLinesAsync(Bitmap)` → `List<string>`)를 구현하므로 `TraitMatcherService`와 그 이하 파이프라인은 무수정. `ScannerService`에서 `CaptureRaw()`를 직접 전달해 색반전 없는 원본 이미지로 매칭. 파일명 = 키워드 문자열 (`Data/templates/흐름.png` → `"흐름"`). CCoeffNormed로 ±20% 멀티스케일 NCC 수행.

**Tech Stack:** C# 12 / .NET 8 / WPF, OpenCvSharp4 4.9.0, `Data/templates/*.png` (사용자 제공 게임 스크린샷 크롭)

> **빌드 환경 주의:** WSL에서 `dotnet` 실행 불가. 빌드/테스트는 Windows PowerShell에서 수행.

---

## Task 1: NuGet 추가 + Config.TemplateThreshold + 템플릿 폴더

**Files:**
- Modify: `src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj`
- Modify: `tests/EndfieldEssenceOverlay.Tests/EndfieldEssenceOverlay.Tests.csproj`
- Modify: `src/EndfieldEssenceOverlay/Config.cs`
- Create: `src/EndfieldEssenceOverlay/Data/templates/.gitkeep`

**Step 1: 메인 프로젝트 csproj에 OpenCvSharp4 추가**

기존 `<ItemGroup>` (FuzzySharp, System.Drawing.Common, Tesseract)에 추가:

```xml
<PackageReference Include="OpenCvSharp4" Version="4.9.0.20240103" />
<PackageReference Include="OpenCvSharp4.runtime.win" Version="4.9.0.20240103" />
```

기존 Content ItemGroup들 아래에 추가:

```xml
<ItemGroup>
  <Content Include="Data\templates\**\*.png">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

**Step 2: 테스트 프로젝트 csproj에 OpenCvSharp4 추가**

기존 `<ItemGroup>` (FuzzySharp, xunit)에 추가:

```xml
<PackageReference Include="OpenCvSharp4" Version="4.9.0.20240103" />
<PackageReference Include="OpenCvSharp4.runtime.win" Version="4.9.0.20240103" />
```

**Step 3: Config.cs에 TemplateThreshold 추가**

`SnapDisplayThreshold` 상수 아래에 추가:

```csharp
// 템플릿 매칭 임계값: CCoeffNormed 점수 (0.0~1.0), 이 값 이상이면 키워드 감지로 판정
public const double TemplateThreshold = 0.80;
```

**Step 4: 템플릿 폴더 생성**

```bash
mkdir -p src/EndfieldEssenceOverlay/Data/templates
touch src/EndfieldEssenceOverlay/Data/templates/.gitkeep
```

**Step 5: 빌드 확인 (Windows PowerShell)**

```powershell
dotnet restore src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
dotnet build src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
```

Expected: `Build succeeded`

**Step 6: Commit**

```bash
git add src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
git add tests/EndfieldEssenceOverlay.Tests/EndfieldEssenceOverlay.Tests.csproj
git add src/EndfieldEssenceOverlay/Config.cs
git add src/EndfieldEssenceOverlay/Data/templates/.gitkeep
git commit -m "feat: add OpenCvSharp4 NuGet + TemplateThreshold config + templates dir"
```

---

## Task 2: TemplateMatchService 테스트 작성

**Files:**
- Create: `tests/EndfieldEssenceOverlay.Tests/TemplateMatchServiceTests.cs`

**Step 1: 테스트 파일 작성**

```csharp
// tests/EndfieldEssenceOverlay.Tests/TemplateMatchServiceTests.cs
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using EndfieldEssenceOverlay.Services;
using OpenCvSharp;
using Xunit;

namespace EndfieldEssenceOverlay.Tests;

public class TemplateMatchServiceTests : IDisposable
{
    private readonly string _tempDir;

    public TemplateMatchServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // 그레이스케일 Bitmap → OpenCV Mat 변환 헬퍼
    private static Bitmap MatToGrayBitmap(Mat mat)
    {
        var bmp = new Bitmap(mat.Width, mat.Height, PixelFormat.Format32bppArgb);
        for (int y = 0; y < mat.Height; y++)
        for (int x = 0; x < mat.Width;  x++)
        {
            byte v = mat.At<byte>(y, x);
            bmp.SetPixel(x, y, Color.FromArgb(v, v, v));
        }
        return bmp;
    }

    // 뚜렷한 패턴 생성: 위쪽 절반 밝음(200), 아래쪽 절반 어두움(50)
    private static Mat MakePatternMat(int w, int h)
    {
        var mat = new Mat(h, w, MatType.CV_8UC1);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w;  x++)
            mat.At<byte>(y, x) = y < h / 2 ? (byte)200 : (byte)50;
        return mat;
    }

    [Fact]
    public async Task ExtractLinesAsync_NoTemplates_ReturnsEmpty()
    {
        // 빈 디렉터리 — 템플릿 없음
        var svc = new TemplateMatchService(_tempDir);
        svc.Initialize();

        using var bmp = new Bitmap(100, 100);
        var result = await svc.ExtractLinesAsync(bmp);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractLinesAsync_TemplateExistsInSource_ReturnsKeyword()
    {
        // 20x20 패턴 템플릿 파일로 저장
        using var tpl = MakePatternMat(20, 20);
        Cv2.ImWrite(Path.Combine(_tempDir, "흐름.png"), tpl);

        var svc = new TemplateMatchService(_tempDir);
        svc.Initialize();

        // 100x100 배경(gray=100)에 동일 패턴을 (30, 30)에 삽입
        using var src = new Mat(100, 100, MatType.CV_8UC1, new Scalar(100));
        for (int y = 0; y < 20; y++)
        for (int x = 0; x < 20;  x++)
            src.At<byte>(30 + y, 30 + x) = tpl.At<byte>(y, x);

        using var bmp = MatToGrayBitmap(src);
        var result = await svc.ExtractLinesAsync(bmp);

        Assert.Contains("흐름", result);
    }

    [Fact]
    public async Task ExtractLinesAsync_TemplateNotInSource_ReturnsEmpty()
    {
        // 20x20 패턴 템플릿 파일로 저장
        using var tpl = MakePatternMat(20, 20);
        Cv2.ImWrite(Path.Combine(_tempDir, "흐름.png"), tpl);

        var svc = new TemplateMatchService(_tempDir);
        svc.Initialize();

        // 균일한 배경 — 패턴 없음
        using var src = new Mat(100, 100, MatType.CV_8UC1, new Scalar(100));
        using var bmp = MatToGrayBitmap(src);
        var result = await svc.ExtractLinesAsync(bmp);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ExtractLinesAsync_MultipleTemplates_ReturnsOnlyMatched()
    {
        // 두 가지 다른 패턴 템플릿 저장
        using var tplA = MakePatternMat(20, 20);           // 밝음/어두움 패턴
        using var tplB = new Mat(20, 20, MatType.CV_8UC1); // 균일 어두움 (별도 패턴)
        tplB.SetTo(new Scalar(30));
        Cv2.ImWrite(Path.Combine(_tempDir, "효율.png"), tplA);
        Cv2.ImWrite(Path.Combine(_tempDir, "강공.png"), tplB);

        var svc = new TemplateMatchService(_tempDir);
        svc.Initialize();

        // 소스에는 tplA만 삽입
        using var src = new Mat(100, 100, MatType.CV_8UC1, new Scalar(100));
        for (int y = 0; y < 20; y++)
        for (int x = 0; x < 20;  x++)
            src.At<byte>(30 + y, 30 + x) = tplA.At<byte>(y, x);

        using var bmp = MatToGrayBitmap(src);
        var result = await svc.ExtractLinesAsync(bmp);

        Assert.Contains("효율", result);
        Assert.DoesNotContain("강공", result);
    }
}
```

**Step 2: 테스트 실행 — 실패 확인 (Windows PowerShell)**

```powershell
dotnet test tests/EndfieldEssenceOverlay.Tests/EndfieldEssenceOverlay.Tests.csproj --filter "TemplateMatchServiceTests"
```

Expected: 컴파일 오류 (`TemplateMatchService` 클래스 없음)

**Step 3: Commit**

```bash
git add tests/EndfieldEssenceOverlay.Tests/TemplateMatchServiceTests.cs
git commit -m "test: add failing TemplateMatchService tests"
```

---

## Task 3: TemplateMatchService 구현

**Files:**
- Create: `src/EndfieldEssenceOverlay/Services/TemplateMatchService.cs`

**Step 1: TemplateMatchService.cs 작성**

```csharp
// src/EndfieldEssenceOverlay/Services/TemplateMatchService.cs
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using OpenCvSharp;

namespace EndfieldEssenceOverlay.Services;

public class TemplateMatchService : IDisposable
{
    private readonly string _templatesDir;
    private readonly List<(string Keyword, Mat Template)> _templates = [];

    // 프로덕션 경로 기본값
    public TemplateMatchService() : this(
        Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory,
            "Data", "templates"))
    { }

    // 테스트용: 디렉터리 주입
    public TemplateMatchService(string templatesDir)
    {
        _templatesDir = templatesDir;
    }

    public void Initialize()
    {
        if (!Directory.Exists(_templatesDir)) return;

        foreach (var file in Directory.GetFiles(_templatesDir, "*.png"))
        {
            var keyword = Path.GetFileNameWithoutExtension(file);
            var mat = Cv2.ImRead(file, ImreadModes.Grayscale);
            if (!mat.Empty())
                _templates.Add((keyword, mat));
        }
    }

    /// <summary>
    /// 캡처된 Bitmap에서 등록된 템플릿 키워드를 찾아 반환.
    /// 파이프라인 인터페이스는 OcrService와 동일.
    /// </summary>
    public async Task<List<string>> ExtractLinesAsync(Bitmap bitmap)
    {
        if (_templates.Count == 0) return [];

        return await Task.Run(() =>
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            using var source = Cv2.ImDecode(ms.ToArray(), ImreadModes.Grayscale);
            if (source.Empty()) return [];

            return _templates
                .Where(t => MatchesAtAnyScale(source, t.Template))
                .Select(t => t.Keyword)
                .ToList();
        });
    }

    // ±20% 멀티스케일 NCC. 한 스케일에서라도 임계값 초과시 true.
    private static bool MatchesAtAnyScale(Mat source, Mat template)
    {
        double[] scales = [0.80, 0.90, 1.00, 1.10, 1.20];

        foreach (double scale in scales)
        {
            int tw = Math.Max(1, (int)(template.Width  * scale));
            int th = Math.Max(1, (int)(template.Height * scale));
            if (tw > source.Width || th > source.Height) continue;

            using var scaled = template.Resize(new Size(tw, th));
            using var result = new Mat();
            Cv2.MatchTemplate(source, scaled, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out _);

            if (maxVal >= Config.TemplateThreshold) return true;
        }
        return false;
    }

    public void Dispose()
    {
        foreach (var (_, mat) in _templates)
            mat.Dispose();
        _templates.Clear();
    }
}
```

**Step 2: 테스트 실행 — 통과 확인 (Windows PowerShell)**

```powershell
dotnet test tests/EndfieldEssenceOverlay.Tests/EndfieldEssenceOverlay.Tests.csproj --filter "TemplateMatchServiceTests"
```

Expected: 4개 테스트 PASS

**Step 3: Commit**

```bash
git add src/EndfieldEssenceOverlay/Services/TemplateMatchService.cs
git commit -m "feat: implement TemplateMatchService with multi-scale NCC"
```

---

## Task 4: ScannerService 교체

**Files:**
- Modify: `src/EndfieldEssenceOverlay/Services/ScannerService.cs`

ScannerService는 `CaptureRaw()` 하나만 사용하도록 단순화하고, 타입을 `TemplateMatchService`로 교체한다.

**Step 1: ScannerService.cs 수정**

필드 및 생성자:

```csharp
// Before:
private readonly OcrService           _ocr;
public ScannerService(ScreenCaptureService capture, OcrService ocr)
{
    _capture = capture;
    _ocr     = ocr;
}

// After:
private readonly TemplateMatchService _templateMatcher;
public ScannerService(ScreenCaptureService capture, TemplateMatchService templateMatcher)
{
    _capture         = capture;
    _templateMatcher = templateMatcher;
}
```

`ScanOnce()` 메서드 — `Capture()` 제거, `rawBmp` 재사용:

```csharp
private async Task ScanOnce()
{
    // 1. 캡처 (변화 감지 + 템플릿 매칭 겸용)
    using var rawBmp = _capture.CaptureRaw();
    if (rawBmp == null) return;

    byte[] currentFrame = ToGrayscaleBytes(rawBmp);

    // 2. 이전 프레임과 비교 (변화 없어도 2초마다 강제 스캔)
    bool forceOcr = (DateTime.UtcNow - _lastOcrTime).TotalMilliseconds > 2000;
    if (_previousFrame is not null &&
        !HasSignificantChange(currentFrame, _previousFrame) &&
        !forceOcr)
        return;

    _previousFrame = currentFrame;
    _lastOcrTime   = DateTime.UtcNow;

    if (Config.ShowDebugImage)
    {
        var pngBytes = BitmapToPng(rawBmp);
        Application.Current.Dispatcher.Invoke(
            () => DebugOcrImage?.Invoke(pngBytes));
    }

    var lines = await _templateMatcher.ExtractLinesAsync(rawBmp);

    if (Config.ShowDebugText)
        Application.Current.Dispatcher.Invoke(
            () => DebugOcrLines?.Invoke(lines));

    if (lines.Count > 0)
        Application.Current.Dispatcher.Invoke(
            () => KeywordsDetected?.Invoke(lines));
}
```

**Step 2: 빌드 확인 (Windows PowerShell)**

```powershell
dotnet build src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
```

Expected: `error CS0246: 'OcrService'` 오류 — MainWindow 아직 안 바꿨으므로 정상

**Step 3: Commit**

```bash
git add src/EndfieldEssenceOverlay/Services/ScannerService.cs
git commit -m "refactor: ScannerService uses TemplateMatchService + single CaptureRaw"
```

---

## Task 5: MainWindow 교체 + 빌드 최종 확인

**Files:**
- Modify: `src/EndfieldEssenceOverlay/MainWindow.xaml.cs`

**Step 1: MainWindow.xaml.cs — 필드 및 OnClosed 수정**

```csharp
// Before:
private readonly OcrService           _ocr     = new();

// After:
private readonly TemplateMatchService _templateMatcher = new();
```

`InitializeAsync()`:

```csharp
// Before:
_ocr.Initialize();
_scanner = new ScannerService(_capture, _ocr);

// After:
_templateMatcher.Initialize();
_scanner = new ScannerService(_capture, _templateMatcher);
```

`OnClosed()`:

```csharp
// Before:
_ocr.Dispose();

// After:
_templateMatcher.Dispose();
```

**Step 2: 전체 빌드 + 테스트 (Windows PowerShell)**

```powershell
dotnet build src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
dotnet test tests/EndfieldEssenceOverlay.Tests/EndfieldEssenceOverlay.Tests.csproj
```

Expected: Build succeeded, 모든 테스트 PASS

**Step 3: 폴더 배포 빌드**

```powershell
dotnet publish src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj `
  -r win-x64 -c Release --self-contained `
  -o publish
```

Expected: `publish/` 폴더에 exe + OpenCvSharp dll + `Data/templates/` 생성

**Step 4: 동작 확인 체크리스트**

- [ ] `publish/EndfieldEssenceOverlay.exe` 실행됨
- [ ] `Data/templates/` 폴더 존재 (비어있어도 크래시 없음)
- [ ] 템플릿 없을 때 앱이 정상 동작 (기질 감지 없이 idle 상태)
- [ ] 흐름.png 템플릿 추가 후 게임 화면에서 흐름 인식됨

**Step 5: Commit**

```bash
git add src/EndfieldEssenceOverlay/MainWindow.xaml.cs
git commit -m "feat: switch to TemplateMatchService, remove OcrService from main pipeline"
```

---

## 참고: 템플릿 이미지 준비 방법

1. 게임 실행 후 기질 패널이 보이는 상태에서 화면 캡처 (Windows Snipping Tool 등)
2. 키워드 텍스트만 타이트하게 크롭 — 배경 여백 5px 정도 포함 권장
3. `publish/Data/templates/흐름.png` 로 저장 (파일명 = 키워드 문자열 정확히)
4. 앱 재시작 → 자동 로드

템플릿 파일명 예시:
```
흐름.png
효율.png
힘 증가.png
의지 증가.png
공격력 증가.png
...
```

## 참고: 임계값 튜닝

- `Config.TemplateThreshold = 0.80` — 기본값
- 오탐 많으면 올리기 (0.85~0.90)
- 미탐 많으면 내리기 (0.70~0.75)
- DebugText ON 상태에서 감지된 키워드 목록으로 확인
