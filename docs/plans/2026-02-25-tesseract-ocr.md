# Tesseract OCR 전환 구현 계획


**Goal:** Windows.Media.Ocr(WinRT)를 Tesseract OCR로 교체해 게임 스타일 폰트(ㅎ 계열 음절)를 정확히 인식한다.

**Architecture:** OcrService.cs 내부만 교체 (인터페이스 동일 유지: `Initialize()` + `ExtractLinesAsync(Bitmap)`). tessdata/kor.traineddata를 앱 폴더에 함께 배포. 단일 exe 대신 폴더 배포로 전환(native dll 때문).

**Tech Stack:** C# 12 / .NET 8 / WPF, Tesseract NuGet 5.2.0, kor.traineddata (LSTM best)

> **빌드 환경 주의:** WSL에서 `dotnet` 실행 불가. 빌드/테스트는 Windows PowerShell에서 수행.

---

## Task 1: tessdata 파일 준비 + csproj 수정

**Files:**
- Modify: `src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj`
- Create: `src/EndfieldEssenceOverlay/tessdata/kor.traineddata` (수동 다운로드)

**Step 1: kor.traineddata 다운로드 (Windows에서 수행)**

```powershell
# PowerShell에서 실행
Invoke-WebRequest `
  -Uri "https://github.com/tesseract-ocr/tessdata_best/raw/main/kor.traineddata" `
  -OutFile "<project-root>\src\EndfieldEssenceOverlay\tessdata\kor.traineddata"
```

Expected: `tessdata/kor.traineddata` (~15MB) 생성됨

**Step 2: csproj에 Tesseract NuGet 추가 + tessdata 번들 설정**

현재 파일의 `<ItemGroup>` (FuzzySharp, System.Drawing.Common)에 추가:

```xml
<PackageReference Include="Tesseract" Version="5.2.0" />
```

기존 Content ItemGroup 아래에 tessdata 항목 추가:

```xml
<ItemGroup>
  <Content Include="tessdata\kor.traineddata">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

**Step 3: 빌드 확인 (Windows PowerShell)**

```powershell
dotnet restore src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
dotnet build src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
```

Expected: Build succeeded

**Step 4: Commit**

```bash
git add src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
git add src/EndfieldEssenceOverlay/tessdata/kor.traineddata
git commit -m "feat: add Tesseract NuGet + kor.traineddata"
```

---

## Task 2: OcrService.cs 교체

**Files:**
- Modify: `src/EndfieldEssenceOverlay/Services/OcrService.cs`

`Initialize()`와 `ExtractLinesAsync(Bitmap)` 시그니처는 그대로 유지. 내부 구현만 WinRT → Tesseract로 교체.

**Step 1: OcrService.cs 전체 교체**

```csharp
// src/EndfieldEssenceOverlay/Services/OcrService.cs
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Tesseract;

namespace EndfieldEssenceOverlay.Services;

public class OcrService : IDisposable
{
    private TesseractEngine? _engine;

    public void Initialize()
    {
        var tessDataPath = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath)
                ?? AppContext.BaseDirectory,
            "tessdata");

        if (!Directory.Exists(tessDataPath))
            throw new InvalidOperationException(
                $"tessdata 폴더를 찾을 수 없습니다: {tessDataPath}\n" +
                "앱 폴더에 tessdata/kor.traineddata 파일이 있는지 확인하세요.");

        _engine = new TesseractEngine(tessDataPath, "kor", EngineMode.LstmOnly);
    }

    public async Task<List<string>> ExtractLinesAsync(Bitmap bitmap)
    {
        if (_engine is null) throw new InvalidOperationException("Initialize() 먼저 호출하세요.");

        return await Task.Run(() =>
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, ImageFormat.Png);
            var pngBytes = ms.ToArray();

            using var pix  = Pix.LoadFromMemory(pngBytes);
            using var page = _engine.Process(pix, PageSegMode.SparseText);

            var text = page.GetText() ?? "";
            return text
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => StripSymbols(l.Trim()))
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();
        });
    }

    // 한글 음절(가-힣) + 자모(ㄱ-ㅣ)만 추출
    private static string StripSymbols(string text)
    {
        var matches = Regex.Matches(text, @"[\uAC00-\uD7A3\u3130-\u318F]+");
        return string.Join(" ", matches.Select(m => m.Value)).Trim();
    }

    public void Dispose() => _engine?.Dispose();
}
```

**Step 2: 빌드 확인**

```powershell
dotnet build src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj
```

Expected: Build succeeded (WinRT 관련 using 오류 없어야 함)

**Step 3: Commit**

```bash
git add src/EndfieldEssenceOverlay/Services/OcrService.cs
git commit -m "feat: replace Windows OCR with Tesseract LSTM"
```

---

## Task 3: 배포 방식 변경 (폴더 배포)

**Files:**
- 빌드 명령만 변경 (파일 수정 없음)

단일 exe는 네이티브 dll과 tessdata 때문에 불가. 폴더 배포로 전환.

**Step 1: 폴더 배포 빌드 (Windows PowerShell)**

```powershell
dotnet publish src/EndfieldEssenceOverlay/EndfieldEssenceOverlay.csproj `
  -r win-x64 -c Release --self-contained `
  -o publish
```

(`-p:PublishSingleFile=true` 제거)

Expected: `publish/` 폴더에 exe + dll + tessdata/ 생성됨

**Step 2: 폴더 구조 확인**

```powershell
ls publish/tessdata/
```

Expected: `kor.traineddata` 파일이 있어야 함

**Step 3: 동작 확인 체크리스트**

- [ ] `publish/EndfieldEssenceOverlay.exe` 실행됨
- [ ] 앱 시작 시 에러 없음 (tessdata 로드 성공)
- [ ] 게임 화면 스캔 시 "흐름" 인식됨
- [ ] 게임 화면 스캔 시 "효율" 인식됨
- [ ] 기존에 인식되던 다른 기질도 여전히 인식됨

**Step 4: Commit**

```bash
git add .
git commit -m "feat: switch to folder publish for Tesseract native dll support"
```

---

## 참고: 문제 발생 시

**tessdata 경로 오류:**
- `publish/tessdata/kor.traineddata` 파일 존재 여부 확인
- `EngineMode.LstmOnly` → `EngineMode.Default` 로 변경 시도

**인식률이 기대보다 낮을 경우:**
- `PageSegMode.SparseText` → `PageSegMode.Auto` 변경 시도
- ScreenCaptureService에서 업스케일 배율을 3으로 올려볼 것 (`Config.UpscaleFactor = 3`)
