// src/EndfieldEssenceOverlay/Services/TemplateMatchService.cs
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using OpenCvSharp;

namespace EndfieldEssenceOverlay.Services;

public record MatchCandidate(string Keyword, double Score);

public class TemplateMatchService : IDisposable
{
    private readonly string _templatesDir;
    private readonly List<(string Keyword, Mat Template)> _templates = [];

    /// <summary>직전 스캔의 전체 후보 (점수 내림차순, NMS 전)</summary>
    public List<MatchCandidate> LastCandidates { get; private set; } = [];

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

        foreach (var file in Directory.GetFiles(_templatesDir, "*.png", SearchOption.AllDirectories))
        {
            var keyword = Path.GetFileNameWithoutExtension(file).Replace('_', ' ');
            var mat = Cv2.ImRead(file, ImreadModes.Grayscale);
            if (!mat.Empty())
                _templates.Add((keyword, mat));
        }
    }

    /// <summary>
    /// 캡처된 Bitmap에서 등록된 템플릿 키워드를 찾아 반환.
    /// 멀티스케일 NCC + 위치 기반 중복 제거(NMS)로 오탐 억제.
    /// </summary>
    public async Task<List<string>> ExtractLinesAsync(Bitmap bitmap)
    {
        using var grayMat = BitmapToGrayMat(bitmap);
        return await ExtractLinesAsync(grayMat);
    }

    /// <summary>이미 변환된 그레이스케일 Mat으로 매칭 (이중 변환 방지).</summary>
    public async Task<List<string>> ExtractLinesAsync(Mat source)
    {
        if (_templates.Count == 0 || source.Empty()) { LastCandidates = []; return []; }

        return await Task.Run(() =>
        {

            // 1. 모든 템플릿에 대해 병렬 매칭
            var bag = new ConcurrentBag<DetectedMatch>();
            Parallel.ForEach(_templates, (item) =>
            {
                var match = BestMatchAtAnyScale(source, item.Keyword, item.Template);
                if (match != null)
                    bag.Add(match);
            });
            var candidates = bag.ToList();

            // 점수 내림차순 정렬
            candidates.Sort((a, b) => b.Score.CompareTo(a.Score));

            // 디버그용: 전체 후보 저장
            LastCandidates = candidates
                .Select(c => new MatchCandidate(c.Keyword, c.Score))
                .ToList();

            // 2. NMS — 겹치는 영역은 높은 점수만 유지
            var kept = new List<DetectedMatch>();
            foreach (var candidate in candidates)
            {
                if (!kept.Any(k => Overlaps(k, candidate, 0.3)))
                    kept.Add(candidate);
            }

            return kept.Select(m => m.Keyword).ToList();
        });
    }

    // ── Bitmap → Mat 직접 변환 (PNG 인코딩/디코딩 제거) ────────

    public static Mat BitmapToGrayMat(Bitmap bitmap)
    {
        var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            using var bgra = new Mat(bitmap.Height, bitmap.Width, MatType.CV_8UC4, data.Scan0, data.Stride);
            var gray = new Mat();
            Cv2.CvtColor(bgra, gray, ColorConversionCodes.BGRA2GRAY);
            return gray;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    // ── 매칭 결과 ───────────────────────────────────────────────

    private record DetectedMatch(string Keyword, double Score, int X, int Y, int W, int H);

    private static DetectedMatch? BestMatchAtAnyScale(Mat source, string keyword, Mat template)
    {
        double[] scales = [0.40, 0.45, 0.50, 0.55, 0.60, 0.65, 0.70, 0.75, 0.80, 0.85, 0.90, 0.95, 1.00, 1.10, 1.20, 1.40, 1.60, 1.80, 2.00];
        DetectedMatch? best = null;

        foreach (double scale in scales)
        {
            int tw = Math.Max(1, (int)(template.Width  * scale));
            int th = Math.Max(1, (int)(template.Height * scale));
            if (tw > source.Width || th > source.Height) continue;

            using var scaled = template.Resize(new OpenCvSharp.Size(tw, th));
            using var result = new Mat();
            Cv2.MatchTemplate(source, scaled, result, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(result, out _, out double maxVal, out _, out OpenCvSharp.Point maxLoc);

            if (maxVal >= Config.TemplateThreshold &&
                (best == null || maxVal > best.Score))
            {
                best = new DetectedMatch(keyword, maxVal, maxLoc.X, maxLoc.Y, tw, th);
                if (maxVal >= 0.95) break; // 고신뢰 → 추가 스케일 불필요
            }
        }
        return best;
    }

    private static bool Overlaps(DetectedMatch a, DetectedMatch b, double threshold)
    {
        int x1 = Math.Max(a.X, b.X);
        int y1 = Math.Max(a.Y, b.Y);
        int x2 = Math.Min(a.X + a.W, b.X + b.W);
        int y2 = Math.Min(a.Y + a.H, b.Y + b.H);
        if (x1 >= x2 || y1 >= y2) return false;

        int intersection = (x2 - x1) * (y2 - y1);
        int smaller = Math.Min(a.W * a.H, b.W * b.H);
        return (double)intersection / smaller >= threshold;
    }

    public void Dispose()
    {
        foreach (var (_, mat) in _templates)
            mat.Dispose();
        _templates.Clear();
    }
}
