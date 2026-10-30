# -*- coding: utf-8 -*-
"""抓「文件被占用」防护页的截图：一路走到准备安装，点「安装」后抓新页面。
用法：python shot_guard.py <setup.exe> <输出目录>
"""
import ctypes, os, subprocess, sys, time
from ctypes import wintypes
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from shot_installer import grab, write_png, find_child, dismiss_dialog, BM_CLICK, WM_CLOSE

user32 = ctypes.windll.user32

def buttons(hwnd):
    res = []
    @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def cb(c, _):
        cls = ctypes.create_unicode_buffer(128); user32.GetClassNameW(c, cls, 128)
        if 'BUTTON' in cls.value.upper() and user32.IsWindowVisible(c):
            t = ctypes.create_unicode_buffer(128); user32.GetWindowTextW(c, t, 128)
            if t.value.strip(): res.append(t.value.strip())
        return True
    user32.EnumChildWindows(hwnd, cb, 0)
    return res

def click(hwnd, text, wait=0.7):
    b = find_child(hwnd, "BUTTON", text)
    if not b:
        return False
    user32.PostMessageW(b, BM_CLICK, 0, 0); time.sleep(wait); return True

def main():
    exe, outdir = sys.argv[1], sys.argv[2]
    os.makedirs(outdir, exist_ok=True)
    proc = subprocess.Popen([exe])
    hwnd = None
    for _ in range(100):
        hwnd = user32.FindWindowW(None, "ComicConverter 安装")
        if hwnd: break
        time.sleep(0.1)
    time.sleep(1.0)

    for _ in range(8):
        if "安装" in buttons(hwnd): break
        click(hwnd, "下一步")
        for _ in range(3):
            if not dismiss_dialog(): break

    # 到达准备安装页，点安装 → 触发占用防护
    click(hwnd, "安装", wait=1.5)
    for _ in range(3):
        if not dismiss_dialog(): break

    w, h, rgb = grab(hwnd)
    write_png(os.path.join(outdir, "guard-blocked.png"), w, h, rgb)
    print("抓取防护页 %dx%d 按钮=%r" % (w, h, buttons(hwnd)))

    user32.PostMessageW(hwnd, WM_CLOSE, 0, 0)
    time.sleep(0.4)
    if proc.poll() is None: proc.kill()

main()