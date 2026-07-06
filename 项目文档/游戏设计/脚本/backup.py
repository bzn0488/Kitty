#!/usr/bin/env python3
"""
全量备份脚本 — 备份整个工作区到 A:\\通用工作区备份，按时间保留最多4份。

用法:
  python 脚本/backup.py

备份内容：
  - 整个 A:\\通用工作区模板 目录（全量）
  - 排除：.venv/ .obsidian/ __pycache__/ 备份/ .git/

保留策略：
  - 每次运行创建新的时间戳备份文件夹
  - 最多保留 4 份
  - 超出时自动删除最旧的
"""

import shutil
import sys
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
BACKUP_BASE = ROOT.parent / "通用工作区备份"

MAX_BACKUPS = 4

# 排除的目录/文件模式
EXCLUDE_DIRS = {
    ".venv",
    ".obsidian",
    "__pycache__",
    ".git",
    "备份",
}
EXCLUDE_FILES = {".gitignore"}


def ignore_patterns(path: str, names: list[str]) -> set[str]:
    to_ignore = set()
    p = Path(path)
    for name in names:
        full = p / name
        if full.is_dir() and name in EXCLUDE_DIRS:
            to_ignore.add(name)
        elif full.is_file() and name in EXCLUDE_FILES:
            to_ignore.add(name)
        if name == "__pycache__":
            to_ignore.add(name)
    return to_ignore


def get_existing_backups() -> list[Path]:
    if not BACKUP_BASE.is_dir():
        return []
    dirs = [d for d in BACKUP_BASE.iterdir() if d.is_dir()]
    dirs.sort(key=lambda d: d.stat().st_ctime)
    return dirs


def clean_old_backups():
    backups = get_existing_backups()
    if len(backups) <= MAX_BACKUPS:
        return
    to_delete = len(backups) - MAX_BACKUPS
    for i in range(to_delete):
        old_dir = backups[i]
        shutil.rmtree(old_dir)
        print(f"  🗑️  删除旧备份: {old_dir.name}")


def size_str(path: Path) -> str:
    total = sum(f.stat().st_size for f in path.rglob("*") if f.is_file())
    if total > 1024 * 1024:
        return f"{total / 1024 / 1024:.1f} MB"
    return f"{total / 1024:.0f} KB"


def run():
    timestamp = datetime.now().strftime("%Y-%m-%d_%H%M%S")
    backup_dir = BACKUP_BASE / timestamp
    BACKUP_BASE.mkdir(parents=True, exist_ok=True)

    print(f"📦 开始全量备份: {ROOT.name}")
    print(f"  → 目标: {backup_dir}")
    print(f"  排除: {', '.join(sorted(EXCLUDE_DIRS))}")

    try:
        shutil.copytree(ROOT, backup_dir, ignore=ignore_patterns, dirs_exist_ok=False)
    except Exception as e:
        print(f"❌ 备份失败: {e}")
        sys.exit(1)

    total_size = size_str(backup_dir)
    print(f"  ✅ 完成 ({total_size})")
    clean_old_backups()

    total_backups = len(get_existing_backups())
    print(f"\n📊 现有备份: {total_backups}/{MAX_BACKUPS}")


if __name__ == "__main__":
    run()
