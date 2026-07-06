#!/usr/bin/env python3
"""
生成 _dashboard.md — Obsidian 工作区仪表盘

用法:
  python 脚本/generate_dashboard.py

功能:
  - 读取 _state.json 获取工作流状态
  - 读取 _index.json 获取知识库统计（分类、标签、近期更新）
  - 扫描 记忆/决策/ 获取最近决策
  - 扫描知识库获取 draft 待处理项
  - 读取 _差异清单.md 获取待建原子文档数
  - 读取 _交叉引用索引.md 获取引用统计
  - 生成混合静态内容 + Dataview 查询的仪表盘

集成到工作流：
  - Agent 在会话开始/结束时自动运行此脚本
  - 也可在 Obsidian 中用 Templater 或手动触发
"""

import json
import sys
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
DASHBOARD = ROOT / "_dashboard.md"
STATE_FILE = ROOT / "工作流" / "_state.json"
INDEX_FILE = ROOT / "知识库" / "_index.json"
DECISIONS_DIR = ROOT / "记忆" / "决策"
SESSIONS_DIR = ROOT / "记忆" / "会话"
KNOWLEDGE_DIR = ROOT / "知识库"
DIFF_FILE = KNOWLEDGE_DIR / "_差异清单.md"
CREF_FILE = KNOWLEDGE_DIR / "_交叉引用索引.md"
GDD_DIR = ROOT / "GDD分析"

CATEGORY_ICONS = {
    "理论知识": "📖", "游戏设计理论": "📚", "UI设计理论": "🎨",
    "跨项目知识": "🔗", "项目经验": "🧪", "已验证经验": "✅",
}

IGNORE_CATS = {"根目录", "_原始文档归档", "_存档", "归纳文档"}


def load_json(path):
    if not path.exists():
        return {}
    return json.loads(path.read_text(encoding="utf-8"))


def get_workflow():
    s = load_json(STATE_FILE)
    if not s or s.get("status") == "idle":
        return {"status": "⏸️ 空闲", "workflow": "无", "current": None, "completed": [], "started": ""}

    wf_name = s.get("active_workflow", "未知")
    completed = s.get("completed_steps", [])
    current = s.get("current_step")
    raw = s.get("status", "unknown")
    label = {"active": "▶️ 进行中", "completed": "✅ 已完成", "idle": "⏸️ 空闲"}.get(raw, raw)

    step_names = {}
    wf_file = ROOT / "工作流" / f"{wf_name}.json"
    if wf_file.exists():
        try:
            for st in json.loads(wf_file.read_text(encoding="utf-8")).get("steps", []):
                n = st.get("name", "")
                step_names[st["id"]] = f"{st['id']} — {n}" if n else st["id"]
        except Exception:
            pass

    return {
        "status": label,
        "workflow": wf_name,
        "current": step_names.get(current, current) if current else None,
        "completed": [step_names.get(s, s) for s in completed],
        "started": s.get("started_at", "")[:10],
    }


def get_kb():
    idx = load_json(INDEX_FILE)
    if not idx:
        return {}
    arts = idx.get("articles", [])
    cats = {}
    for a in arts:
        p = a.get("path", "")
        cat = p.split("/")[0] if "/" in p else "根目录"
        if cat in IGNORE_CATS:
            continue
        if cat not in cats:
            cats[cat] = {"count": 0, "verified": 0, "draft": 0}
        c = cats[cat]
        c["count"] += 1
        if a.get("status") == "verified":
            c["verified"] += 1
        else:
            c["draft"] += 1

    recent = sorted(arts, key=lambda a: a.get("last_modified", ""), reverse=True)[:8]
    all_tags = idx.get("tags", {})
    top_tags = sorted(all_tags.items(), key=lambda x: -len(x[1]))[:10]

    return {
        "total": len(arts),
        "cats": cats,
        "recent": recent,
        "top_tags": top_tags,
        "generated": idx.get("generated_at", ""),
    }


def get_decisions(limit=5):
    if not DECISIONS_DIR.exists():
        return []
    out = []
    for f in sorted(DECISIONS_DIR.glob("*.md"), reverse=True):
        if f.name == ".gitkeep":
            continue
        text = f.read_text(encoding="utf-8")
        title = None
        time = ""
        for line in text.split("\n"):
            if line.startswith("# 决策") and "：" in line:
                title = line.split("：", 1)[-1].strip()
            elif line.startswith("- **时间**"):
                time = line.split(":", 1)[-1].strip()
        out.append({"file": f.name, "title": title or f.stem, "time": time})
        if len(out) >= limit:
            break
    return out


def get_inbox():
    draft = 0
    for f in KNOWLEDGE_DIR.rglob("*.md"):
        if f.name.startswith("_") or f.name == "README.md":
            continue
        c = f.read_text(encoding="utf-8")
        if c.startswith("---") and "status: draft" in c.split("---", 2)[1]:
            draft += 1
    return draft


def get_pending():
    if not DIFF_FILE.exists():
        return 0
    text = DIFF_FILE.read_text(encoding="utf-8")
    in_sec = False
    n = 0
    for line in text.split("\n"):
        if "大文档独有" in line:
            in_sec = True
        elif "原子文档独有" in line:
            in_sec = False
        elif in_sec and "待建原子文档" in line:
            n += 1
    return n


def cref_count():
    if not CREF_FILE.exists():
        return 0
    return sum(1 for l in CREF_FILE.read_text(encoding="utf-8").split("\n") if "|" in l and "→" in l)


def get_gdd():
    if not GDD_DIR.exists():
        return []
    return sorted(GDD_DIR.glob("*.md"), reverse=True)[:3]


def get_sessions():
    if not SESSIONS_DIR.exists():
        return []
    return [f.stem for f in sorted(SESSIONS_DIR.glob("*.md"), reverse=True) if f.name != ".gitkeep"][:5]


def fmt_time(s):
    return s[:10] if s else "—"


# ═══════════════════════════════════════════════
#  GENERATE
# ═══════════════════════════════════════════════

def build():
    wf = get_workflow()
    kb = get_kb()
    decisions = get_decisions()
    inbox = get_inbox()
    sessions = get_sessions()
    pending = get_pending()
    cref = cref_count()
    gdds = get_gdd()
    today = datetime.now().strftime("%Y-%m-%d")

    L = []

    # Frontmatter
    L.append("---")
    L.append("title: 工作区仪表盘")
    L.append("tags: [meta, dashboard]")
    L.append("created: 2026-06-03")
    L.append("status: verified")
    L.append("aliases: [仪表盘, 首页, Home, 总览]")
    L.append("---")
    L.append("")
    L.append("# 📊 工作区仪表盘")
    L.append("")
    L.append(f"> 📅 更新于 `{today}` · 刷新: `python 脚本/generate_dashboard.py`")
    L.append("")

    # ═══ 区块 1: 工作流 ═══
    L.append("---")
    L.append("")
    L.append("## 🔄 工作流状态")
    L.append("")
    L.append(f"| 字段 | 值 |")
    L.append(f"|:----|:---|")
    L.append(f"| **状态** | {wf['status']} |")
    L.append(f"| **工作流** | {wf['workflow']} |")
    L.append(f"| **当前步骤** | {wf['current'] or '—'} |")
    L.append(f"| **已完成** | {len(wf['completed'])} 步 |")
    L.append(f"| **开始于** | {wf['started'] or '—'} |")
    L.append("")
    if wf["completed"]:
        L.append("<details><summary>展开查看完成步骤</summary>\n")
        for s in wf["completed"]:
            L.append(f"- ✅ {s}")
        L.append("\n</details>")
    L.append("")

    # ═══ 区块 2: 知识库 ═══
    L.append("---")
    L.append("")
    L.append("## 📚 知识库")
    L.append("")
    L.append(f"**{kb['total']} 篇** · 索引 `{fmt_time(kb.get('generated',''))}` · 交叉引用 {cref} 条")
    L.append("")
    L.append("| 分类 | 篇数 | ✅ 已验证 | 📝 草稿 |")
    L.append("|:----|:----:|:--------:|:------:|")
    for cat in sorted(kb.get("cats", {}).keys()):
        c = kb["cats"][cat]
        icon = CATEGORY_ICONS.get(cat, "📁")
        L.append(f"| {icon} {cat} | {c['count']} | {c['verified']} | {c['draft']} |")
    L.append("")

    if kb.get("recent"):
        L.append("**最近更新：**\n")
        for a in kb["recent"]:
            icon = "✅" if a.get("status") == "verified" else "📝"
            L.append(f"- {icon} `{a['path']}` — {a['title']} ({fmt_time(a.get('last_modified',''))})")
    L.append("")

    if kb.get("top_tags"):
        L.append("**热门标签：**\n")
        L.append("> " + " · ".join(f"`#{t}` ({len(n)}篇)" for t, n in kb["top_tags"]))
    L.append("")

    # ═══ 区块 3: 收件箱 ═══
    L.append("---")
    L.append("")
    L.append("## 📥 待处理")
    L.append("")
    L.append(f"| 项目 | 数量 |")
    L.append(f"|:----|:----:|")
    L.append(f"| 📝 草稿文档 (`draft`) | {inbox} 篇 |")
    L.append(f"| 📋 待建原子文档 | {pending} 项 |")
    L.append("")
    L.append("> 💡 添加 `#inbox` 标签 → 下次会话 Agent 自动入库")
    L.append("")

    # ═══ 区块 4: 决策 ═══
    L.append("---")
    L.append("")
    L.append("## 📝 最近决策")
    L.append("")
    if decisions:
        L.append("| 日期 | 决策 |")
        L.append("|:----|:-----|")
        for d in decisions:
            L.append(f"| {d['time'][:10]} | [[记忆/决策/{d['file']}|{d['title']}]] |")
    else:
        L.append("> 暂无决策记录")
    L.append("")
    if gdds:
        L.append("**GDD 分析：**\n")
        for f in gdds:
            L.append(f"- 📄 [[GDD分析/{f.name}|{f.stem}]]")
    L.append("")

    # ═══ 区块 5: 会话 ═══
    L.append("---")
    L.append("")
    L.append("## 💬 最近会话")
    L.append("")
    if sessions:
        for s in sessions:
            L.append(f"- 📅 [[记忆/会话/{s}.md|{s}]]")
    else:
        L.append("> 暂无会话记录")
    L.append("")

    # ═══ 区块 6: 快捷入口 ═══
    L.append("---")
    L.append("")
    L.append("## 🔗 快捷入口")
    L.append("")
    L.append("| 入口 | 说明 |")
    L.append("|:----|:-----|")
    L.append("| [[_index.md\|🏠 入口]] | 工作区总览 |")
    L.append("| [[知识库/_索引.md\|📚 知识库]] | 分类导航 |")
    L.append("| [[知识库/_差异清单.md\|📋 差异清单]] | 大文档 ↔ 原子文档对照 |")
    L.append("| [[知识库/_交叉引用索引.md\|🔗 交叉引用]] | 知识关联矩阵 |")
    L.append("| [[记忆/决策/\|📝 决策]] | 设计决策归档 |")
    L.append("| [[记忆/会话/\|💬 会话]] | 会话摘要归档 |")
    L.append("| [[文档/ARCHITECTURE.md\|🏗️ 架构]] | 工作区架构 |")
    L.append("| [[文档/QUICKSTART.md\|🚀 快速开始]] | 使用指引 |")
    L.append("")

    # ═══ 区块 7: Dataview ═══
    L.append("---")
    L.append("---")
    L.append("")
    L.append("## ⚡ Dataview 实时查询")
    L.append("")
    L.append("> 需 [Dataview 插件](https://blacksmithgu.github.io/obsidian-dataview/)，自动读取 Obsidian 索引。")
    L.append("")

    L.append("### 📝 全部草稿")
    L.append("```dataview")
    L.append('TABLE file.link AS "文档", tags AS "标签", date(created) AS "创建"')
    L.append('FROM "知识库"')
    L.append('WHERE status = "draft"')
    L.append("SORT created DESC")
    L.append("```\n")

    L.append("### 📥 Inbox")
    L.append("```dataview")
    L.append('TABLE file.link AS "文档", date(created) AS "创建"')
    L.append('FROM "知识库"')
    L.append('WHERE contains(tags, "inbox")')
    L.append("```\n")

    L.append("### 🔄 最近修改")
    L.append("```dataview")
    L.append('TABLE file.link AS "文档", date(file.mtime) AS "修改时间"')
    L.append('FROM "知识库"')
    L.append("SORT file.mtime DESC")
    L.append("LIMIT 15")
    L.append("```\n")

    L.append("### 🏷️ 按标签分组")
    L.append("```dataview")
    L.append("TABLE rows.file.link AS \"文档\", rows.status AS \"状态\"")
    L.append('FROM "知识库"')
    L.append("GROUP BY tags")
    L.append("```\n")

    L.append(f"---\n*{today} · 由 `脚本/generate_dashboard.py` 生成*\n")

    DASHBOARD.write_text("\n".join(L), encoding="utf-8")

    print(f"✅ 仪表盘已生成: {DASHBOARD}")
    print(f"   工作流: {wf['status']} · 知识库: {kb['total']} 篇 · 待处理: {inbox} 草稿 · 待建: {pending} 项")


if __name__ == "__main__":
    sys.exit(build())
