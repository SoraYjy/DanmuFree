#!/usr/bin/env python3
"""生成 DanmuFree 应用图标。

- 有 ``assets/app.png`` 时：读它，转多分辨率 ``assets/app.ico``。
- 没有 ``assets/app.png`` 时：生成一张占位图标（粉紫渐变圆角块 + 弹幕气泡 + 朗读喇叭）
  并写出 ``app.png`` + ``app.ico``。

用法：
    python scripts/make_ico.py                  # 生成 / 转换图标
    python scripts/make_ico.py path/to/src.png  # 用指定 PNG 作为源（推荐：放你设计的 1024×1024 1:1 PNG）

依赖：Pillow（``pip install pillow``）。
"""
from __future__ import annotations

import sys
from pathlib import Path

try:
    from PIL import Image, ImageDraw
except ImportError:
    sys.exit("需要 Pillow：pip install pillow")

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "assets"
OUT_PNG = ASSETS / "app.png"
OUT_ICO = ASSETS / "app.ico"

ICO_SIZES = [16, 24, 32, 48, 64, 128, 256]


def _lerp(a, b, t):
    return tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3))


def make_placeholder(size: int = 1024) -> Image.Image:
    """占位图标：粉→紫渐变圆角块 + 白色弹幕气泡 + 三条弹幕行 + 右下「朗读」徽标。"""
    top = (255, 107, 157)   # bilibili 粉
    bot = (123, 95, 255)    # 紫
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))

    # 竖向渐变底
    grad = Image.new("RGBA", (size, size))
    for y in range(size):
        grad.putdata([_lerp(top, bot, y / (size - 1))] * size)

    # 圆角遮罩
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, size - 1, size - 1], radius=int(size * 0.22), fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)

    # 气泡
    bw, bh = int(size * 0.56), int(size * 0.40)
    bx = (size - bw) // 2
    by = int(size * 0.26)
    d.rounded_rectangle([bx, by, bx + bw, by + bh],
                        radius=int(bh * 0.26), fill=(255, 255, 255, 255))
    # 气泡尾
    d.polygon(
        [(bx + int(bw * 0.18), by + bh),
         (bx + int(bw * 0.08), by + bh + int(size * 0.10)),
         (bx + int(bw * 0.34), by + bh)],
        fill=(255, 255, 255, 255))
    # 三条弹幕行（深紫短条）
    line_y = [by + int(bh * 0.30), by + int(bh * 0.52), by + int(bh * 0.74)]
    line_w = [int(bw * 0.62), int(bw * 0.74), int(bw * 0.48)]
    for y, w in zip(line_y, line_w):
        d.rounded_rectangle(
            [bx + int(bw * 0.12), y, bx + int(bw * 0.12) + w, y + int(size * 0.035)],
            radius=int(size * 0.02), fill=(90, 80, 120, 255))

    # 右下「朗读」徽标：白圆 + 粉色小喇叭
    cr = int(size * 0.16)
    cx, cy = size - int(size * 0.20), size - int(size * 0.20)
    d.ellipse([cx - cr, cy - cr, cx + cr, cy + cr], fill=(255, 255, 255, 255))
    tr = cr * 0.5
    d.polygon(
        [(cx - tr * 0.55, cy - tr * 0.45),
         (cx + tr, cy - tr),
         (cx + tr, cy + tr),
         (cx - tr * 0.55, cy + tr * 0.45)],
        fill=(255, 107, 157, 255))
    return img


def to_ico(img: Image.Image, path: Path) -> None:
    img.save(path, format="ICO", sizes=[(s, s) for s in ICO_SIZES])


def main() -> None:
    src = Path(sys.argv[1]) if len(sys.argv) > 1 else None
    ASSETS.mkdir(parents=True, exist_ok=True)

    if src and src.exists():
        base = Image.open(src).convert("RGBA")
        base.save(OUT_PNG)  # 同时刷新源
        print(f"用源图 {src}")
    elif OUT_PNG.exists():
        base = Image.open(OUT_PNG).convert("RGBA")
        print(f"用已有 {OUT_PNG}")
    else:
        base = make_placeholder()
        base.save(OUT_PNG)
        print(f"生成占位图标 → {OUT_PNG}（替换为你设计的 1:1 PNG 后重跑本脚本即可）")

    to_ico(base, OUT_ICO)
    print(f"写出 {OUT_ICO}（{', '.join(str(s) for s in ICO_SIZES)}px）")


if __name__ == "__main__":
    main()
