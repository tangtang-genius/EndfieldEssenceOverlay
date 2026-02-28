# Arknights Endfield Trait Overlay Tool Implementation Plan


**Goal:** 게임 화면을 실시간 스캔하여 기질 키워드를 OCR로 읽고 유효/소유 여부를 오버레이로 표시하는 Windows 데스크탑 툴 제작.

**Architecture:** 단축키(F9) 입력 시 mss로 화면 캡처 → PaddleOCR로 한국어 텍스트 추출 → rapidfuzz로 유효 기질 목록 대조 → tkinter 오버레이에 결과 표시. 앱 시작 시 PaddleOCR를 미리 초기화하여 응답 지연 최소화.

**Tech Stack:** Python 3.10+, mss, PaddleOCR, opencv-python, rapidfuzz, keyboard, tkinter (stdlib)

**Runtime:** Windows Python (WSL2에서 개발, Windows Python에서 실행). 테스트 중 matcher/config 단위 테스트는 WSL2에서 가능하나, 실제 캡처·OCR·오버레이는 Windows 환경 필요.

---

## 사전 준비

### 의존성 설치 (Windows Python 환경에서 실행)

```bash
pip install mss paddleocr paddlepaddle opencv-python rapidfuzz keyboard
```

> PaddleOCR는 한국어 모델(`lang='korean'`)을 사용. 최초 실행 시 모델 파일 자동 다운로드 (~수백 MB).

---

## Task 1: 프로젝트 골격 & 설정 파일

**Files:**
- Create: `overlay_tool/config.py`
- Create: `overlay_tool/valid_traits.txt`
- Create: `overlay_tool/owned_traits.txt`

**Step 1: config.py 작성**

```python
# overlay_tool/config.py

# 단축키 설정
HOTKEY_SCAN = 'f9'          # 스캔 실행
HOTKEY_TOGGLE_PASSTHROUGH = 'f10'  # 클릭 투과 ON/OFF

# 캡처 영역 (픽셀 좌표, 게임 해상도에 맞게 조정 필요)
# 기질 패널 좌측 영역: {"top": y, "left": x, "width": w, "height": h}
CAPTURE_REGION = {
    "top": 200,
    "left": 50,
    "width": 400,
    "height": 300,
}

# 이미지 전처리
UPSCALE_FACTOR = 2  # OCR 정확도 향상을 위한 업스케일 배율

# 퍼지 매칭 임계값 (0~100, 높을수록 엄격)
FUZZY_THRESHOLD = 85

# 오버레이 창 위치 및 크기
OVERLAY_X = 10
OVERLAY_Y = 10
OVERLAY_WIDTH = 450
OVERLAY_HEIGHT = 150

# 데이터 파일 경로 (config.py 기준 상대 경로)
import os
_BASE = os.path.dirname(os.path.abspath(__file__))
VALID_TRAITS_PATH = os.path.join(_BASE, 'valid_traits.txt')
OWNED_TRAITS_PATH = os.path.join(_BASE, 'owned_traits.txt')
```

**Step 2: valid_traits.txt 예시 데이터 작성**

```
# 유효 기질 목록
# 형식: 키워드1,키워드2,키워드3  (순서 무관)
# # 으로 시작하는 줄은 주석
민첩 증가,치명타 확률 증가,고통
공격 강화,화염,치유
방어 관통,독,집중
속도 증가,냉기,재생
```

**Step 3: owned_traits.txt 초기 파일 작성**

```
# 소유 중인 기질 목록 (앱이 자동 관리)
# [소유 중] 버튼 클릭 시 자동으로 추가됨
```

**Step 4: 파일 존재 확인**

```bash
ls overlay_tool/
```

Expected: `config.py  valid_traits.txt  owned_traits.txt`

**Step 5: Commit**

```bash
git add overlay_tool/config.py overlay_tool/valid_traits.txt overlay_tool/owned_traits.txt
git commit -m "feat: add project config and data files"
```

---

## Task 2: matcher.py (핵심 비즈니스 로직 - TDD)

**Files:**
- Create: `overlay_tool/matcher.py`
- Create: `tests/test_matcher.py`

이 모듈은 순수 Python 로직이므로 WSL2/Windows 모두에서 단위 테스트 가능.

**Step 1: 실패하는 테스트 작성**

```python
# tests/test_matcher.py
import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))

import tempfile
import pytest
from overlay_tool.matcher import TraitMatcher, MatchResult


def make_matcher(valid_lines, owned_lines):
    """임시 파일로 TraitMatcher 생성 헬퍼"""
    with tempfile.NamedTemporaryFile(mode='w', suffix='.txt',
                                     delete=False, encoding='utf-8') as vf:
        vf.write('\n'.join(valid_lines))
        valid_path = vf.name
    with tempfile.NamedTemporaryFile(mode='w', suffix='.txt',
                                     delete=False, encoding='utf-8') as of:
        of.write('\n'.join(owned_lines))
        owned_path = of.name
    return TraitMatcher(valid_path, owned_path), valid_path, owned_path


class TestMatchResult:
    def test_invalid(self):
        r = MatchResult(status='invalid')
        assert r.status == 'invalid'
        assert r.matched_name is None

    def test_valid_unowned(self):
        r = MatchResult(status='valid_unowned', matched_name='민첩 증가,치명타 확률 증가,고통')
        assert r.status == 'valid_unowned'
        assert r.matched_name == '민첩 증가,치명타 확률 증가,고통'

    def test_valid_owned(self):
        r = MatchResult(status='valid_owned', matched_name='민첩 증가,치명타 확률 증가,고통')
        assert r.status == 'valid_owned'


class TestTraitMatcher:
    def test_exact_match_valid_unowned(self):
        matcher, _, _ = make_matcher(
            ['민첩 증가,치명타 확률 증가,고통'],
            ['# empty']
        )
        result = matcher.match(['민첩 증가', '치명타 확률 증가', '고통'])
        assert result.status == 'valid_unowned'

    def test_exact_match_valid_owned(self):
        matcher, _, _ = make_matcher(
            ['민첩 증가,치명타 확률 증가,고통'],
            ['민첩 증가,치명타 확률 증가,고통']
        )
        result = matcher.match(['민첩 증가', '치명타 확률 증가', '고통'])
        assert result.status == 'valid_owned'

    def test_order_independent(self):
        matcher, _, _ = make_matcher(
            ['민첩 증가,치명타 확률 증가,고통'],
            []
        )
        result = matcher.match(['고통', '민첩 증가', '치명타 확률 증가'])
        assert result.status == 'valid_unowned'

    def test_no_match_returns_invalid(self):
        matcher, _, _ = make_matcher(
            ['민첩 증가,치명타 확률 증가,고통'],
            []
        )
        result = matcher.match(['전혀', '다른', '키워드'])
        assert result.status == 'invalid'

    def test_fuzzy_match_typo(self):
        """OCR 오인식 시뮬레이션: '치명타 확률 증가' -> '치명타 확율 증가'"""
        matcher, _, _ = make_matcher(
            ['민첩 증가,치명타 확률 증가,고통'],
            []
        )
        result = matcher.match(['민첩 증가', '치명타 확율 증가', '고통'])
        assert result.status == 'valid_unowned'

    def test_mark_owned_appends_to_file(self):
        matcher, _, owned_path = make_matcher(
            ['민첩 증가,치명타 확률 증가,고통'],
            []
        )
        keywords = ['민첩 증가', '치명타 확률 증가', '고통']
        matcher.mark_owned(keywords)

        # 파일에 저장됐는지 확인
        with open(owned_path, encoding='utf-8') as f:
            content = f.read()
        assert '민첩 증가' in content

        # 메모리도 갱신됐는지 확인
        result = matcher.match(keywords)
        assert result.status == 'valid_owned'

    def test_comments_and_blank_lines_ignored(self):
        matcher, _, _ = make_matcher(
            ['# 주석', '', '민첩 증가,치명타 확률 증가,고통', ''],
            []
        )
        result = matcher.match(['민첩 증가', '치명타 확률 증가', '고통'])
        assert result.status == 'valid_unowned'
```

**Step 2: 테스트 실행 (실패 확인)**

```bash
cd <project-root>
python -m pytest tests/test_matcher.py -v 2>&1 | head -30
```

Expected: `ImportError` 또는 `ModuleNotFoundError` (matcher.py 없음)

**Step 3: matcher.py 구현**

```python
# overlay_tool/matcher.py
from dataclasses import dataclass, field
from typing import Optional
from rapidfuzz import fuzz
from overlay_tool.config import FUZZY_THRESHOLD


@dataclass
class MatchResult:
    status: str  # 'invalid' | 'valid_unowned' | 'valid_owned'
    matched_name: Optional[str] = None


def _load_trait_file(path: str) -> list[frozenset]:
    """텍스트 파일에서 기질 목록 로드. 주석/빈 줄 무시."""
    traits = []
    try:
        with open(path, encoding='utf-8') as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith('#'):
                    continue
                keywords = [k.strip() for k in line.split(',') if k.strip()]
                if keywords:
                    traits.append(frozenset(keywords))
    except FileNotFoundError:
        pass
    return traits


def _fuzzy_keyword_match(keyword: str, candidates: frozenset, threshold: int) -> bool:
    """키워드가 candidates 중 하나와 임계값 이상 유사한지 확인."""
    for candidate in candidates:
        if fuzz.ratio(keyword, candidate) >= threshold:
            return True
    return False


def _fuzzy_set_match(scanned: list[str], target: frozenset, threshold: int) -> bool:
    """스캔된 키워드 리스트가 target frozenset과 퍼지 매칭되는지 확인."""
    if len(scanned) != len(target):
        return False
    matched_targets = set()
    for keyword in scanned:
        found = False
        for candidate in target:
            if candidate not in matched_targets and fuzz.ratio(keyword, candidate) >= threshold:
                matched_targets.add(candidate)
                found = True
                break
        if not found:
            return False
    return len(matched_targets) == len(target)


class TraitMatcher:
    def __init__(self, valid_path: str, owned_path: str):
        self.valid_path = valid_path
        self.owned_path = owned_path
        self._valid: list[frozenset] = _load_trait_file(valid_path)
        self._owned: list[frozenset] = _load_trait_file(owned_path)

    def match(self, keywords: list[str]) -> MatchResult:
        """스캔된 키워드 3개로 유효/소유 여부 판별."""
        # 1. owned 목록에서 먼저 확인
        for owned_set in self._owned:
            if _fuzzy_set_match(keywords, owned_set, FUZZY_THRESHOLD):
                name = ','.join(sorted(owned_set))
                return MatchResult(status='valid_owned', matched_name=name)

        # 2. valid 목록 확인
        for valid_set in self._valid:
            if _fuzzy_set_match(keywords, valid_set, FUZZY_THRESHOLD):
                name = ','.join(sorted(valid_set))
                return MatchResult(status='valid_unowned', matched_name=name)

        return MatchResult(status='invalid')

    def mark_owned(self, keywords: list[str]) -> None:
        """키워드 조합을 소유 목록에 추가 (파일 + 메모리)."""
        line = ','.join(keywords)
        with open(self.owned_path, 'a', encoding='utf-8') as f:
            f.write(line + '\n')
        self._owned.append(frozenset(keywords))
```

**Step 4: 테스트 실행 (통과 확인)**

```bash
cd <project-root>
pip install rapidfuzz  # WSL2 테스트용
python -m pytest tests/test_matcher.py -v
```

Expected: 모든 테스트 PASS

**Step 5: Commit**

```bash
git add overlay_tool/matcher.py tests/test_matcher.py
git commit -m "feat: add TraitMatcher with fuzzy matching and owned tracking"
```

---

## Task 3: capture.py (화면 캡처 + 이미지 전처리)

**Files:**
- Create: `overlay_tool/capture.py`

> 이 모듈은 실제 Windows 환경에서만 완전히 테스트 가능. 단위 테스트는 numpy 배열 변환 로직만 검증.

**Step 1: capture.py 구현**

```python
# overlay_tool/capture.py
import numpy as np
import cv2
import mss
from overlay_tool.config import CAPTURE_REGION, UPSCALE_FACTOR


def capture_and_preprocess() -> np.ndarray:
    """
    지정 영역 캡처 후 OCR용 전처리.
    Returns: 그레이스케일 + 업스케일된 numpy 배열 (H, W)
    """
    with mss.mss() as sct:
        screenshot = sct.grab(CAPTURE_REGION)
    img = np.array(screenshot)          # BGRA
    img = cv2.cvtColor(img, cv2.COLOR_BGRA2GRAY)  # 그레이스케일

    if UPSCALE_FACTOR != 1:
        h, w = img.shape
        img = cv2.resize(
            img,
            (w * UPSCALE_FACTOR, h * UPSCALE_FACTOR),
            interpolation=cv2.INTER_CUBIC
        )
    return img


def preprocess_image(img_bgra: np.ndarray) -> np.ndarray:
    """
    외부에서 받은 BGRA numpy 배열 전처리 (테스트용).
    Returns: 그레이스케일 + 업스케일된 배열
    """
    gray = cv2.cvtColor(img_bgra, cv2.COLOR_BGRA2GRAY)
    if UPSCALE_FACTOR != 1:
        h, w = gray.shape
        gray = cv2.resize(
            gray,
            (w * UPSCALE_FACTOR, h * UPSCALE_FACTOR),
            interpolation=cv2.INTER_CUBIC
        )
    return gray
```

**Step 2: 전처리 로직 단위 테스트**

```python
# tests/test_capture.py
import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))

import numpy as np
import pytest


def test_preprocess_grayscale_and_upscale():
    """전처리 함수: BGRA -> 그레이스케일 + 2x 업스케일"""
    from overlay_tool.capture import preprocess_image
    # 100x200 BGRA 더미 이미지
    dummy = np.zeros((100, 200, 4), dtype=np.uint8)
    result = preprocess_image(dummy)
    assert result.ndim == 2                # 그레이스케일
    assert result.shape == (200, 400)      # 2x 업스케일 (UPSCALE_FACTOR=2 기본값)
```

**Step 3: 테스트 실행**

```bash
pip install opencv-python  # WSL2 테스트용
python -m pytest tests/test_capture.py -v
```

Expected: PASS

**Step 4: Commit**

```bash
git add overlay_tool/capture.py tests/test_capture.py
git commit -m "feat: add screen capture and image preprocessing"
```

---

## Task 4: ocr.py (PaddleOCR 텍스트 추출 + 키워드 파싱)

**Files:**
- Create: `overlay_tool/ocr.py`
- Create: `tests/test_ocr_parsing.py`

> PaddleOCR 초기화는 무거우므로 싱글턴 패턴 사용. 파싱 로직만 단위 테스트.

**Step 1: 파싱 로직 테스트 작성**

```python
# tests/test_ocr_parsing.py
import sys, os
sys.path.insert(0, os.path.join(os.path.dirname(__file__), '..'))

from overlay_tool.ocr import parse_keywords


class TestParseKeywords:
    def test_extracts_three_keywords(self):
        """OCR 결과에서 키워드 3개 추출"""
        # PaddleOCR 결과 형식: [[[bbox, (text, confidence)], ...], ...]
        # 편의상 텍스트 목록만 전달하는 내부 파싱 함수 테스트
        ocr_texts = ['민첩 증가', '치명타 확률 증가', '고통']
        result = parse_keywords(ocr_texts)
        assert result == ['민첩 증가', '치명타 확률 증가', '고통']

    def test_strips_whitespace(self):
        ocr_texts = ['  민첩 증가  ', '치명타 확률 증가', '고통  ']
        result = parse_keywords(ocr_texts)
        assert result == ['민첩 증가', '치명타 확률 증가', '고통']

    def test_filters_empty_strings(self):
        ocr_texts = ['민첩 증가', '', '치명타 확률 증가', '  ', '고통']
        result = parse_keywords(ocr_texts)
        assert result == ['민첩 증가', '치명타 확률 증가', '고통']

    def test_returns_up_to_three(self):
        """여러 줄 OCR 결과 중 상위 3개만"""
        ocr_texts = ['민첩 증가', '치명타 확률 증가', '고통', '추가 텍스트', '더 많은 텍스트']
        result = parse_keywords(ocr_texts)
        assert len(result) == 3

    def test_returns_empty_if_insufficient(self):
        """3개 미만이면 빈 리스트"""
        ocr_texts = ['민첩 증가', '치명타 확률 증가']
        result = parse_keywords(ocr_texts)
        assert result == []
```

**Step 2: 테스트 실행 (실패 확인)**

```bash
python -m pytest tests/test_ocr_parsing.py -v
```

Expected: ImportError

**Step 3: ocr.py 구현**

```python
# overlay_tool/ocr.py
from __future__ import annotations
from typing import TYPE_CHECKING
import numpy as np

if TYPE_CHECKING:
    from paddleocr import PaddleOCR as _PaddleOCR

_ocr_instance: '_PaddleOCR | None' = None


def get_ocr():
    """PaddleOCR 싱글턴 (최초 호출 시 초기화)."""
    global _ocr_instance
    if _ocr_instance is None:
        from paddleocr import PaddleOCR
        _ocr_instance = PaddleOCR(use_angle_cls=True, lang='korean', show_log=False)
    return _ocr_instance


def extract_text(image: np.ndarray) -> list[str]:
    """
    전처리된 그레이스케일 이미지에서 텍스트 추출.
    Returns: OCR로 인식된 텍스트 목록 (신뢰도 순서대로)
    """
    ocr = get_ocr()
    result = ocr.ocr(image, cls=True)
    texts = []
    if result and result[0]:
        for line in result[0]:
            text = line[1][0]   # (text, confidence) 튜플에서 텍스트만
            texts.append(text)
    return texts


def parse_keywords(texts: list[str]) -> list[str]:
    """
    텍스트 목록에서 빈 문자열 제거 후 상위 3개 반환.
    3개 미만이면 빈 리스트 반환.
    """
    cleaned = [t.strip() for t in texts if t.strip()]
    if len(cleaned) < 3:
        return []
    return cleaned[:3]


def scan_keywords(image: np.ndarray) -> list[str]:
    """extract_text + parse_keywords 합성 함수."""
    texts = extract_text(image)
    return parse_keywords(texts)
```

**Step 4: 파싱 테스트 실행 (통과 확인)**

```bash
python -m pytest tests/test_ocr_parsing.py -v
```

Expected: 모든 테스트 PASS

**Step 5: Commit**

```bash
git add overlay_tool/ocr.py tests/test_ocr_parsing.py
git commit -m "feat: add PaddleOCR wrapper with keyword parsing"
```

---

## Task 5: overlay.py (tkinter 오버레이 창)

**Files:**
- Create: `overlay_tool/overlay.py`

> tkinter GUI는 자동 단위 테스트 어려움. 코드 작성 후 수동 검증 필요.

**Step 1: overlay.py 구현**

```python
# overlay_tool/overlay.py
import tkinter as tk
from typing import Callable, Optional
from overlay_tool.config import OVERLAY_X, OVERLAY_Y, OVERLAY_WIDTH, OVERLAY_HEIGHT


class TraitOverlay:
    """
    항상 최상위에 표시되는 반투명 결과 오버레이 창.

    상태:
    - idle: 기본 (반투명, 대기 중 표시)
    - invalid: 빨간 ❌
    - valid_unowned: 초록 ✅ + 기질 이름 + [소유 중] 버튼
    - valid_owned: 노란 ⚠️ + "이미 소유 중"
    """

    BG_COLOR = '#1a1a1a'
    ALPHA = 0.88

    STATUS_STYLES = {
        'idle':          {'icon': '⏳', 'color': '#aaaaaa', 'msg': '스캔 대기 중 (F9)'},
        'scanning':      {'icon': '🔍', 'color': '#aaaaaa', 'msg': '스캔 중...'},
        'invalid':       {'icon': '❌', 'color': '#ff4444', 'msg': '비유효 기질'},
        'valid_unowned': {'icon': '✅', 'color': '#44ff88', 'msg': ''},
        'valid_owned':   {'icon': '⚠️', 'color': '#ffdd44', 'msg': '이미 소유 중'},
        'error':         {'icon': '⚠️', 'color': '#ff8800', 'msg': '오류 발생'},
    }

    def __init__(self, on_mark_owned: Optional[Callable] = None):
        """
        on_mark_owned: [소유 중] 버튼 클릭 시 호출되는 콜백
        """
        self._on_mark_owned = on_mark_owned
        self._click_through = False
        self._root: Optional[tk.Tk] = None

    def build(self) -> None:
        """tkinter 창 초기화. mainloop() 전에 호출."""
        self._root = tk.Tk()
        self._root.title('기질 오버레이')
        self._root.geometry(f'{OVERLAY_WIDTH}x{OVERLAY_HEIGHT}+{OVERLAY_X}+{OVERLAY_Y}')
        self._root.overrideredirect(True)   # 타이틀바 제거
        self._root.wm_attributes('-topmost', True)
        self._root.wm_attributes('-alpha', self.ALPHA)
        self._root.configure(bg=self.BG_COLOR)

        # 아이콘 + 상태 텍스트
        self._icon_label = tk.Label(
            self._root, text='⏳', font=('Segoe UI Emoji', 28),
            bg=self.BG_COLOR, fg='#aaaaaa'
        )
        self._icon_label.pack(side=tk.LEFT, padx=(12, 4), pady=8)

        right_frame = tk.Frame(self._root, bg=self.BG_COLOR)
        right_frame.pack(side=tk.LEFT, fill=tk.BOTH, expand=True)

        self._status_label = tk.Label(
            right_frame, text='스캔 대기 중 (F9)',
            font=('Malgun Gothic', 13, 'bold'),
            bg=self.BG_COLOR, fg='#aaaaaa', anchor='w'
        )
        self._status_label.pack(fill=tk.X, pady=(10, 0))

        self._detail_label = tk.Label(
            right_frame, text='',
            font=('Malgun Gothic', 10),
            bg=self.BG_COLOR, fg='#888888', anchor='w', wraplength=320
        )
        self._detail_label.pack(fill=tk.X)

        self._owned_btn = tk.Button(
            right_frame, text='[소유 중]',
            font=('Malgun Gothic', 10),
            bg='#333333', fg='#44ff88',
            activebackground='#44ff88', activeforeground='#000000',
            relief=tk.FLAT, cursor='hand2',
            command=self._handle_mark_owned
        )
        # 버튼은 valid_unowned 상태에서만 표시

        # 창 드래그 이동 지원
        self._root.bind('<Button-1>', self._on_drag_start)
        self._root.bind('<B1-Motion>', self._on_drag_motion)
        self._drag_x = 0
        self._drag_y = 0

    def _on_drag_start(self, event):
        self._drag_x = event.x
        self._drag_y = event.y

    def _on_drag_motion(self, event):
        dx = event.x - self._drag_x
        dy = event.y - self._drag_y
        x = self._root.winfo_x() + dx
        y = self._root.winfo_y() + dy
        self._root.geometry(f'+{x}+{y}')

    def _handle_mark_owned(self):
        if self._on_mark_owned:
            self._on_mark_owned()

    def show_idle(self):
        self._update('idle', '')

    def show_scanning(self):
        self._update('scanning', '')

    def show_invalid(self):
        self._update('invalid', '')

    def show_valid_unowned(self, matched_name: str):
        self._update('valid_unowned', matched_name)

    def show_valid_owned(self, matched_name: str):
        self._update('valid_owned', matched_name)

    def show_error(self, msg: str):
        self._update('error', msg)

    def _update(self, status: str, detail: str):
        """상태 갱신. tk 메인 스레드에서 호출해야 함."""
        style = self.STATUS_STYLES[status]
        self._icon_label.config(text=style['icon'], fg=style['color'])
        self._status_label.config(text=style['msg'] or detail, fg=style['color'])

        if status == 'valid_unowned':
            self._detail_label.config(text=detail)
            self._owned_btn.pack(anchor='w', pady=(2, 0))
        else:
            self._detail_label.config(text='')
            self._owned_btn.pack_forget()

        self._root.update_idletasks()

    def set_click_through(self, enabled: bool):
        """
        클릭 투과 ON/OFF.
        Windows에서는 WS_EX_TRANSPARENT 플래그로 구현 (pywin32 필요).
        미지원 플랫폼에서는 무시.
        """
        self._click_through = enabled
        try:
            import ctypes
            hwnd = ctypes.windll.user32.FindWindowW(None, '기질 오버레이')
            GWL_EXSTYLE = -20
            WS_EX_LAYERED = 0x00080000
            WS_EX_TRANSPARENT = 0x00000020
            style = ctypes.windll.user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
            if enabled:
                style |= WS_EX_TRANSPARENT
            else:
                style &= ~WS_EX_TRANSPARENT
            ctypes.windll.user32.SetWindowLongW(hwnd, GWL_EXSTYLE, style)
        except Exception:
            pass  # Windows 외 환경에서 무시

    def schedule(self, fn: Callable, delay_ms: int = 0):
        """메인 스레드에서 함수 예약 실행 (after 사용)."""
        if self._root:
            self._root.after(delay_ms, fn)

    def run(self):
        """tkinter 메인 루프 시작. 블로킹 호출."""
        if self._root:
            self._root.mainloop()
```

**Step 2: Commit**

```bash
git add overlay_tool/overlay.py
git commit -m "feat: add tkinter overlay with status display and owned button"
```

---

## Task 6: main.py (진입점 + 단축키 루프)

**Files:**
- Create: `overlay_tool/main.py`
- Create: `overlay_tool/__init__.py`

**Step 1: `__init__.py` 생성 (빈 파일)**

```python
# overlay_tool/__init__.py
```

**Step 2: main.py 구현**

```python
# overlay_tool/main.py
"""
명일방주 엔드필드 기질 오버레이 툴
실행: python -m overlay_tool.main  또는  python overlay_tool/main.py
"""
import threading
import keyboard

from overlay_tool.config import HOTKEY_SCAN, HOTKEY_TOGGLE_PASSTHROUGH
from overlay_tool.capture import capture_and_preprocess
from overlay_tool.ocr import scan_keywords, get_ocr
from overlay_tool.matcher import TraitMatcher, MatchResult
from overlay_tool.overlay import TraitOverlay
from overlay_tool.config import VALID_TRAITS_PATH, OWNED_TRAITS_PATH


# 전역 상태
_overlay: TraitOverlay = None
_matcher: TraitMatcher = None
_last_keywords: list[str] = []
_click_through: bool = False


def _on_scan():
    """F9 단축키 콜백: 캡처 → OCR → 매칭 → 오버레이 갱신 (별도 스레드)."""
    def _run():
        global _last_keywords
        _overlay.schedule(_overlay.show_scanning)
        try:
            image = capture_and_preprocess()
            keywords = scan_keywords(image)

            if not keywords:
                _overlay.schedule(lambda: _overlay.show_error('키워드 3개 인식 실패'))
                return

            _last_keywords = keywords
            result: MatchResult = _matcher.match(keywords)

            if result.status == 'invalid':
                _overlay.schedule(_overlay.show_invalid)
            elif result.status == 'valid_unowned':
                name = result.matched_name or ','.join(keywords)
                _overlay.schedule(lambda: _overlay.show_valid_unowned(name))
            elif result.status == 'valid_owned':
                name = result.matched_name or ','.join(keywords)
                _overlay.schedule(lambda: _overlay.show_valid_owned(name))

        except Exception as e:
            err_msg = str(e)[:80]
            _overlay.schedule(lambda: _overlay.show_error(err_msg))

    threading.Thread(target=_run, daemon=True).start()


def _on_mark_owned():
    """[소유 중] 버튼 클릭 콜백."""
    global _last_keywords
    if _last_keywords:
        _matcher.mark_owned(_last_keywords)
        name = ','.join(_last_keywords)
        _overlay.schedule(lambda: _overlay.show_valid_owned(name))


def _on_toggle_passthrough():
    """F10 단축키 콜백: 클릭 투과 토글."""
    global _click_through
    _click_through = not _click_through
    _overlay.set_click_through(_click_through)


def main():
    global _overlay, _matcher

    print("기질 오버레이 툴 시작 중...")
    print("PaddleOCR 초기화 중 (최초 1회, 시간이 걸릴 수 있습니다)...")
    get_ocr()  # 미리 초기화
    print("OCR 초기화 완료.")

    _matcher = TraitMatcher(VALID_TRAITS_PATH, OWNED_TRAITS_PATH)
    _overlay = TraitOverlay(on_mark_owned=_on_mark_owned)
    _overlay.build()

    # 단축키 등록 (별도 스레드에서 동작)
    keyboard.add_hotkey(HOTKEY_SCAN, _on_scan)
    keyboard.add_hotkey(HOTKEY_TOGGLE_PASSTHROUGH, _on_toggle_passthrough)

    print(f"준비 완료! {HOTKEY_SCAN.upper()} = 스캔, {HOTKEY_TOGGLE_PASSTHROUGH.upper()} = 투과 토글")
    _overlay.run()  # tkinter 메인 루프 (블로킹)


if __name__ == '__main__':
    main()
```

**Step 3: Commit**

```bash
git add overlay_tool/__init__.py overlay_tool/main.py
git commit -m "feat: add main entry point with hotkey loop and scan pipeline"
```

---

## Task 7: 통합 테스트 & README

**Files:**
- Create: `README.md`
- Create: `tests/__init__.py`
- Create: `requirements.txt`

**Step 1: requirements.txt 작성**

```
mss>=9.0
paddleocr>=2.7
paddlepaddle>=2.6
opencv-python>=4.8
rapidfuzz>=3.0
keyboard>=0.13
```

**Step 2: README.md 작성**

```markdown
# 명일방주 엔드필드 기질 오버레이 툴

게임 화면을 실시간 스캔하여 기질 키워드를 OCR로 읽고, 유효/소유 여부를 오버레이로 즉시 표시.

## 설치

Windows Python 3.10+ 환경에서:

```bash
pip install -r requirements.txt
```

## 실행

```bash
python -m overlay_tool.main
# 또는
python overlay_tool/main.py
```

## 단축키

| 키 | 동작 |
|---|---|
| F9 | 화면 스캔 & 결과 표시 |
| F10 | 클릭 투과 ON/OFF 토글 |

## 캡처 영역 설정

`overlay_tool/config.py`의 `CAPTURE_REGION`을 게임 해상도에 맞게 수정:

```python
CAPTURE_REGION = {
    "top": 200,    # 캡처 시작 Y 좌표
    "left": 50,    # 캡처 시작 X 좌표
    "width": 400,  # 캡처 너비
    "height": 300, # 캡처 높이
}
```

## 기질 목록 편집

`overlay_tool/valid_traits.txt`를 텍스트 편집기로 직접 편집:

```
# 주석
민첩 증가,치명타 확률 증가,고통
공격 강화,화염,치유
```

## 오버레이 의미

| 표시 | 의미 |
|---|---|
| ❌ 빨간색 | 비유효 기질 |
| ✅ 초록색 | 유효 & 미소유 → [소유 중] 버튼 클릭으로 등록 |
| ⚠️ 노란색 | 유효하나 이미 소유 중 |
```

**Step 3: 단위 테스트 전체 실행 (WSL2)**

```bash
cd <project-root>
pip install rapidfuzz opencv-python
python -m pytest tests/ -v --ignore=tests/test_integration.py
```

Expected: `test_matcher.py` 및 `test_ocr_parsing.py` 모두 PASS, `test_capture.py` PASS

**Step 4: Windows 환경 수동 검증 체크리스트**

```
□ python -m overlay_tool.main 실행 → "OCR 초기화 완료" 출력 확인
□ 오버레이 창이 화면 최상위에 표시됨
□ F9 누르면 "스캔 중..." 표시 후 결과 갱신
□ valid_traits.txt에 있는 기질 화면에서 F9 → ✅ 초록 표시
□ owned_traits.txt에 있는 기질 → ⚠️ 노란 표시
□ [소유 중] 클릭 → owned_traits.txt에 추가 & ⚠️ 변경
□ F10 → 오버레이 투과 (마우스 클릭이 게임 창에 전달)
□ 오버레이 창 드래그 이동 가능
```

**Step 5: Final Commit**

```bash
git add requirements.txt README.md tests/__init__.py
git commit -m "feat: complete trait overlay tool with docs and requirements"
```

---

## 캡처 영역 캘리브레이션 가이드

게임 실행 후 기질 패널이 보이는 상태에서:

```python
# calibrate.py (임시 스크립트)
import mss, cv2, numpy as np

with mss.mss() as sct:
    # 전체 화면 캡처
    full = np.array(sct.grab(sct.monitors[1]))

# 일부 영역 확인용 창 표시
cv2.imshow('Full Screen', cv2.resize(full, (1280, 720)))
cv2.waitKey(0)
```

1. 전체 화면 캡처로 좌표 확인
2. `config.py`의 `CAPTURE_REGION` 수정
3. F9로 스캔 테스트

---

## 알려진 제약

- **keyboard 모듈**: Windows에서 관리자 권한이 필요할 수 있음 (전역 단축키)
- **PaddleOCR**: 최초 실행 시 모델 다운로드 (~500MB)
- **클릭 투과 (F10)**: Windows 전용 (ctypes WS_EX_TRANSPARENT)
- **한국어 OCR 정확도**: 캡처 영역과 UPSCALE_FACTOR 튜닝으로 개선 가능
