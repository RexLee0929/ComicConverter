#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ComicConverter 一键构建：主程序 → payload.zip → 安装器 → 同步到发布区。

用法:
  python tools/build.py                 # 只编译，不碰发布区（默认）
  python tools/build.py --apply         # 编译并把产物同步到发布区的 dist/（源码也一并同步）
  python tools/build.py --apply --clean # 先清 bin/obj 再编译
  python tools/build.py --no-installer  # 跳过安装器（只编主程序）
  python tools/build.py --dest D:/Code/ComicConverter
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
APP_DIR = ROOT / "src" / "app"
SETUP_DIR = ROOT / "src" / "installer"
APP_OUT = APP_DIR / "bin" / "Release" / "net48"
SETUP_OUT = SETUP_DIR / "bin" / "Release" / "net48"
PAYLOAD = SETUP_DIR / "payload.zip"
SETUP_EXE = "ComicConverter-Setup.exe"
SKIP_EXT = {".pdb", ".xml", ".zip"}
# 运行时产生的文件绝不能进安装包：会把开发者本机的设置带给用户
SKIP_NAMES = {"config.json", "error.log"}


def wanted(p: Path) -> bool:
    return p.is_file() and p.suffix.lower() not in SKIP_EXT and p.name.lower() not in SKIP_NAMES


def run(cmd: list[str], cwd: Path) -> bool:
    print(f"$ {' '.join(cmd)}   (cwd={cwd.relative_to(ROOT)})")
    try:
        p = subprocess.run(cmd, cwd=str(cwd), capture_output=True)
    except FileNotFoundError:
        print("× 找不到 dotnet，请确认已安装 .NET SDK")
        return False

    out = p.stdout.decode("utf-8", "replace").strip()
    err = p.stderr.decode("utf-8", "replace").strip()
    if p.returncode != 0:
        print(out[-3000:])
        print(err[-3000:])
        return False

    # 只回显警告与摘要，避免刷屏
    for line in out.splitlines():
        s = line.strip()
        if s.startswith("warning") or "警告" in s or "错误" in s or "-> " in s:
            print("  " + s)
    return True


def make_payload() -> int:
    if not APP_OUT.is_dir():
        print(f"× 找不到主程序产物目录: {APP_OUT}")
        return 0

    files = [p for p in APP_OUT.rglob("*") if wanted(p)]
    if not files:
        print("× 主程序产物为空")
        return 0

    if PAYLOAD.exists():
        PAYLOAD.unlink()
    with zipfile.ZipFile(PAYLOAD, "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for p in sorted(files):
            z.write(p, p.relative_to(APP_OUT).as_posix())

    kb = PAYLOAD.stat().st_size / 1024
    print(f"payload.zip  {len(files)} 个文件  {kb:.0f} KB")
    return len(files)


def artifact_list() -> list[tuple[Path, Path]]:
    """(源绝对路径, 目标相对路径)"""
    pairs: list[tuple[Path, Path]] = []
    if APP_OUT.is_dir():
        for p in APP_OUT.rglob("*"):
            if not wanted(p):
                continue
            pairs.append((p, p.relative_to(APP_OUT)))
    setup = SETUP_OUT / SETUP_EXE
    if setup.is_file():
        pairs.append((setup, Path(SETUP_EXE)))
    return pairs


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--apply", action="store_true", help="同步到发布区（默认只编译）")
    ap.add_argument("--clean", action="store_true", help="编译前删除 bin/obj")
    ap.add_argument("--no-installer", action="store_true")
    ap.add_argument("--dest", default=os.environ.get("CC_DEST", r"D:/Code/ComicConverter"))
    args = ap.parse_args()

    dest = Path(args.dest).resolve()

    if args.clean:
        for d in [APP_DIR / "bin", APP_DIR / "obj", SETUP_DIR / "bin", SETUP_DIR / "obj"]:
            if d.is_dir():
                shutil.rmtree(d, ignore_errors=True)
                print(f"清理 {d.relative_to(ROOT)}")

    print("=" * 64)
    print("[1/3] 编译主程序")
    if not run(["dotnet", "build", "-c", "Release", "-v", "m"], APP_DIR):
        return 1

    print("[2/3] 打包 payload.zip")
    n = make_payload()
    if n == 0:
        return 1

    if not args.no_installer:
        print("[3/3] 编译安装器")
        if not run(["dotnet", "build", "-c", "Release", "-v", "m"], SETUP_DIR):
            return 1
    else:
        print("[3/3] 跳过安装器")

    pairs = artifact_list()
    print("-" * 64)
    print(f"产物 {len(pairs)} 个：")
    for src, rel in pairs:
        print(f"  {rel.as_posix():<44} {src.stat().st_size / 1024:>8.0f} KB")

    if not args.apply:
        print("-" * 64)
        print("未指定 --apply，发布区未改动。")
        return 0

    if not dest.is_dir():
        print(f"× 发布区不存在: {dest}")
        return 2

    # 发布产物统一进 dist/：dist 在 .gitignore 里，仓库只留源码
    dist = dest / "dist"
    written = 0
    for src, rel in pairs:
        dst = dist / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, dst)
        written += 1
    print(f"已写入 {dist}  {written} 个文件")

    print("-" * 64)
    print("同步源码")
    # --prune：发布区是编辑区的镜像，编辑区删掉的文件发布区也要删，
    # 否则残留旧 .cs 会和新文件重复定义，发布区直接编译不过。
    rc = subprocess.run([sys.executable, str(HERE / "sync_to_code.py"),
                         "--apply", "--prune", "--dest", str(dest)])
    return rc.returncode


if __name__ == "__main__":
    sys.exit(main())
