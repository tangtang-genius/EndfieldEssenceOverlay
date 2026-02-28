// src/EndfieldEssenceOverlay/Services/ScannerService.cs
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using OpenCvSharp;

namespace EndfieldEssenceOverlay.Services;

public class ScannerService : IDisposable
{
    private readonly ScreenCaptureService _capture;
    private readonly TemplateMatchService _templateMatcher;
    private CancellationTokenSource?      _cts;
    private byte[]?                       _previousFrame;
    private DateTime                      _lastScanTime = DateTime.MinValue;

    /// <summary>키워드 3개가 성공적으로 감지될 때 발생 (UI 스레드에서 호출됨)</summary>
    public event Action<List<string>>? KeywordsDetected;

    /// <summary>디버그 모드 전용: 감지된 키워드 + top3 후보 (UI 스레드에서 호출됨)</summary>
    public event Action<List<string>, List<MatchCandidate>>? DebugKeywords;

    /// <summary>디버그 모드 전용: 캡처 이미지 PNG 바이트 (UI 스레드에서 호출됨)</summary>
    public event Action<byte[]>? DebugCaptureImage;

    public ScannerService(ScreenCaptureService capture, TemplateMatchService templateMatcher)
    {
        _capture         = capture;
        _templateMatcher = templateMatcher;
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
        // 1. 캡처
        using var rawBmp = _capture.CaptureRaw();
        if (rawBmp == null) return;

        // 2. 그레이스케일 Mat 한 번만 생성 (변화감지 + 매칭 공용)
        using var grayMat = TemplateMatchService.BitmapToGrayMat(rawBmp);
        if (grayMat.Empty()) return;

        byte[] currentFrame = MatToBytes(grayMat);

        // 3. 이전 프레임과 비교 (변화 없어도 2초마다 강제 스캔)
        bool forceScan = (DateTime.UtcNow - _lastScanTime).TotalMilliseconds > 2000;
        if (_previousFrame is not null &&
            !HasSignificantChange(currentFrame, _previousFrame) &&
            !forceScan)
            return;

        _previousFrame = currentFrame;
        _lastScanTime  = DateTime.UtcNow;

        if (Config.ShowDebugImage)
        {
            var pngBytes = BitmapToPng(rawBmp);
            Application.Current.Dispatcher.Invoke(
                () => DebugCaptureImage?.Invoke(pngBytes));
        }

        var lines = await _templateMatcher.ExtractLinesAsync(grayMat);

        if (Config.ShowDebugText)
        {
            var top3 = _templateMatcher.LastCandidates.Take(3).ToList();
            Application.Current.Dispatcher.Invoke(
                () => DebugKeywords?.Invoke(lines, top3));
        }

        Application.Current.Dispatcher.Invoke(
            () => KeywordsDetected?.Invoke(lines));
    }

    // ── 유틸 ────────────────────────────────────────────────────

    private static byte[] MatToBytes(Mat grayMat)
    {
        var bytes = new byte[grayMat.Rows * grayMat.Cols];
        Marshal.Copy(grayMat.Data, bytes, 0, bytes.Length);
        return bytes;
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

    private static byte[] BitmapToPng(Bitmap bmp)
    {
        using var ms = new System.IO.MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    public void Dispose() => Stop();
}
