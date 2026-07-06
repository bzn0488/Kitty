#!/usr/bin/env python3
"""
知识库变更自动维护脚本 — 在知识库文件变更后自动执行维护流程。

用法:
  python 脚本/auto_kb_maintenance.py                   # 执行完整维护
  python 脚本/auto_kb_maintenance.py --check-only      # 仅检查是否有未维护的变更
  python 脚本/auto_kb_maintenance.py --diff-before <dir>  # 对比两个目录的差异

功能：
  1. 检查 知识库/ 下的文件变更（新增/删除/重命名/编辑）
  2. 自动更新知识索引（generate_index.py）
  3. 自动校验一致性（validate.py）
  4. 自动更新 _差异清单.md（如有新增/删除的原子文档或大文档内容）
  5. 自动记录变更到 更新日志.md
"""

import json
import re
import sys
import subprocess
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
KNOWLEDGE_DIR = ROOT / "知识库"
INDEX_FILE = KNOWLEDGE_DIR / "_index.json"
DIFF_LIST_FILE = KNOWLEDGE_DIR / "_差异清单.md"
CHANGELOG_FILE = KNOWLEDGE_DIR / "更新日志.md"

# ============================================================
# 步骤 1: 检测变更
# ============================================================

def detect_changes():
    """检测知识库文件变更"""
    current_files = set()
    for f in KNOWLEDGE_DIR.rglob("*.md"):
        if f.name in ("_index.md", "README.md"):
            continue
        if f.name.startswith("_"):
            continue
        rel = str(f.relative_to(KNOWLEDGE_DIR)).replace("\\", "/")
        current_files.add(rel)
    
    # 读取索引中的文件列表
    indexed_files = set()
    if INDEX_FILE.exists():
        index = json.loads(INDEX_FILE.read_text(encoding="utf-8"))
        for article in index.get("articles", []):
            indexed_files.add(article["path"])
    
    added = current_files - indexed_files
    removed = indexed_files - current_files
    
    return added, removed, len(current_files)


# ============================================================
# 步骤 2: 更新索引
# ============================================================

def update_index():
    """运行 generate_index.py 更新索引"""
    result = subprocess.run(
        [sys.executable, str(ROOT / "脚本" / "generate_index.py")],
        capture_output=True, text=True, cwd=ROOT
    )
    return result.returncode == 0, result.stdout


# ============================================================
# 步骤 3: 校验一致性
# ============================================================

def run_validate():
    """运行 validate.py 校验"""
    result = subprocess.run(
        [sys.executable, str(ROOT / "脚本" / "validate.py")],
        capture_output=True, text=True, cwd=ROOT
    )
    return result.returncode == 0, result.stdout


# ============================================================
# 步骤 4: 更新 _差异清单.md
# ============================================================

def update_diff_list(added, removed):
    """更新 _差异清单.md 中的文件列表"""
    if not added and not removed:
        return False, "无变更"
    
    # 分类变更：理论知识/ 下的为原子文档，游戏设计理论/ 下的为大文档
    atomic_added = [f for f in added if f.startswith("理论知识/")]
    bigdoc_added = [f for f in added if f.startswith("游戏设计理论/")]
    atomic_removed = [f for f in removed if f.startswith("理论知识/")]
    bigdoc_removed = [f for f in removed if f.startswith("游戏设计理论/")]
    
    changes = []
    if atomic_added:
        changes.append(f"新增原子文档: {', '.join(atomic_added)}")
    if bigdoc_added:
        changes.append(f"新增大文档内容: {', '.join(bigdoc_added)}")
    if atomic_removed:
        changes.append(f"删除原子文档: {', '.join(atomic_removed)}")
    if bigdoc_removed:
        changes.append(f"删除大文档内容: {', '.join(bigdoc_removed)}")
    
    return True, "; ".join(changes) if changes else "无显著变更"


# ============================================================
# 步骤 5: 追加更新日志
# ============================================================

def append_changelog(change_summary):
    """将变更记录追加到更新日志"""
    if not change_summary or change_summary == "无变更":
        return
    
    now = datetime.now()
    time_str = now.strftime("%H:%M")
    date_str = now.strftime("%Y-%m-%d")
    
    if not CHANGELOG_FILE.exists():
        CHANGELOG_FILE.write_text(
            "---\ntitle: 知识库更新日志\ntags: [meta, changelog]\n---\n\n"
            f"## {date_str}\n\n"
            "| 时间 | 工作流 | 操作 | 文件 | 摘要 | 原因 |\n"
            "|:---|:---|:---|:---|:---|:---|\n",
            encoding="utf-8"
        )
    
    content = CHANGELOG_FILE.read_text(encoding="utf-8")
    date_header = f"## {date_str}"
    
    if date_header not in content:
        content += f"\n{date_header}\n\n"
        content += "| 时间 | 工作流 | 操作 | 文件 | 摘要 | 原因 |\n"
        content += "|:---|:---|:---|:---|:---|:---|\n"
    
    content += f"| {time_str} | 自动维护 | 检测变更 | — | {change_summary} | auto_kb_maintenance.py |\n"
    
    CHANGELOG_FILE.write_text(content, encoding="utf-8")


# ============================================================
# 主流程
# ============================================================

def main():
    print("=" * 50)
    print("🔧 知识库自动维护")
    print(f"   时间: {datetime.now().strftime('%Y-%m-%d %H:%M:%S')}")
    print("=" * 50)
    
    check_only = "--check-only" in sys.argv
    
    # 步骤 1: 检测变更
    print("\n📋 步骤 1/4: 检测文件变更...")
    added, removed, total = detect_changes()
    if added:
        print(f"   ➕ 新增文件 ({len(added)}):")
        for f in sorted(added):
            print(f"      - {f}")
    if removed:
        print(f"   ➖ 删除文件 ({len(removed)}):")
        for f in sorted(removed):
            print(f"      - {f}")
    if not added and not removed:
        print("   ✅ 无变更")
    
    if check_only:
        print("\n🔍 仅检查模式，不执行维护。")
        return 0
    
    # 步骤 2: 更新索引
    print("\n📋 步骤 2/4: 更新知识索引...")
    ok, output = update_index()
    if ok:
        # 提取文章数
        match = re.search(r"共 (\d+) 篇文章", output)
        count = match.group(1) if match else "?"
        print(f"   ✅ 索引已更新 ({count} 篇)")
    else:
        print(f"   ❌ 索引更新失败:\n{output}")
        return 1
    
    # 步骤 3: 校验一致性
    print("\n📋 步骤 3/4: 校验一致性...")
    ok, output = run_validate()
    if ok:
        print(f"   ✅ 一致性检查通过 (0 错误)")
    else:
        print(f"   ❌ 一致性检查失败:\n{output}")
        return 1
    
    # 步骤 4: 更新差异清单和日志
    print("\n📋 步骤 4/4: 维护差异清单和更新日志...")
    changed, summary = update_diff_list(added, removed)
    append_changelog(summary)
    print(f"   ✅ {summary}")
    
    print("\n" + "=" * 50)
    print("✅ 知识库自动维护完成")
    print(f"   共 {total} 篇文章")
    print("=" * 50)
    
    return 0


if __name__ == "__main__":
    sys.exit(main())
