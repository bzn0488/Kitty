#!/usr/bin/env python3
"""
知识索引生成器 — 扫描 知识库/ 目录，自动生成 _index.json。
同时支持生成项目 _索引.md。

用法:
  python 脚本/generate_index.py                     # 生成知识库索引
  python 脚本/generate_index.py --project 家族纪元   # 生成项目索引
  python 脚本/generate_index.py --all                # 生成知识库 + 所有项目索引
"""

import json
import os
import re
import sys
from datetime import datetime
from pathlib import Path

# 项目根目录（脚本位于 _共享资源/脚本/ 下）
ROOT = Path(__file__).resolve().parent.parent  # -> _共享资源/
TEMPLATE_ROOT = ROOT.parent                    # -> 模板根目录
KNOWLEDGE_DIR = ROOT / "知识库"
INDEX_FILE = KNOWLEDGE_DIR / "_index.json"


def extract_frontmatter(content):
    """提取 YAML frontmatter"""
    match = re.match(r"^---\s*\n(.*?)\n---", content, re.DOTALL)
    if not match:
        return {}
    
    yaml_text = match.group(1)
    meta = {}
    for line in yaml_text.strip().split("\n"):
        if ":" in line:
            key, _, value = line.partition(":")
            key = key.strip()
            value = value.strip()
            if value.startswith("[") and value.endswith("]"):
                value = [v.strip().strip("'\"") for v in value[1:-1].split(",")]
            meta[key] = value
    return meta


def get_file_metadata(filepath, base_dir):
    """获取文件元数据（相对于 base_dir）"""
    rel_path = filepath.relative_to(base_dir)
    content = filepath.read_text(encoding="utf-8")
    frontmatter = extract_frontmatter(content)
    
    title_match = re.search(r"^#\s+(.+)$", content, re.MULTILINE)
    title = title_match.group(1) if title_match else filepath.stem
    
    return {
        "path": str(rel_path).replace("\\", "/"),
        "title": frontmatter.get("title", title),
        "desc": frontmatter.get("description", frontmatter.get("desc", "")),
        "tags": frontmatter.get("tags", []),
        "created": frontmatter.get("created", None),
        "status": frontmatter.get("status", "draft"),
        "size": len(content),
    }


def scan_knowledge():
    """扫描 知识库/ 目录"""
    articles = []
    tags = {}
    
    for filepath in sorted(KNOWLEDGE_DIR.rglob("*.md")):
        if filepath.name.startswith("_") or filepath.name == "README.md":
            continue
        meta = get_file_metadata(filepath, KNOWLEDGE_DIR)
        articles.append(meta)
        for tag in meta["tags"]:
            if tag not in tags:
                tags[tag] = []
            tags[tag].append(meta["path"])
    
    return articles, tags


def generate_project_index(project_name):
    """生成项目的 _索引.md"""
    project_dir = TEMPLATE_ROOT / project_name
    if not project_dir.is_dir():
        print(f"❌ 项目目录不存在: {project_dir}")
        return False
    
    # 扫描项目下的所有 .md 和 .py 文件
    files_by_dir = {}
    for filepath in sorted(project_dir.rglob("*")):
        if filepath.is_dir():
            continue
        if filepath.name.startswith("_") and filepath.suffix == ".md":
            # 收集索引/配置类文件
            if filepath.name in ("_索引.md", "_项目配置.md", "_系统索引.md", "_workspace.json"):
                continue
        if filepath.suffix not in (".md", ".py", ".json", ".canvas"):
            continue
        # 跳过隐藏目录
        parts = filepath.relative_to(project_dir).parts
        if any(p.startswith(".") or p.startswith("__") for p in parts):
            continue
        # 跳过 venv、node_modules 等
        if any(p in str(filepath) for p in [".venv", "node_modules", "__pycache__", ".git"]):
            continue
        
        rel = filepath.relative_to(project_dir)
        parent = str(rel.parent) if rel.parent != "." else ""
        
        meta = {}  # desc only from frontmatter
        if filepath.suffix == ".md":
            content = filepath.read_text(encoding="utf-8", errors="ignore")
            fm = extract_frontmatter(content)
            meta["desc"] = fm.get("description", fm.get("desc", ""))
            title_match = re.search(r"^#\s+(.+)$", content, re.MULTILINE)
            meta["title"] = fm.get("title", title_match.group(1) if title_match else filepath.stem)
        else:
            meta["title"] = filepath.stem
            meta["desc"] = ""
        
        key = parent if parent else "."
        if key not in files_by_dir:
            files_by_dir[key] = []
        files_by_dir[key].append({
            "name": filepath.name,
            "title": meta["title"],
            "desc": meta["desc"],
        })
    
    # 生成 Markdown
    lines = [
        f"# {project_name} — 文件索引",
        "",
        f"> 由 `脚本/generate_index.py --project {project_name}` 自动生成",
        f"> 更新于 {datetime.now().strftime('%Y-%m-%d %H:%M')}",
        "",
    ]
    
    # 按目录分组输出
    dir_order = sorted(files_by_dir.keys(), key=lambda k: (k != ".", k))
    for key in dir_order:
        files = files_by_dir[key]
        display = "根目录" if key == "." else key
        lines.append(f"## {display}")
        lines.append("")
        for f in files:
            path_str = f"{key}/{f['name']}" if key != "." else f['name']
            if f["desc"]:
                lines.append(f"- `{path_str}` — {f['desc']}")
            else:
                lines.append(f"- `{path_str}`")
        lines.append("")
    
    index_path = project_dir / "_索引.md"
    index_path.write_text("\n".join(lines), encoding="utf-8")
    print(f"✅ 项目索引已生成: {index_path}")
    print(f"   共 {sum(len(v) for v in files_by_dir.values())} 个文件")
    return True


def main():
    if "--help" in sys.argv or "-h" in sys.argv:
        print(__doc__)
        return 0
    
    if "--all" in sys.argv:
        # 知识库索引
        print("🔍 扫描 知识库/ 目录...")
        articles, tags = scan_knowledge()
        index = {
            "description": "知识库索引（由 脚本/generate_index.py 自动生成）",
            "generated_at": datetime.now().isoformat(),
            "total_articles": len(articles),
            "articles": articles,
            "tags": {k: v for k, v in sorted(tags.items())},
        }
        INDEX_FILE.write_text(json.dumps(index, ensure_ascii=False, indent=2), encoding="utf-8")
        print(f"✅ 知识库索引: {INDEX_FILE} ({len(articles)} 篇)")
        
        # 项目索引
        projects = ["家族纪元", "荒野与曙光", "增量自走棋", "大富翁"]
        for p in projects:
            if (TEMPLATE_ROOT / p).is_dir():
                generate_project_index(p)
        
        # 共享资源能力清单
        import subprocess
        cap_script = ROOT / "脚本" / "_collect_capabilities.py"
        if cap_script.exists():
            subprocess.run([sys.executable, str(cap_script)], check=True)
        
        return 0
    
    if "--project" in sys.argv:
        idx = sys.argv.index("--project") + 1
        if idx >= len(sys.argv):
            print("❌ 请指定项目名称，如 --project 家族纪元")
            return 1
        project_name = sys.argv[idx]
        if generate_project_index(project_name):
            return 0
        return 1
    
    # 默认行为：生成知识库索引
    print("🔍 扫描 知识库/ 目录...")
    articles, tags = scan_knowledge()
    
    index = {
        "description": "知识库索引（由 脚本/generate_index.py 自动生成）",
        "generated_at": datetime.now().isoformat(),
        "total_articles": len(articles),
        "articles": articles,
        "tags": {k: v for k, v in sorted(tags.items())},
    }
    
    INDEX_FILE.write_text(json.dumps(index, ensure_ascii=False, indent=2), encoding="utf-8")
    
    print(f"✅ 索引已生成: {INDEX_FILE}")
    print(f"   共 {len(articles)} 篇文章, {len(tags)} 个标签")
    
    if tags:
        print("\n📑 标签统计:")
        for tag, paths in sorted(tags.items()):
            print(f"   #{tag}: {len(paths)} 篇")
    
    return 0


if __name__ == "__main__":
    sys.exit(main())
