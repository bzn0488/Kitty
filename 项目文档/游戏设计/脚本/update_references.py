#!/usr/bin/env python3
"""
交叉引用更新脚本 — 更新所有 .md 文件中对理论知识/和项目经验/文件的引用，
因为文件已被添加了编号前缀。

用法:
  python 脚本/update_references.py --dry-run   # 预览模式
  python 脚本/update_references.py             # 实际执行

工作原理：
  1. 扫描理论知识/和项目经验/目录，建立「旧名 → 新名」映射
  2. 扫描知识库/下所有 .md 文件
  3. 将文件中出现的旧引用替换为新引用
"""

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
KNOWLEDGE_DIR = ROOT / "知识库"

# 需要更新的目录及其在引用中的前缀
WATCH_DIRS = {
    "理论知识": "理论知识/",
    "项目经验": "项目经验/",
}

DRY_RUN = "--dry-run" in sys.argv


def build_rename_map() -> dict:
    """构建旧名 → 新名的映射字典（仅包含实际被重命名的文件）"""
    rename_map = {}

    for dir_name, prefix in WATCH_DIRS.items():
        target_dir = KNOWLEDGE_DIR / dir_name
        if not target_dir.exists():
            continue

        for f in target_dir.glob("*.md"):
            if f.name.startswith("_"):
                continue
            # 如果文件有编号前缀，提取旧名
            match = re.match(r"^(\d{2})-(.+)$", f.name)
            if match:
                new_name = f.name
                old_name = match.group(2)
                rename_map[prefix + old_name] = prefix + new_name
                # 也匹配不带目录前缀的引用
                rename_map[old_name] = new_name

    return rename_map


def update_file(filepath: Path, rename_map: dict) -> bool:
    """更新单个文件中的引用，返回是否修改"""
    content = filepath.read_text(encoding="utf-8")
    original = content
    modified = False

    # 修改文件中在知识库特定段落的引用
    for old, new in sorted(rename_map.items(), key=lambda x: -len(x[0])):
        # 替换 markdown 链接 [text](ref)
        content = content.replace(f"]({old})", f"]({new})")
        content = content.replace(f"`{old}`", f"`{new}`")
        content = content.replace(f"「{old}」", f"「{new}」")
        # 替换无包裹的引用（通常是列表项）
        content = content.replace(f"`{old}", f"`{new}")

    if content != original:
        if not DRY_RUN:
            filepath.write_text(content, encoding="utf-8")
        return True
    return False


def main():
    print("=" * 60)
    print("🔗 交叉引用更新工具")
    if DRY_RUN:
        print("  模式: 🔍 预览（不修改文件）")
    else:
        print("  模式: ✏️  实际执行")
    print("=" * 60)

    rename_map = build_rename_map()
    print(f"\n📋 发现 {len(rename_map)} 个重命名映射:")
    for old, new in sorted(rename_map.items()):
        print(f"  {old} → {new}")

    print(f"\n🔍 扫描知识库 .md 文件...")
    md_files = sorted(KNOWLEDGE_DIR.rglob("*.md"))
    
    modified_count = 0
    for md_file in md_files:
        if update_file(md_file, rename_map):
            rel = md_file.relative_to(KNOWLEDGE_DIR)
            print(f"  ✏️  {rel}")
            modified_count += 1

    print(f"\n{'=' * 60}")
    if DRY_RUN:
        print(f"📊 预览完成: 将修改 {modified_count} 个文件")
    else:
        print(f"📊 更新完成: 已修改 {modified_count} 个文件")
    print(f"{'=' * 60}")

    # 始终输出 JSON 以便工作流解析
    import json
    print(json.dumps({
        "dry_run": DRY_RUN,
        "mappings": len(rename_map),
        "files_modified": modified_count,
    }, ensure_ascii=False))


if __name__ == "__main__":
    main()
