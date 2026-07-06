#!/usr/bin/env python3
"""
同步 Obsidian 插件配置 — 在两个仓库间同步插件

用法:
  python 脚本/sync_obsidian_plugins.py          # 从千水仓库同步到当前工作区
  python 脚本/sync_obsidian_plugins.py --reverse # 从当前工作区同步到千水仓库

功能:
  - 拷贝 .obsidian/plugins 目录，只更新有差异的文件
  - 千水仓库路径按需修改 SOURCE_VAULT
"""

import sys
import shutil
import filecmp
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

# ⚙️ 千水仓库路径
SOURCE_VAULT = Path(r"O:\黑曜石文档\千水")


def sync_plugins(src: Path, dst: Path, direction: str):
    src_plugins = src / ".obsidian" / "plugins"
    dst_plugins = dst / ".obsidian" / "plugins"

    if not src_plugins.exists():
        print(f"❌ 源插件目录不存在: {src_plugins}")
        return

    dst_plugins.mkdir(parents=True, exist_ok=True)

    copied = 0
    skipped = 0

    for item in src_plugins.rglob("*"):
        if item.is_relative_to(src_plugins / ".git"):
            continue
        rel = item.relative_to(src_plugins)
        target = dst_plugins / rel

        if item.is_dir():
            target.mkdir(parents=True, exist_ok=True)
        else:
            if target.exists() and filecmp.cmp(item, target, shallow=False):
                skipped += 1
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(item, target)
            copied += 1

    print(f"  ✅ {direction}: 复制 {copied} 个, 跳过 {skipped} 个（无变化）")
    print(f"  目标: {dst_plugins}")


def main():
    reverse = "--reverse" in sys.argv

    if not SOURCE_VAULT.exists():
        print(f"❌ 源仓库路径不存在: {SOURCE_VAULT}")
        print(f"   请修改脚本中的 SOURCE_VAULT 变量")
        sys.exit(1)

    if reverse:
        print("📤 反向同步：当前工作区 → 千水仓库")
        sync_plugins(ROOT, SOURCE_VAULT, "当前工作区 → 千水")
    else:
        print("📥 正向同步：千水仓库 → 当前工作区")
        sync_plugins(SOURCE_VAULT, ROOT, "千水 → 当前工作区")

    print()
    print("💡 在 Obsidian 中重新加载：设置 → 社区插件 → 刷新")


if __name__ == "__main__":
    main()
