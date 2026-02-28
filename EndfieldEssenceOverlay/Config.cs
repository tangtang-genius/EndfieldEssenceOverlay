// src/EndfieldEssenceOverlay/Config.cs
using System.IO;

namespace EndfieldEssenceOverlay;

public static class Config
{
    // 캡처 영역 (픽셀 좌표) — calibration.json 으로 동적 설정됨
    public static int CaptureLeft   { get; set; } = 50;
    public static int CaptureTop    { get; set; } = 200;
    public static int CaptureWidth  { get; set; } = 400;
    public static int CaptureHeight { get; set; } = 300;

    // OCR 이미지 업스케일 배율 (정확도 향상, 1 = 비활성)
    // NearestNeighbor 보간 사용 → 게임 UI 픽셀 폰트 경계 선명하게 유지
    public const int UpscaleFactor = 3;

    // 실시간 스캔 설정
    public static int   PollIntervalMs  { get; set; } = 100;   // 폴링 주기 (ms)
    public const double ChangeThreshold = 10.0;  // 픽셀 평균 차이 임계값 (0~255)

    // 이진화 임계값: 이 값 이상인 픽셀(밝은 글씨) → 검정, 미만(어두운 배경) → 흰색
    // 너무 높으면 안티앨리어싱 획이 잘려 ㅌ→ㄴ, ㅎ 탈락 등 오인식 발생
    // 회색 배경(~128)과 흰 글씨(~220+) 사이, 안티앨리어싱 경계(~150~179)를 살리도록 낮게 설정
    // (BinarizeThreshold 제거 — OTSU 자동 이진화로 대체됨)

    // 어휘 스냅 임계값: OCR 라인이 이 점수 이상일 때만 가장 가까운 키워드로 스냅 (0~100)
    // 너무 낮으면 "설정", "다음" 같은 UI 텍스트도 오탐, 너무 높으면 오인식 보정 안됨
    public const int SnapThreshold = 50;

    // 비유효 기질 표시용 임계값: SnappedEssences 화면 표시 시 이 점수 이상인 것만 노출
    // "다음", "이전" 같은 UI 텍스트가 낮은 점수로 스냅되어 표시되는 것을 방지
    public const int SnapDisplayThreshold = 60;

    // 템플릿 매칭 임계값: CCoeffNormed 점수 (0.0~1.0), 이 값 이상이면 키워드 감지로 판정
    public const double TemplateThreshold = 0.70;

    // 디버그: 각 기능 독립 토글
    public static bool ShowDebugText  { get; set; } = false;
    public static bool ShowDebugImage { get; set; } = false;

    // 오버레이 불투명도 (0.3 ~ 1.0)
    public static double OverlayOpacity { get; set; } = 0.8;

    // 오버레이 창 초기 위치 & 크기
    public const int OverlayLeft   = 10;
    public const int OverlayTop    = 10;
    public const int OverlayWidth  = 460;
    public const int OverlayHeight = 130;

    // 버전 정보
    public const string AppVersion  = "1.0.0";
    public const string GameVersion = "1.0";   // 대응 엔드필드 버전

    // 게임 창 타이틀 (PrintWindow 캡처용)
    public static string GameWindowTitle { get; set; } = "Endfield";

    // 데이터 파일 경로
    private static readonly string _baseDir =
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

    public static string WeaponsJsonPath =>
        Path.Combine(_baseDir, "Data", "weapons.json");

    public static string OwnedJsonPath =>
        Path.Combine(_baseDir, "Data", "owned.json");

    public static string CalibrationPath =>
        Path.Combine(_baseDir, "Data", "calibration.json");
}
