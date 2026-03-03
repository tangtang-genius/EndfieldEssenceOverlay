"""
Template Cropper — 게임 스크린샷에서 기질 키워드 템플릿을 일괄 추출

사용법:
  1. 이 스크립트와 같은 폴더에 스크린샷(PNG/JPG)을 넣기
  2. 아래 CROP_REGION 값을 키워드 텍스트 영역에 맞게 조정
  3. python crop_templates.py
  4. output/ 폴더에 결과 저장됨

  --preview  첫 번째 이미지의 크롭 결과만 미리보기 (저장 안 함)
  --no-trim  자동 트리밍 없이 고정 영역만 크롭
"""

from PIL import Image
import numpy as np
from pathlib import Path
import sys

SCRIPT_DIR = Path(__file__).parent
OUTPUT_DIR = SCRIPT_DIR / "output"
IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".bmp"}

# ── 설정 ────────────────────────────────────────────────────────
# 스크린샷에서 자를 영역 (left, top, right, bottom) — 픽셀 좌표
# 키워드 텍스트를 넉넉히 포함하도록 설정 (자동 트리밍이 여백 제거)
CROP_REGION = (100, 200, 500, 260)

# 자동 트리밍 후 텍스트 주변 패딩 (px) — 모든 템플릿에 동일 적용
PADDING = 2

# 배경 판별 임계값 (0~255)
# 밝은 글씨 + 어두운 배경: 이 값 초과 픽셀 = 텍스트
BG_THRESHOLD = 80
# ────────────────────────────────────────────────────────────────


def auto_trim(img: Image.Image) -> Image.Image:
    """배경을 제거하고 텍스트 바운딩 박스 + 균일 패딩으로 크롭."""
    gray = np.array(img.convert("L"))
    mask = gray > BG_THRESHOLD

    if not mask.any():
        return img

    rows = np.any(mask, axis=1)
    cols = np.any(mask, axis=0)
    rmin, rmax = np.where(rows)[0][[0, -1]]
    cmin, cmax = np.where(cols)[0][[0, -1]]

    rmin = max(0, rmin - PADDING)
    rmax = min(gray.shape[0] - 1, rmax + PADDING)
    cmin = max(0, cmin - PADDING)
    cmax = min(gray.shape[1] - 1, cmax + PADDING)

    return img.crop((cmin, rmin, cmax + 1, rmax + 1))


def get_images():
    """스크립트 디렉터리의 이미지 파일 목록 (output/ 제외)."""
    return sorted(
        f for f in SCRIPT_DIR.iterdir()
        if f.suffix.lower() in IMAGE_EXTS
        and OUTPUT_DIR not in f.parents
        and f.parent == SCRIPT_DIR
    )


def preview(images: list[Path], do_trim: bool):
    """첫 번째 이미지의 크롭 결과를 보여줌."""
    img = Image.open(images[0])
    print(f"원본: {images[0].name}  ({img.size[0]}x{img.size[1]})")
    print(f"크롭 영역: {CROP_REGION}")

    cropped = img.crop(CROP_REGION)
    print(f"크롭 후: {cropped.size[0]}x{cropped.size[1]}")

    if do_trim:
        trimmed = auto_trim(cropped)
        print(f"트리밍 후: {trimmed.size[0]}x{trimmed.size[1]}")
        trimmed.show()
    else:
        cropped.show()


def process(images: list[Path], do_trim: bool):
    """모든 이미지를 크롭하여 output/ 에 그레이스케일 PNG로 저장."""
    OUTPUT_DIR.mkdir(exist_ok=True)

    for img_path in images:
        img = Image.open(img_path)
        cropped = img.crop(CROP_REGION)

        if do_trim:
            result = auto_trim(cropped)
        else:
            result = cropped

        # 그레이스케일 변환 (TemplateMatchService가 Grayscale로 로드)
        result = result.convert("L")

        out_name = img_path.stem + ".png"
        out_path = OUTPUT_DIR / out_name
        result.save(out_path)
        print(f"  {img_path.name:30s} → {out_name:30s}  ({result.size[0]}x{result.size[1]})")

    print(f"\n완료! {len(images)}개 → {OUTPUT_DIR}/")


def main():
    args = set(sys.argv[1:])
    do_trim = "--no-trim" not in args
    is_preview = "--preview" in args

    images = get_images()
    if not images:
        print("이미지 파일이 없습니다. 스크립트와 같은 폴더에 스크린샷을 넣어주세요.")
        return

    print(f"이미지 {len(images)}개 발견")

    if is_preview:
        preview(images, do_trim)
    else:
        process(images, do_trim)


if __name__ == "__main__":
    main()
