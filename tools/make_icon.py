#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
生成 ComicConverter 的程序图标（.ico），纯标准库实现，不依赖 Pillow。

图形：深绿圆角底 + 两张叠放的白色漫画页，前页印着分格（一大两小）。
      表达「漫画页」，比对话气泡更贴合这个工具的用途。母版 512px 用距离场绘制，
      做 1px 抗锯齿，再逐级缩到 16/32/48/64/128/256。

用法:
  python tools/make_icon.py                     # 写入 src/app/app.ico 与 src/installer/app.ico
  python tools/make_icon.py --preview out.png   # 额外导出 256px 预览图
  python tools/make_icon.py --sheet out.png     # 导出 16/32/48 放大对照图，检查小尺寸可读性
"""

from __future__ import annotations

import argparse
import math
import struct
import sys
import zlib
from pathlib import Path

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent

MASTER = 512
SIZES = [16, 32, 48, 64, 128, 256]

BG_TOP = (0x1B, 0x8F, 0x72)
BG_BOTTOM = (0x0A, 0x52, 0x40)
INK = (0x0F, 0x6E, 0x56)
WHITE = (0xFF, 0xFF, 0xFF)


# ----------------------------------------------------------------- 几何
def sd_round_box(px, py, cx, cy, hw, hh, r):
    dx = abs(px - cx) - hw + r
    dy = abs(py - cy) - hh + r
    ax = dx if dx > 0 else 0.0
    ay = dy if dy > 0 else 0.0
    return math.hypot(ax, ay) + min(max(dx, dy), 0.0) - r


def cover(d):
    v = 0.5 - d
    return 0.0 if v <= 0 else (1.0 if v >= 1 else v)


def render_master() -> bytearray:
    W = H = MASTER
    buf = bytearray(W * H * 4)
    half = W / 2.0

    # 形状按绘制顺序：后页 -> 前页 -> 三个分格
    shapes = [
        (lambda x, y: sd_round_box(x, y, 284, 238, 112, 138, 18), WHITE, 0.40),
        (lambda x, y: sd_round_box(x, y, 244, 268, 124, 144, 18), WHITE, 1.00),
        (lambda x, y: sd_round_box(x, y, 244, 202, 94, 44, 10), INK, 1.00),
        (lambda x, y: sd_round_box(x, y, 192, 327, 44, 56, 10), INK, 1.00),
        (lambda x, y: sd_round_box(x, y, 296, 327, 44, 56, 10), INK, 1.00),
    ]

    for y in range(H):
        py = y + 0.5
        t = py / H
        r0 = BG_TOP[0] + (BG_BOTTOM[0] - BG_TOP[0]) * t
        g0 = BG_TOP[1] + (BG_BOTTOM[1] - BG_TOP[1]) * t
        b0 = BG_TOP[2] + (BG_BOTTOM[2] - BG_TOP[2]) * t

        for x in range(W):
            px = x + 0.5
            a_bg = cover(sd_round_box(px, py, half, half, half, half, W * 0.22))
            if a_bg <= 0.0:
                continue

            ar, ag, ab = r0, g0, b0
            for fn, col, alpha in shapes:
                a = cover(fn(px, py)) * alpha
                if a > 0.0:
                    ar = ar * (1 - a) + col[0] * a
                    ag = ag * (1 - a) + col[1] * a
                    ab = ab * (1 - a) + col[2] * a

            i = (y * W + x) * 4
            buf[i] = int(ar + 0.5)
            buf[i + 1] = int(ag + 0.5)
            buf[i + 2] = int(ab + 0.5)
            buf[i + 3] = int(a_bg * 255 + 0.5)

    return buf


def downscale(buf: bytearray, src: int, dst: int) -> bytearray:
    """预乘 alpha 的盒式平均，避免边缘发黑。"""
    out = bytearray(dst * dst * 4)
    k = src / dst
    for y in range(dst):
        y0 = int(y * k)
        y1 = max(y0 + 1, int((y + 1) * k))
        for x in range(dst):
            x0 = int(x * k)
            x1 = max(x0 + 1, int((x + 1) * k))
            rs = gs = bs = as_ = 0
            n = 0
            for yy in range(y0, y1):
                base = yy * src * 4
                for xx in range(x0, x1):
                    i = base + xx * 4
                    a = buf[i + 3]
                    rs += buf[i] * a
                    gs += buf[i + 1] * a
                    bs += buf[i + 2] * a
                    as_ += a
                    n += 1
            o = (y * dst + x) * 4
            if as_ > 0:
                out[o] = min(255, int(rs / as_ + 0.5))
                out[o + 1] = min(255, int(gs / as_ + 0.5))
                out[o + 2] = min(255, int(bs / as_ + 0.5))
                out[o + 3] = min(255, int(as_ / n + 0.5))
    return out


def png_bytes(w: int, h: int, rgba: bytearray) -> bytes:
    raw = bytearray()
    stride = w * 4
    for y in range(h):
        raw.append(0)
        raw += rgba[y * stride:(y + 1) * stride]
    comp = zlib.compress(bytes(raw), 9)

    def chunk(tag: bytes, data: bytes) -> bytes:
        return (struct.pack(">I", len(data)) + tag + data
                + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF))

    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
            + chunk(b"IDAT", comp)
            + chunk(b"IEND", b""))


def ico_bytes(images) -> bytes:
    n = len(images)
    head = struct.pack("<HHH", 0, 1, n)
    entries = b""
    offset = 6 + 16 * n
    blobs = b""
    for size, data in images:
        dim = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        blobs += data
    return head + entries + blobs


def sheet(images, path: Path, scale: int = 4):
    """把小尺寸并排放到一张图上，放大看可读性。"""
    total_w = sum(s for s, _ in images) * scale + 20 * (len(images) + 1)
    max_h = max(s for s, _ in images) * scale
    W, H = total_w, max_h + 40
    canvas = bytearray(W * H * 4)
    for i in range(W * H):
        canvas[i * 4 + 0] = 0xF3
        canvas[i * 4 + 1] = 0xF3
        canvas[i * 4 + 2] = 0xF3
        canvas[i * 4 + 3] = 0xFF

    x_off = 20
    for size, _ in images:
        small = downscale(render_master(), MASTER, size) if size != MASTER else render_master()
        for y in range(size * scale):
            for x in range(size * scale):
                sx, sy = x // scale, y // scale
                si = (sy * size + sx) * 4
                di = ((20 + y) * W + (x_off + x)) * 4
                a = small[si + 3] / 255.0
                for c in range(3):
                    canvas[di + c] = int(small[si + c] * a + canvas[di + c] * (1 - a))
        x_off += size * scale + 20

    path.write_bytes(png_bytes(W, H, canvas))


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--preview", default="")
    ap.add_argument("--sheet", default="")
    ap.add_argument("--out", default="")
    args = ap.parse_args()

    print(f"绘制母版 {MASTER}x{MASTER} …")
    master = render_master()

    images = []
    for s in SIZES:
        small = master if s == MASTER else downscale(master, MASTER, s)
        images.append((s, png_bytes(s, s, small)))
        print(f"  {s}x{s}  {len(images[-1][1]):>6} B")

    ico = ico_bytes(images)
    targets = [Path(args.out)] if args.out else [
        ROOT / "src" / "app" / "app.ico",
        ROOT / "src" / "installer" / "app.ico",
    ]
    for t in targets:
        t.parent.mkdir(parents=True, exist_ok=True)
        t.write_bytes(ico)
        print(f"写入 {t}  ({len(ico)} B)")

    if args.preview:
        Path(args.preview).write_bytes(png_bytes(256, 256, downscale(master, MASTER, 256)))
        print(f"预览 {args.preview}")
    if args.sheet:
        sheet([(16, None), (32, None), (48, None)], Path(args.sheet))
        print(f"小尺寸对照 {args.sheet}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
