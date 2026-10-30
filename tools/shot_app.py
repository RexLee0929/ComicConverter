"""抓 ComicConverter 主程序窗口的截图（只读，不改文件）。

用法：
    python shot_app.py <ComicConverter.exe> <输出目录> <标签>

启动程序 → 等窗口就绪 → 用屏幕 DC 抓窗口区域（WebView2 是独立进程渲染，
PrintWindow 抓不到内容，所以走 BitBlt）→ 可选：模拟点菜单 → 关窗口。
"""
import ctypes
import os
import struct
import subprocess
import sys
import time
import zlib
from ctypes import wintypes

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32
WM_CLOSE = 0x0010
SRCCOPY = 0x00CC0020

try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    try:
        user32.SetProcessDPIAware()
    except Exception:
        pass


class RECT(ctypes.Structure):
    _fields_ = [("left", ctypes.c_long), ("top", ctypes.c_long),
                ("right", ctypes.c_long), ("bottom", ctypes.c_long)]


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [("biSize", wintypes.DWORD), ("biWidth", ctypes.c_long),
                ("biHeight", ctypes.c_long), ("biPlanes", wintypes.WORD),
                ("biBitCount", wintypes.WORD), ("biCompression", wintypes.DWORD),
                ("biSizeImage", wintypes.DWORD), ("biXPelsPerMeter", ctypes.c_long),
                ("biYPelsPerMeter", ctypes.c_long), ("biClrUsed", wintypes.DWORD),
                ("biClrImportant", wintypes.DWORD)]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", wintypes.DWORD * 3)]


def grab_screen(rect):
    """从屏幕 DC 抓一块矩形，返回 (w, h, RGB bytes)。"""
    x, y = rect.left, rect.top
    w, h = rect.right - rect.left, rect.bottom - rect.top

    hdc = user32.GetDC(0)
    mem = gdi32.CreateCompatibleDC(hdc)
    bmp = gdi32.CreateCompatibleBitmap(hdc, w, h)
    gdi32.SelectObject(mem, bmp)
    gdi32.BitBlt(mem, 0, 0, w, h, hdc, x, y, SRCCOPY)

    bmi = BITMAPINFO()
    bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bmi.bmiHeader.biWidth = w
    bmi.bmiHeader.biHeight = -h
    bmi.bmiHeader.biPlanes = 1
    bmi.bmiHeader.biBitCount = 32
    bmi.bmiHeader.biCompression = 0

    buf = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(mem, bmp, 0, h, buf, ctypes.byref(bmi), 0)

    gdi32.DeleteObject(bmp)
    gdi32.DeleteDC(mem)
    user32.ReleaseDC(0, hdc)

    raw = buf.raw
    out = bytearray(w * h * 3)
    for yy in range(h):
        s = yy * w * 4
        row = raw[s:s + w * 4]
        out[yy * w * 3 + 0::3][:] = row[2::4]
        out[yy * w * 3 + 1::3][:] = row[1::4]
        out[yy * w * 3 + 2::3][:] = row[0::4]
    return w, h, bytes(out)


def write_png(path, w, h, rgb):
    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)

    stride = w * 3
    raw = bytearray()
    for y in range(h):
        raw.append(0)
        raw += rgb[y * stride:(y + 1) * stride]

    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 2, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(raw), 6))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def grab_printwindow(hwnd):
    """用 PrintWindow 抓窗口自身绘制内容（对 WPF 自绘区域有效）。"""
    r = RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    w, h = r.right - r.left, r.bottom - r.top

    hdc = user32.GetWindowDC(hwnd)
    mem = gdi32.CreateCompatibleDC(hdc)
    bmp = gdi32.CreateCompatibleBitmap(hdc, w, h)
    gdi32.SelectObject(mem, bmp)
    user32.PrintWindow(hwnd, mem, 2)   # PW_RENDERFULLCONTENT：走 DWM 合成表面

    bmi = BITMAPINFO()
    bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bmi.bmiHeader.biWidth = w
    bmi.bmiHeader.biHeight = -h
    bmi.bmiHeader.biPlanes = 1
    bmi.bmiHeader.biBitCount = 32
    bmi.bmiHeader.biCompression = 0

    buf = ctypes.create_string_buffer(w * h * 4)
    gdi32.GetDIBits(mem, bmp, 0, h, buf, ctypes.byref(bmi), 0)

    gdi32.DeleteObject(bmp)
    gdi32.DeleteDC(mem)
    user32.ReleaseDC(hwnd, hdc)

    raw = buf.raw
    out = bytearray(w * h * 3)
    for yy in range(h):
        s = yy * w * 4
        row = raw[s:s + w * 4]
        out[yy * w * 3 + 0::3][:] = row[2::4]
        out[yy * w * 3 + 1::3][:] = row[1::4]
        out[yy * w * 3 + 2::3][:] = row[0::4]
    return w, h, bytes(out)


def main():
    exe, outdir, tag = sys.argv[1], sys.argv[2], sys.argv[3]
    mode = sys.argv[4] if len(sys.argv) > 4 else ""
    os.makedirs(outdir, exist_ok=True)

    proc = subprocess.Popen([exe])
    hwnd = None
    for _ in range(120):
        hwnd = user32.FindWindowW(None, "ComicConverter")
        if hwnd:
            break
        time.sleep(0.1)
    if not hwnd:
        proc.kill()
        raise SystemExit("找不到主窗口")

    user32.SetForegroundWindow(hwnd)
    time.sleep(4.0)                      # 等 WebView2 首屏渲染完

    wins = [("pw", grab_printwindow)]
    if mode != "pwonly":
        wins.append(("scr", lambda h: grab_screen(_rect(h))))

    for suffix, fn in wins:
        try:
            w, h, rgb = fn(hwnd)
        except Exception as ex:
            print("  %s 失败: %s" % (suffix, ex))
            continue
        write_png(os.path.join(outdir, "%s-%s.png" % (tag, suffix)), w, h, rgb)
        print("%s-%s  %dx%d" % (tag, suffix, w, h))

    user32.PostMessageW(hwnd, WM_CLOSE, 0, 0)
    time.sleep(0.8)
    if proc.poll() is None:
        time.sleep(0.5)
        proc.kill()


def _rect(hwnd):
    r = RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(r))
    return r


if __name__ == "__main__":
    main()