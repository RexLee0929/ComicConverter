#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
ComicConverter 同步工具：当前工作区（编辑区） -> D:\\Code\\ComicConverter（发布区）

用法:
  python tools/sync_to_code.py                 # 预演（默认，不写任何文件）
  python tools/sync_to_code.py --apply         # 真正写入发布区
  python tools/sync_to_code.py --with-build    # 连编译产物一起同步（exe/dll/runtimes/安装包）
  python tools/sync_to_code.py --dest <路径>   # 覆盖目标目录（默认取环境变量 CC_DEST 或 D:/Code/ComicConverter）
  python tools/sync_to_code.py --force         # 目标被外部改过时仍然覆盖
  python tools/sync_to_code.py --prune         # 发布区里已从源码删除的文件，一并删除（需配合 --apply）

安全策略:
  1. 不做 --apply 时只打印计划，绝不落盘。
  2. 每个写入的文件先备份到 <目标>/.sync-backup/<时间戳>/ 下，再覆盖。
  3. 若目标文件在上次同步之后被外部改动过（哈希对不上），默认报冲突并跳过，不覆盖。
  4. 默认不删除任何目标文件，除非显式 --prune。
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import sys
import time
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = Path(__file__).resolve().parent
SRC_ROOT = HERE.parent                      # 编辑区根目录
STATE_FILE = HERE / ".sync-state.json"

DEFAULT_DEST = r"D:/Code/ComicConverter"

# 源码同步白名单（相对编辑区根）
SOURCE_FILES = ["README.md", "LICENSE", ".gitignore"]
SOURCE_DIRS = ["src", "tools"]

# 判断「是不是同一批代码」时忽略的路径片段
SKIP_PARTS = {"bin", "obj", ".git", ".vs", ".sync-backup", ".sync-state.json",
              "_shots", "_shots2"}

# 编译产物所在位置（相对编辑区根）
BUILD_MAIN = "src/app/bin/Release/net48"
BUILD_SETUP = "src/installer/bin/Release/net48"


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def rel_files() -> list[Path]:
    """收集需要同步的源码文件（相对编辑区根的 POSIX 路径）。"""
    out: list[Path] = []
    for name in SOURCE_FILES:
        p = SRC_ROOT / name
        if p.is_file():
            out.append(Path(name))
    for d in SOURCE_DIRS:
        base = SRC_ROOT / d
        if not base.is_dir():
            continue
        for p in base.rglob("*"):
            if not p.is_file():
                continue
            r = p.relative_to(SRC_ROOT)
            if SKIP_PARTS & set(r.parts):
                continue
            if any(part.startswith("_") for part in r.parts):   # 临时文件 / 截图
                continue
            if r.name in ("payload.zip",):                      # 构建中间物
                continue
            out.append(r)
    return sorted(out, key=lambda x: x.as_posix().lower())


def build_files() -> list[tuple[Path, Path]]:
    """收集编译产物 -> [(源绝对路径, 目标相对路径)]，目标一律平铺到发布区根目录。"""
    pairs: list[tuple[Path, Path]] = []
    main = SRC_ROOT / BUILD_MAIN
    if main.is_dir():
        for p in main.iterdir():
            if p.is_file() and p.suffix.lower() in {".exe", ".dll", ".config"}:
                pairs.append((p, Path(p.name)))
        rt = main / "runtimes"
        if rt.is_dir():
            for p in rt.rglob("*"):
                if p.is_file():
                    pairs.append((p, Path("runtimes") / p.relative_to(rt)))
    setup = SRC_ROOT / BUILD_SETUP
    if setup.is_dir():
        for p in setup.iterdir():
            if p.is_file() and p.name.lower().endswith(".exe"):
                pairs.append((p, Path("ComicConverter-Setup.exe")))
    return pairs


def load_state() -> dict:
    if STATE_FILE.is_file():
        try:
            return json.loads(STATE_FILE.read_text(encoding="utf-8"))
        except Exception:
            print("! .sync-state.json 解析失败，按「无历史」处理")
    return {"files": {}}


def main() -> int:
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument("--apply", action="store_true", help="真正写入（默认只预演）")
    ap.add_argument("--with-build", action="store_true", help="同时同步编译产物")
    ap.add_argument("--force", action="store_true", help="目标被外部改动时强制覆盖")
    ap.add_argument("--prune", action="store_true", help="删除发布区中已不存在的源码文件")
    ap.add_argument("--dest", default=os.environ.get("CC_DEST", DEFAULT_DEST))
    args = ap.parse_args()

    dest = Path(args.dest).resolve()
    print(f"编辑区: {SRC_ROOT}")
    print(f"发布区: {dest}")
    print(f"模式  : {'写入' if args.apply else '预演(不落盘)'}"
          f"{' + 编译产物' if args.with_build else ''}")
    print("-" * 64)

    if not dest.is_dir():
        print(f"× 发布区不存在: {dest}")
        return 2
    if dest == SRC_ROOT:
        print("× 编辑区与发布区是同一个目录，无需同步")
        return 2

    state = load_state()
    known = state.get("files", {})

    tasks = [(SRC_ROOT / r, r) for r in rel_files()]
    if args.with_build:
        tasks += build_files()
        if not (SRC_ROOT / BUILD_MAIN).is_dir():
            print("! 未找到编译产物目录，--with-build 实际没带上二进制")

    add, same, conflict, backup_dir = [], [], [], None
    stamp = time.strftime("%Y%m%d-%H%M%S")
    new_state = {}

    for src, rel in tasks:
        dst = dest / rel
        if not src.is_file():
            continue
        src_hash = sha256(src)
        new_state[rel.as_posix()] = src_hash
        if not dst.is_file():
            add.append((src, rel, "新增"))
            continue
        dst_hash = sha256(dst)
        if dst_hash == src_hash:
            same.append(rel)
            continue
        was = known.get(rel.as_posix())
        if was is None or was == dst_hash or args.force:
            add.append((src, rel, "更新"))
        else:
            conflict.append((src, rel, "目标被外部改过"))

    print(f"无变化 {len(same):>4} 个")
    for src, rel, tag in add:
        print(f"  [{tag}] {rel.as_posix()}")
    if conflict:
        print(f"冲突   {len(conflict):>4} 个（目标在上次同步后被改动，未覆盖）")
        for src, rel, tag in conflict:
            print(f"  [!] {rel.as_posix()}")
        print("      → 确认要覆盖请加 --force")

    removed: list[Path] = []
    if args.prune:
        for k in known:
            if k not in new_state and (dest / k).is_file():
                removed.append(Path(k))
        for r in removed:
            print(f"  [删除] {r.as_posix()}")

    if not args.apply:
        print("-" * 64)
        print(f"预演结束：待写 {len(add)}，冲突 {len(conflict)}，待删 {len(removed)}。加 --apply 执行。")
        return 1 if conflict else 0

    if not add and not removed:
        print("-" * 64)
        print("无需写入。")
        return 0

    backup_dir = SRC_ROOT / "tools" / ".sync-backup" / stamp
    written = 0
    for src, rel, tag in add:
        dst = dest / rel
        dst.parent.mkdir(parents=True, exist_ok=True)
        if dst.is_file():
            b = backup_dir / rel
            b.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(dst, b)
        shutil.copy2(src, dst)
        written += 1

    for r in removed:
        dst = dest / r
        b = backup_dir / r
        b.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(dst, b)
        dst.unlink()

    # 写完再落状态，避免中途失败把状态写脏
    merged = dict(known)
    merged.update(new_state)
    for r in removed:
        merged.pop(r.as_posix(), None)
    STATE_FILE.write_text(
        json.dumps({"synced_at": stamp, "dest": str(dest), "files": merged},
                   ensure_ascii=False, indent=2),
        encoding="utf-8",
    )

    print("-" * 64)
    print(f"完成：写入 {written}，删除 {len(removed)}。")
    if backup_dir and backup_dir.is_dir():
        print(f"覆盖前的旧文件备份在: {backup_dir}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
