#!/usr/bin/env python3
"""
一致性校验器 — 检查工作区各组件之间的一致性。

用法:
  python 脚本/validate.py

检查项:
  1. 工作流定义 × _router.json — 所有被引用的工作流都存在
  2. 知识索引 × 实际文件 — _index.json 与实际文件一致
  3. 文件引用 — 所有 .md 中的文件路径引用有效
  4. _workspace.json — 基本配置完整
"""

import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ERRORS = []
WARNINGS = []


def error(msg):
    ERRORS.append(msg)
    print(f"  ❌ {msg}")


def warn(msg):
    WARNINGS.append(msg)
    print(f"  ⚠️  {msg}")


def ok(msg):
    print(f"  ✅ {msg}")


def check_router():
    """检查路由表引用的工作流是否存在"""
    print("\n📋 检查工作流路由表...")
    router_path = ROOT / "工作流" / "_router.json"
    if not router_path.exists():
        warn("_router.json 不存在")
        return
    
    router = json.loads(router_path.read_text(encoding="utf-8"))
    for rule in router.get("rules", []):
        wf_path = ROOT / rule["workflow"]
        if wf_path.exists():
            ok(f"工作流 '{rule['id']}' → {rule['workflow']}")
        else:
            error(f"工作流 '{rule['id']}' 引用的 {rule['workflow']} 不存在")


def check_knowledge_index():
    """检查知识索引与实际文件一致"""
    print("\n📚 检查知识索引...")
    index_path = ROOT / "知识库" / "_index.json"
    if not index_path.exists():
        warn("知识库/_index.json 不存在")
        return
    
    index = json.loads(index_path.read_text(encoding="utf-8"))
    
    # 收集索引中的路径
    indexed_paths = set()
    for article in index.get("articles", []):
        p = ROOT / "知识库" / article["path"]
        indexed_paths.add(str(p.relative_to(ROOT / "知识库")))
        if not p.exists():
            error(f"索引中的文件不存在: {article['path']}")
    
    # 收集实际文件
    actual_files = set()
    for f in (ROOT / "知识库").rglob("*.md"):
        if f.name.startswith("_") or f.name == "README.md":
            continue
        rel = str(f.relative_to(ROOT / "知识库"))
        actual_files.add(rel)
        if rel not in indexed_paths:
            warn(f"文件不在索引中: {rel}")
    
    # 检查索引路径是否都对应实际文件
    orphaned = indexed_paths - actual_files
    for p in orphaned:
        error(f"索引中的路径无对应文件: {p}")
    
    if not orphaned and indexed_paths == actual_files:
        ok(f"知识索引一致 ({len(indexed_paths)} 篇)")
    else:
        ok(f"索引: {len(indexed_paths)} 篇, 实际: {len(actual_files)} 篇")


def check_file_refs():
    """检查 .md 文件中的引用路径是否有效"""
    print("\n🔗 检查文件引用...")
    md_files = list(ROOT.rglob("*.md"))
    ref_count = 0
    bad_refs = 0
    
    # 匹配 [text](path) 和 ![text](path) 且不是 http 链接
    ref_pattern = re.compile(r"\[([^\]]*)\]\(([^)]+)\)")
    
    for md_file in md_files:
        content = md_file.read_text(encoding="utf-8")
        for match in ref_pattern.finditer(content):
            ref = match.group(2)
            # 忽略外部链接
            if ref.startswith(("http://", "https://", "mailto:")):
                continue
            # 忽略锚点
            if ref.startswith("#"):
                continue
            
            ref_count += 1
            # 解析相对路径
            ref_path = (md_file.parent / ref).resolve()
            if not ref_path.exists():
                bad_refs += 1
                error(f"断裂引用: {md_file.relative_to(ROOT)} → {ref}")
    
    if bad_refs == 0:
        ok(f"文件引用检查通过 ({ref_count} 个引用)")
    else:
        ok(f"检查了 {ref_count} 个引用, {bad_refs} 个断裂")


def check_workspace_config():
    """检查 _workspace.json"""
    print("\n⚙️  检查工作区配置...")
    config_path = ROOT / "_workspace.json"
    if not config_path.exists():
        error("_workspace.json 不存在")
        return
    
    config = json.loads(config_path.read_text(encoding="utf-8"))
    required = ["workspace_name", "workspace_type"]
    for field in required:
        if field not in config:
            error(f"_workspace.json 缺少字段: {field}")
    
    if config.get("workspace_name") == "你的项目名称":
        warn("workspace_name 还未修改")
    else:
        ok(f"workspace_name: {config['workspace_name']}")


DESIGN_ROUTER = ROOT / "工作流" / "设计路由.json"
PROMPTS_DIR = ROOT / "prompts"


def check_design_router_sync():
    """校验 设计路由.json 与 prompt 文件的步骤数是否一致"""
    print("\n📋 检查设计路由与 Prompt 同步...")
    if not DESIGN_ROUTER.exists():
        warn("设计路由.json 不存在，跳过")
        return

    router = json.loads(DESIGN_ROUTER.read_text(encoding="utf-8"))
    workflows = router.get("workflows", {})

    for prompt_file in sorted(PROMPTS_DIR.glob("*.prompt.md")):
        content = prompt_file.read_text(encoding="utf-8")
        
        # 找引擎约束行：提取 workflow 名称
        # 匹配 "的 `卡牌设计` 工作流"（中间可能有空格）
        m = re.search(r'的\s*[`\u201c]([^`\u201d]+)[`\u201d]\s*工作流', content)
        if not m:
            continue  # 没有引用设计路由的 prompt 跳过
        
        wf_name = m.group(1).strip()
        wf = workflows.get(wf_name)
        if not wf:
            error(f"[{prompt_file.name}] 引用工作流 '{wf_name}'，但设计路由.json 中未找到")
            continue
        
        router_step_count = len(wf.get("steps", []))
        
        # 统计 prompt 中的主步骤数（### N. 模式）
        # 排除 ### N.N 子步骤，只统计 ### N 主步骤
        prompt_step_count = len(re.findall(r'^###\s+(\d+)\.\s', content, re.MULTILINE))
        
        if prompt_step_count != router_step_count:
            error(
                f"[{prompt_file.name}] 步骤数不匹配: "
                f"prompt 有 {prompt_step_count} 步 (### N.)，"
                f"设计路由.json '{wf_name}' 有 {router_step_count} 步"
            )
        else:
            ok(f"[{prompt_file.name}] 引用了 '{wf_name}' — {router_step_count} 步")
    
    if not list(PROMPTS_DIR.glob("*.prompt.md")):
        warn("未找到 prompt 文件")


def main():
    print("=" * 50)
    print("🔍 工作区一致性校验")
    print(f"  路径: {ROOT}")
    print("=" * 50)
    
    check_router()
    check_knowledge_index()
    check_file_refs()
    check_workspace_config()
    check_design_router_sync()
    
    print("\n" + "=" * 50)
    print("📊 校验报告")
    print(f"   错误: {len(ERRORS)}")
    print(f"   警告: {len(WARNINGS)}")
    print("=" * 50)
    
    return 1 if ERRORS else 0


if __name__ == "__main__":
    sys.exit(main())
