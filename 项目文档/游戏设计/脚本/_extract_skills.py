#!/usr/bin/env python3
"""
Ctx2Skill 自动技能提取脚本

扫描所有 .memory.md 文件，聚类相似条目，提示可提取的技能。
还支持从决策日志中提取重复出现的失败模式。

用法:
  python _extract_skills.py                    # 扫描全部
  python _extract_skills.py --scan memory      # 只扫 .memory.md
  python _extract_skills.py --scan decisions   # 只扫决策日志
  python _extract_skills.py --prompt           # 输出提取建议（供 Agent 参考）
"""

import json
import re
import sys
from pathlib import Path
from collections import defaultdict

# 路径
SHARED = Path(__file__).parent.parent
MEMORY_DIRS = [
    SHARED / "prompts",           # 全局 .memory.md
    SHARED.parent / ".github" / "prompts",
]
DECISION_LOG = SHARED.parent / "_配置" / "_决策日志.md"
SKILL_DIR = SHARED / "技能"
REGISTRY_FILE = SKILL_DIR / "_registry.json"
WORKFLOW_DIR = SHARED / "工作流"
FAILURE_DB = WORKFLOW_DIR / "_经验库" / "失败模式.json"


def scan_memory_files():
    """扫描所有 .memory.md 文件，提取经验和教训"""
    entries = []
    for d in MEMORY_DIRS:
        if not d.exists():
            continue
        for f in d.glob("*.memory.md"):
            text = f.read_text("utf-8", errors="ignore")
            name = f.stem.replace(".memory", "")
            # 提取所有 ## 日期 下的内容
            sections = re.split(r"^##\s+", text, flags=re.MULTILINE)
            for sec in sections[1:]:  # 跳过头部
                lines = sec.strip().split("\n")
                date = lines[0].strip()
                content = "\n".join(lines[1:]).strip()
                if content:
                    entries.append({
                        "source": f".memory.md ({name})",
                        "date": date,
                        "content": content[:200],
                    })
    return entries


def scan_decision_log():
    """从决策日志中提取重复模式"""
    if not DECISION_LOG.exists():
        return []
    text = DECISION_LOG.read_text("utf-8", errors="ignore")
    entries = re.split(r"^##\s+", text, flags=re.MULTILINE)
    patterns = []
    for entry in entries[1:]:
        lines = entry.strip().split("\n")
        title = lines[0].strip()
        body = "\n".join(lines[1:])
        # 提取触发和方案
        trigger = ""
        solution = ""
        for line in lines:
            if line.strip().startswith("- **触发**"):
                trigger = line.split("**：")[-1].strip() if "**：" in line else ""
            elif line.strip().startswith("- **方案**"):
                solution = line.split("**：")[-1].strip() if "**：" in line else ""
        patterns.append({
            "title": title,
            "trigger": trigger[:100],
            "solution": solution[:100],
        })
    return patterns


def scan_failure_db():
    """扫描失败经验库"""
    if not FAILURE_DB.exists():
        return []
    db = json.loads(FAILURE_DB.read_text("utf-8", errors="ignore"))
    return db.get("failures", [])


def find_similar_entries(entries):
    """基于关键词聚类的简单相似条目检测"""
    clusters = defaultdict(list)
    # 关键词分组
    keywords = [
        ("json/格式", ["json", "格式", "语法", "解析", "转义"]),
        ("tools/工具", ["tools", "工具", "缺少", "遗漏"]),
        ("步骤/流程", ["步骤", "顺序", "跳步", "路由"]),
        ("路径/引用", ["路径", "引用", "404", "未找到"]),
        ("prompt/提示", ["prompt", "提示", "描述性", "约束"]),
        ("数据/字段", ["字段", "数据", "缺失", "为空"]),
        ("自检/验证", ["自检", "验证", "检查", "对弈"]),
        ("命名/统一", ["命名", "统一", "不一致"]),
    ]
    for entry in entries:
        text = f"{entry.get('title', '')} {entry.get('content', '')} {entry.get('reason', '')} {entry.get('trigger', '')}".lower()
        for cluster_name, kws in keywords:
            if any(kw in text for kw in kws):
                clusters[cluster_name].append(entry)
                break
        else:
            clusters["其他"].append(entry)
    return clusters


def generate_skill_suggestions(clusters):
    """根据聚类结果生成技能提取建议"""
    suggestions = []
    for cluster_name, items in sorted(clusters.items()):
        if len(items) >= 2:
            suggestions.append({
                "cluster": cluster_name,
                "count": len(items),
                "sources": [i.get("source", i.get("title", "?")) for i in items[:5]],
                "suggested_skill": f"是否从 {len(items)} 条相关经验中提取通用技能？"
            })
    return suggestions


def prompt_suggestions(suggestions):
    """输出格式化的提取建议供 Agent 参考"""
    if not suggestions:
        print("✅ 未发现可提取的技能模式")
        return

    print(f"\n{'='*60}")
    print("🔍 Ctx2Skill 技能提取建议")
    print(f"{'='*60}\n")

    for s in sorted(suggestions, key=lambda x: -x["count"]):
        print(f"📌 主题: {s['cluster']} ({s['count']} 条相关条目)")
        for src in s["sources"][:3]:
            print(f"   · {src}")
        print(f"   💡 {s['suggested_skill']}")
        print()

    print(f"\n使用 `_collect_capabilities.py` 更新能力清单。")


def main():
    scan_mode = "all"
    if "--scan" in sys.argv:
        idx = sys.argv.index("--scan")
        if idx + 1 < len(sys.argv):
            scan_mode = sys.argv[idx + 1]

    all_entries = []

    if scan_mode in ("all", "memory"):
        mem = scan_memory_files()
        print(f"📄 .memory.md 条目: {len(mem)}")
        all_entries.extend(mem)

    if scan_mode in ("all", "decisions"):
        dec = scan_decision_log()
        print(f"📋 决策日志条目: {len(dec)}")
        all_entries.extend(dec)

    if scan_mode in ("all", "failures"):
        fail = scan_failure_db()
        print(f"⚠️  失败模式记录: {len(fail)}")
        all_entries.extend(fail)

    if not all_entries:
        print("没有扫描到任何条目")
        return

    clusters = find_similar_entries(all_entries)
    suggestions = generate_skill_suggestions(clusters)

    print(f"\n聚类结果:")
    for name, items in sorted(clusters.items()):
        print(f"  {name}: {len(items)} 条")

    if "--prompt" in sys.argv:
        prompt_suggestions(suggestions)
    else:
        print(f"\n💡 运行 `--prompt` 查看技能提取建议")


if __name__ == "__main__":
    main()
