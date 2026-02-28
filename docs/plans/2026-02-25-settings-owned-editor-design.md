# 설정창 + 소유 목록 편집기 + UI 개선 설계

**날짜:** 2026-02-25
**상태:** 확정

---

## 개요

오버레이 헤더를 단순화하고, 탭 구조의 설정창을 추가한다.
기질 칩(chip) UI와 idle 로직 개선, 소유 목록 인앱 편집기를 구현한다.

---

## 1. 헤더 변경

| 현재 | 변경 후 |
|---|---|
| 👁 디버그 · 📐 캡처 · ✕ 닫기 | ⚙ 설정 · ✕ 닫기 |

- F10 클릭 투과 기능 **제거** (HotkeyService 등록 제거, Win32 SetWindowLong 코드 제거)
- 👁 디버그 버튼 **제거** → 설정창으로 이동
- 📐 캡처 버튼 **제거** → 설정창으로 이동

---

## 2. SettingsWindow (탭 구조)

`WindowStyle="ToolWindow"`, `Topmost="True"`, `ResizeMode="NoResize"`, Width=320
MainWindow에서 `_settingsWindow` 인스턴스를 보관하고, 이미 열려있으면 Focus().

### 2-1. [설정] 탭

```
── 디버그 ──────────────────────
  ☐ OCR 텍스트 패널 표시
  ☐ OCR 입력 이미지 표시

── 스캔 ────────────────────────
  캡처 주기   [500] ms   (100~5000, LostFocus 적용)

── 화면 ────────────────────────
  오버레이 불투명도  ──●────  80%
                      30~100%, 즉시 반영

── 캡처 영역 ───────────────────
  [📐 캡처 범위 재설정]
```

- 체크박스 변경 → 콜백으로 MainWindow의 `ApplyDebugText(bool)` / `ApplyDebugImage(bool)` 즉시 호출
- 불투명도 슬라이더 → MainWindow.Background의 alpha 실시간 업데이트 + `Config.OverlayOpacity` 저장
- 캡처 범위 버튼 → 콜백으로 MainWindow의 `RunCalibration()` 호출

### 2-2. [소유 목록] 탭

```
ScrollViewer
└── 힘 증가
│     ☑ ★6 천둥의 흔적   힘 증가 · 생명력 증가 · 의료
│     ☐ ★6 헤라펜거      힘 증가 · 공격력 증가 · 방출
│     ...
└── 의지 증가
      ...

[저장]  [취소]  ← 하단 고정
```

- 열릴 때: `valid_traits.txt` 전체 무기 로드 + `owned_traits.txt` 기준 초기 체크 상태 설정
- 저장: `owned_traits.txt` 재작성 + `TraitMatcherService.RebuildOwned(IList<string> weaponNames)` 호출
- 취소: 체크 상태 초기값으로 복원
- 기존 오버레이 [소유] 버튼은 유지 (같은 파일 경유, 일관성 보장)

### 2-3. [도움말] 탭

```
ScrollViewer (읽기 전용 텍스트)

📌 기본 사용법
   앱 실행 시 게임 화면을 자동으로 스캔합니다.
   기질 조합이 감지되면 유효/소유/비유효 여부를 표시합니다.

📌 캡처 범위 설정
   설정 → [캡처 범위 재설정] 클릭 후
   기질 3개가 표시되는 패널 영역을 드래그로 선택하세요.
   처음 실행 시 자동으로 캘리브레이션 창이 열립니다.

📌 소유 기질 관리
   설정 → 소유 목록 탭에서 보유 무기를 체크하고 저장합니다.
   또는 유효 기질 감지 시 오버레이의 [소유] 버튼으로 바로 등록할 수 있습니다.
```

---

## 3. 기질 칩(Chip) UI

### 현재
```
주요 능력치 증가  ·  물리 피해 증가  ·  기예
(TextBlock, 점 구분)
```

### 변경 후
```
[주요 능력치 증가]  [물리 피해 증가]  [기예]
  파랑(기질1)         초록(기질2)     주황(기질3)
```

- `TraitsText` TextBlock → `TraitsPanel` (WrapPanel)으로 교체
- 코드에서 동적으로 `Border` + `TextBlock` 생성
- CornerRadius=3, Padding=4,2, Margin=0,0,4,0

**색상 (Background / BorderBrush):**

| 위치 | Background | BorderBrush | 용도 |
|---|---|---|---|
| 기질1 | `#225588CC` | `#5599CC` | 주요 능력치 |
| 기질2 | `#227AB855` | `#77BB55` | 보조 통계 |
| 기질3 | `#22CC7733` | `#FF9944` | 스킬 스텟 |

### 칩 색상이 맞으려면

`TraitMatcherService` 내부 저장 타입을 `HashSet<string>` → **`List<string>`** 으로 변경
(파일 순서 보존 → 인덱스 0=기질1, 1=기질2, 2=기질3)
`SetMatch`는 `List.Contains` + `StringComparer.OrdinalIgnoreCase` 로 대체.

---

## 4. Invalid → Idle 로직 변경

```csharp
case MatchStatus.Invalid:
    if (result.SnappedTraits.Count == 0)
        SetStatus("idle", "실시간 스캔 중");   // 엉뚱한 단어만 보임
    else
        SetStatus("invalid", "비유효 기질", chips);  // 기질 키워드 감지됨
    break;
```

---

## 5. 폰트 크기 조정

| 요소 | 현재 | 변경 |
|---|---|---|
| StatusText | 18 | 20 |
| TraitsText (칩) | 13 | 15 |
| DetailText | 14 | 14 (유지) |

---

## 6. Config 변경

| 항목 | 현재 | 변경 |
|---|---|---|
| `DebugMode` | `static bool` | **제거** |
| `ShowDebugText` | 없음 | `static bool` 추가 |
| `ShowDebugImage` | 없음 | `static bool` 추가 |
| `PollIntervalMs` | `const int` | `static int` (property) |
| `OverlayOpacity` | 없음 | `static double = 0.8` 추가 |
| `VK_TOGGLE`, `MOD_NONE` | 있음 | **제거** (F10 기능 삭제) |

---

## 7. 영향받는 파일 목록

| 파일 | 변경 내용 |
|---|---|
| `Config.cs` | DebugMode→ShowDebugText+ShowDebugImage, PollIntervalMs const→property, OverlayOpacity 추가, VK_TOGGLE 제거 |
| `MainWindow.xaml` | 헤더 버튼 정리, TraitsText→TraitsPanel(WrapPanel) |
| `MainWindow.xaml.cs` | SettingsButton_Click, ApplyDebugText/Image, 칩 생성 로직, F10/HotkeyService 제거 |
| `ScannerService.cs` | Config.DebugMode → Config.ShowDebugText/ShowDebugImage |
| `TraitMatcherService.cs` | HashSet→List (순서 보존), RebuildOwned() 메서드 추가 |
| **신규** `SettingsWindow.xaml` | 탭 구조 설정창 |
| **신규** `SettingsWindow.xaml.cs` | 콜백 기반 설정 적용 |
| `HotkeyService.cs` | 사용 안 함 (삭제 또는 미등록) |
