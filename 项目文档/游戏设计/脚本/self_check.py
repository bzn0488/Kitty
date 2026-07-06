#!/usr/bin/env python3
"""
知识库自检脚本 — 检查文档完整性、编号规范性、索引一致性。

功能:
  1. 文档存在性检查 — 对照现有索引，检查文件是否被误删
  2. 编号规范性检查 — 检查需要编号的目录是否有数字前缀
  3. 索引一致性检查 — 实际文件是否在 _index.json 中
  4. 生成检查报告

用法:
  python 脚本/self_check.py
"""

import json
import re
import sys
from datetime import datetime
from pathlib import Path
from collections import defaultdict

ROOT = Path(__file__).resolve().parent.parent
KNOWLEDGE_DIR = ROOT / "知识库"
INDEX_FILE = KNOWLEDGE_DIR / "_index.json"

ERRORS = []
WARNINGS = []
INFO = []

# 需要编号的目录（Key：子目录名，Value：该目录的描述）
NUMBERED_DIRS = {
    "游戏设计理论": "大文档体系",
    "UI设计理论": "UI/UX设计理论",
    "理论知识": "原子概念文档",
    "项目经验": "单项目经验",
}

# 不需要编号的目录/文件（元数据文件不要求编号）
SKIP_PATTERNS = (
    "_索引.md",
    "_差异清单.md",
    "_交叉引用索引.md",
    "_交叉引用维护规范.md",
    "更新日志.md",
    "README.md",
    "_index.json",
)


def error(msg):
    ERRORS.append(msg)
    print(f"  ❌ {msg}")


def warn(msg):
    WARNINGS.append(msg)
    print(f"  ⚠️  {msg}")


def info(msg):
    INFO.append(msg)
    print(f"  📌 {msg}")


def ok(msg):
    print(f"  ✅ {msg}")


def is_meta_file(filename: str) -> bool:
    """判断是否为元数据文件（不需要编号和索引的）"""
    if filename.startswith("_"):
        return True
    if filename in ("README.md", "更新日志.md"):
        return True
    return False


def has_number_prefix(filename: str) -> bool:
    """检查文件名是否有编号前缀，如 '01-xxx.md'"""
    return bool(re.match(r"^\d{2}-", filename))


def check_document_integrity():
    """
    检查1：文档存在性
    — 遍历知识库目录，对照 _index.json，找出缺失文件
    """
    print("\n📋 步骤1：文档存在性检查")
    orphaned_refs = []
    
    # 如果 _index.json 存在，用它做对照
    if INDEX_FILE.exists():
        index_data = json.loads(INDEX_FILE.read_text(encoding="utf-8"))
        for article in index_data.get("articles", []):
            fp = KNOWLEDGE_DIR / article["path"]
            if not fp.exists():
                orphaned_refs.append(article["path"])
                error(f"索引中的文件不存在（可能被误删）: {article['path']}")

        if not orphaned_refs:
            ok(f"索引中的 {len(index_data.get('articles', []))} 篇文章全部存在")
    else:
        warn("_index.json 不存在，跳过存在性检查，请先运行 generate_index.py")

    return orphaned_refs


def check_numbering():
    """
    检查2：编号规范性
    — 检查 NUMBERED_DIRS 中列出的目录，所有非元数据文件是否有编号前缀
    """
    print("\n🔢 步骤2：编号规范性检查")
    unnumbered_files = {}

    for subdir_name, description in NUMBERED_DIRS.items():
        subdir = KNOWLEDGE_DIR / subdir_name
        if not subdir.exists():
            warn(f"目录不存在: {subdir_name}/")
            continue

        files = sorted(
            f for f in subdir.rglob("*.md")
            if not is_meta_file(f.name) and f.parent == subdir
        )

        unnumbered = []
        for f in files:
            if not has_number_prefix(f.name):
                unnumbered.append(f.name)

        total = len(files)
        missing = len(unnumbered)
        if missing > 0:
            unnumbered_files[subdir_name] = unnumbered
            warn(f"{description} ({subdir_name}/): {missing}/{total} 篇缺少编号前缀")
            for fn in unnumbered:
                info(f"  未编号文件: {subdir_name}/{fn}")
        else:
            ok(f"{description} ({subdir_name}/): {total} 篇全部已编号")

    return unnumbered_files


def is_indexable_file(filename: str) -> bool:
    """判断文件是否应被索引收录（元数据文件除外）"""
    if filename.startswith("_"):
        return False
    if filename == "README.md":
        return False
    return True


def check_index_consistency():
    """
    检查3：索引一致性
    — 实际文件是否在 _index.json 中有记录
    """
    print("\n📚 步骤3：索引一致性检查")
    if not INDEX_FILE.exists():
        warn("_index.json 不存在，跳过索引一致性检查")
        return [], []

    index_data = json.loads(INDEX_FILE.read_text(encoding="utf-8"))
    indexed_paths = set()
    for article in index_data.get("articles", []):
        p = KNOWLEDGE_DIR / article["path"]
        if p.exists():
            indexed_paths.add(article["path"].replace("\\", "/"))

    actual_files = set()
    for f in sorted(KNOWLEDGE_DIR.rglob("*.md")):
        if not is_indexable_file(f.name):
            continue
        rel = str(f.relative_to(KNOWLEDGE_DIR)).replace("\\", "/")
        actual_files.add(rel)

    missing_from_index = sorted(actual_files - indexed_paths)
    orphaned_in_index = sorted(indexed_paths - actual_files)

    if missing_from_index:
        for fp in missing_from_index:
            warn(f"文件不在索引中: {fp}")
    else:
        ok("所有实际文件都在索引中")

    if orphaned_in_index:
        for fp in orphaned_in_index:
            error(f"索引引用不存在（可能被误删）: {fp}")
    else:
        ok("索引中没有孤立引用")

    return missing_from_index, orphaned_in_index


def check_subdir_structure():
    """
    检查4：子目录结构 — 检查编号目录的子目录文件是否也有编号
    """
    print("\n🗂️  步骤4：子目录结构检查")
    findings = {}

    for subdir_name in NUMBERED_DIRS:
        subdir = KNOWLEDGE_DIR / subdir_name
        if not subdir.exists():
            continue
        
        # 检查子目录
        for child_dir in sorted(subdir.iterdir()):
            if not child_dir.is_dir():
                continue
            if child_dir.name.startswith("_"):
                continue
                
            md_files = sorted(child_dir.glob("*.md"))
            meta_files = [f for f in md_files if is_meta_file(f.name)]
            content_files = [f for f in md_files if not is_meta_file(f.name)]
            
            if not content_files:
                continue
            
            unnumbered = [f.name for f in content_files if not has_number_prefix(f.name)]
            if unnumbered:
                rel_dir = f"{subdir_name}/{child_dir.name}"
                findings[rel_dir] = unnumbered
                warn(f"{rel_dir}/: {len(unnumbered)}/{len(content_files)} 篇缺少编号")
                for fn in unnumbered:
                    info(f"  未编号文件: {rel_dir}/{fn}")
            else:
                ok(f"{subdir_name}/{child_dir.name}/: 全部已编号")

    return findings


def generate_report(unnumbered_files, missing_from_index, orphaned_in_index):
    """生成总结报告"""
    print("\n" + "=" * 60)
    print("📊 自检报告")
    print("=" * 60)
    print(f"   错误: {len(ERRORS)}")
    print(f"   警告: {len(WARNINGS)}")
    print(f"   信息: {len(INFO)}")
    print()
    
    if ERRORS:
        print("❗ 错误项（必须修复）：")
        for e in ERRORS:
            print(f"   {e}")
        print()

    if WARNINGS:
        print("⚠️  警告项（建议修复）：")
        for w in WARNINGS:
            print(f"   {w}")
        print()

    if INFO:
        print("📌 详细信息：")
        for i in INFO:
            print(f"   {i}")
        print()

    # 生成修复建议
    print("💡 修复建议：")
    if unnumbered_files:
        print()
        print("  编号修复：可运行以下命令为文件添加编号前缀：")
        print()
        for dir_name, files in unnumbered_files.items():
            print(f"  📁 {dir_name}/")
            for i, fn in enumerate(files, 1):
                print(f"     {fn} → {i:02d}-{fn}")

    if missing_from_index:
        print()
        print("  索引修复：运行 python 脚本/generate_index.py 重建索引")

    print()
    print("=" * 60)
    
    summary = {
        "timestamp": datetime.now().isoformat(),
        "errors": len(ERRORS),
        "warnings": len(WARNINGS),
        "info": len(INFO),
        "has_issues": bool(ERRORS or WARNINGS),
        "details": {
            "missing_files": [e for e in ERRORS if "不存在" in e],
            "unnumbered_dirs": {
                d: len(fs) for d, fs in unnumbered_files.items()
            },
            "files_not_in_index": len(missing_from_index),
            "orphaned_index_refs": len(orphaned_in_index),
        }
    }
    
    return summary


def main():
    print("=" * 60)
    print("🔍 知识库自检")
    print(f"   路径: {KNOWLEDGE_DIR}")
    print(f"   时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 60)

    unnumbered_files = check_numbering()
    check_document_integrity()
    missing_from_index, orphaned_in_index = check_index_consistency()
    check_subdir_structure()

    summary = generate_report(unnumbered_files, missing_from_index, orphaned_in_index)
    
    # 输出 JSON 格式的摘要（供工作流解析）
    print(json.dumps({"summary": summary}, ensure_ascii=False, indent=2))
    
    return 1 if ERRORS else 0


if __name__ == "__main__":
    sys.exit(main())
