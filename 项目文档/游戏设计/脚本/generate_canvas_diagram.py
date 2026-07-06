#!/usr/bin/env python3
"""
生成 Obsidian Canvas 图表 — 布局清晰、颜色语义化

用法:
  # 1. 系统关系图（GDD 分析第2步）
  python 脚本/generate_canvas_diagram.py system-map \\
    --title "增量自走棋-系统关系" \\
    --nodes '[
      {"id":"经济","label":"💰 经济系统","desc":"利息·连胜·出售","group":"economic"},
      {"id":"商店","label":"🏪 商店系统","desc":"刷新·锁定·消耗","group":"economic"},
      {"id":"战斗","label":"⚔️ 战斗系统","desc":"自动对战·站位·AI","group":"combat"},
      {"id":"成长","label":"⬆️ 成长系统","desc":"外成长·内成长·升级","group":"progression"},
      {"id":"协同","label":"🔗 协同系统","desc":"种族·职业·层级","group":"combat"},
      {"id":"进度","label":"📈 进度系统","desc":"关卡·难度·奖励","group":"progression"}
    ]' \\
    --edges '[
      {"from":"经济","to":"商店","label":"消耗"},
      {"from":"经济","to":"成长","label":"消耗"},
      {"from":"商店","to":"经济","label":"反馈"},
      {"from":"商店","to":"战斗","label":"上阵"},
      {"from":"商店","to":"协同","label":"购买"},
      {"from":"成长","to":"战斗","label":"数值"},
      {"from":"协同","to":"战斗","label":"加成"},
      {"from":"战斗","to":"进度","label":"推进"}
    ]'

  # 2. 冲突热力图
  python 脚本/generate_canvas_diagram.py conflict-map ...

  # 3. 自定义
  python 脚本/generate_canvas_diagram.py custom ...
"""

import json
import sys
import os
from pathlib import Path
from datetime import datetime

ROOT = Path(__file__).resolve().parent.parent

# ═══════════════════════════════════════════════
#  语义颜色方案
# ═══════════════════════════════════════════════

# 节点分组 → 颜色 (Obsidian Canvas 色号 1~6)
GROUP_COLORS = {
    "economic":    {"color": "1", "bg": "#e06c75"},   # 红色系 — 经济/资源
    "combat":      {"color": "3", "bg": "#e5c07b"},   # 黄色系 — 战斗/对抗
    "progression": {"color": "2", "bg": "#d19a66"},   # 橙色系 — 成长/进度
    "social":      {"color": "5", "bg": "#56b6c2"},   # 青色系 — 社交/协同
    "narrative":   {"color": "6", "bg": "#c678dd"},   # 紫色系 — 叙事/体验
    "system":      {"color": "4", "bg": "#98c379"},   # 绿色系 — 系统/机制
    "conflict":    {"color": "1", "bg": "#e06c75"},   # 红色 — 冲突
    "info":        {"color": "7", "bg": "#abb2bf"},   # 灰色 — 信息/标注
    "system":      {"color": "4", "bg": "#98c379"},   # 绿色 — GDD 系统
    "theory":      {"color": "6", "bg": "#c678dd"},   # 紫色 — 知识理论
}

# 连线颜色 — 语义化
EDGE_COLORS = {
    "消耗":   "#e06c75",
    "产出":   "#98c379",
    "反馈":   "#61afef",
    "加成":   "#c678dd",
    "推进":   "#56b6c2",
    "上阵":   "#e5c07b",
    "购买":   "#d19a66",
    "冲突":   "#e06c75",
    "引用":   "#61afef",
    "支撑":   "#98c379",
    "default": "#5c6370",
}

# 知识映射 — 匹配类型的颜色
MAPPING_COLORS = {
    "✅": "#98c379",       # 理论验证 — 绿色
    "➕": "#61afef",       # 可补充扩展 — 蓝色
    "⚠️": "#e5c07b",      # 理论矛盾 — 黄色
    "❌": "#e06c75",      # 无理论支撑 — 红色
    "default": "#5c6370",
}

# ═══════════════════════════════════════════════
#  布局引擎
# ═══════════════════════════════════════════════

def auto_layout(nodes, edges):
    """
    自动计算节点位置 — 分层流式布局

    策略：
      1. 按 group 分组，每组放在同一行
      2. 根据 edges 确定层级（入度=0 的放左边，出度=0 的放右边）
      3. 同层内水平均匀分布
    """
    NODE_W, NODE_H = 260, 120
    PAD_X, PAD_Y = 80, 60
    GROUP_GAP = 60  # 组间额外间距

    node_ids = [n["id"] for n in nodes]
    node_map = {n["id"]: n for n in nodes}

    # 计算入度/出度
    in_deg = {nid: 0 for nid in node_ids}
    out_deg = {nid: 0 for nid in node_ids}
    for e in edges:
        if e["from"] in in_deg:
            out_deg[e["from"]] += 1
        if e["to"] in in_deg:
            in_deg[e["to"]] += 1

    # 按 group 分组
    groups = {}
    for n in nodes:
        g = n.get("group", "system")
        if g not in groups:
            groups[g] = []
        groups[g].append(n["id"])

    # 组排序（有优先级的组名）
    group_order = ["economic", "progression", "combat", "social", "narrative", "system", "conflict", "info"]
    sorted_groups = sorted(groups.keys(), key=lambda g: group_order.index(g) if g in group_order else 99)

    # 分配位置
    x_cursor = - (len(sorted_groups) * (NODE_W + PAD_X + GROUP_GAP)) // 2
    positions = {}

    for gi, gname in enumerate(sorted_groups):
        members = groups[gname]
        # 组内节点按入度排序（入度少的靠左）
        members.sort(key=lambda nid: (in_deg.get(nid, 0), node_map[nid].get("label", "")))

        y_cursor = - (len(members) * (NODE_H + PAD_Y)) // 2
        for mi, nid in enumerate(members):
            positions[nid] = {
                "x": x_cursor + (NODE_W // 2 if len(members) == 1 else 0),
                "y": y_cursor,
            }
            y_cursor += NODE_H + PAD_Y

        # 如果该组只有一个节点，居中
        if len(members) == 1:
            positions[members[0]]["x"] = x_cursor
            positions[members[0]]["y"] = 0

        x_cursor += NODE_W + PAD_X + GROUP_GAP

    return positions


# ═══════════════════════════════════════════════
#  节点生成
# ═══════════════════════════════════════════════

def make_node(nid, label, desc, group, pos):
    color_info = GROUP_COLORS.get(group, GROUP_COLORS["system"])
    bullet = desc.replace("·", "\n- ") if desc else ""
    text = f"{label}\n- {bullet}" if desc else label
    return {
        "id": nid,
        "type": "text",
        "text": text,
        "x": pos["x"],
        "y": pos["y"],
        "width": 260,
        "height": 120,
        "color": color_info["color"],
    }


def make_edge(from_id, to_id, label, style="solid"):
    color = EDGE_COLORS.get(label, EDGE_COLORS["default"])
    edge = {
        "id": f"e_{from_id}_{to_id}",
        "fromNode": from_id,
        "fromSide": "right",
        "toNode": to_id,
        "toSide": "left",
        "color": color,
    }
    if label:
        edge["label"] = label
    return edge


# ═══════════════════════════════════════════════
#  图表类型
# ═══════════════════════════════════════════════

def build_system_map(data):
    """系统关系图"""
    nodes_data = data.get("nodes", [])
    edges_data = data.get("edges", [])
    title = data.get("title", "系统关系图")

    positions = auto_layout(nodes_data, edges_data)

    # 标题节点
    title_node = {
        "id": "_title",
        "type": "text",
        "text": f"# {title}\n{datetime.now().strftime('%Y-%m-%d')}",
        "x": -200,
        "y": -350,
        "width": 400,
        "height": 70,
        "color": "7",
    }

    nodes = [title_node]
    for nd in nodes_data:
        pos = positions.get(nd["id"], {"x": 0, "y": 0})
        nodes.append(make_node(nd["id"], nd["label"], nd.get("desc", ""), nd.get("group", "system"), pos))

    edges = []
    for ed in edges_data:
        edges.append(make_edge(ed["from"], ed["to"], ed.get("label", "")))

    return {"nodes": nodes, "edges": edges}


def build_conflict_map(data):
    """冲突热力图"""
    nodes = []
    edges = []
    title = data.get("title", "冲突检测图")

    # 标题
    nodes.append({
        "id": "_title", "type": "text",
        "text": f"## {title}\n{datetime.now().strftime('%Y-%m-%d')}",
        "x": -250, "y": -300, "width": 500, "height": 70, "color": "7",
    })

    conflicts = data.get("conflicts", [])
    if not conflicts:
        nodes.append({
            "id": "_empty", "type": "text",
            "text": "✅ 未发现冲突",
            "x": -100, "y": -100, "width": 200, "height": 60, "color": "4",
        })
        return {"nodes": nodes, "edges": edges}

    # 收集所有涉及的系统
    sys_ids = set()
    for c in conflicts:
        sys_ids.add(c["system_a"])
        sys_ids.add(c["system_b"])

    # 排布系统节点（上方一排）
    sys_list = sorted(sys_ids)
    sw = 220
    gap = 40
    total_w = len(sys_list) * sw + (len(sys_list) - 1) * gap
    start_x = -total_w // 2

    # 从输入数据中获取系统描述
    sys_data = data.get("systems", [])
    sys_desc = {s["id"]: s.get("desc", "") for s in sys_data}

    sys_nodes = {}
    for i, sid in enumerate(sys_list):
        nid = f"sys_{sid}"
        color = "1" if any(c.get("level") == "🔴" and (c["system_a"] == sid or c["system_b"] == sid) for c in conflicts) else "4"
        desc_text = f"\n{sys_desc.get(sid, '')}" if sys_desc.get(sid) else ""
        sys_nodes[sid] = {
            "id": nid, "type": "text",
            "text": f"📦 {sid}{desc_text}",
            "x": start_x + i * (sw + gap),
            "y": -80,
            "width": sw, "height": 90,
            "color": color,
        }
        nodes.append(sys_nodes[sid])

    # 冲突连线（下方）
    conf_count = {}
    for ci, c in enumerate(conflicts):
        from_id = sys_nodes[c["system_a"]]["id"]
        to_id = sys_nodes[c["system_b"]]["id"]
        level = c.get("level", "🟡")
        desc = c.get("description", "")

        edge_color = {"🔴": "#e06c75", "🟡": "#e5c07b", "🟢": "#98c379"}.get(level, "#5c6370")
        edges.append({
            "id": f"conf_{ci}",
            "fromNode": from_id, "fromSide": "bottom",
            "toNode": to_id, "toSide": "bottom",
            "color": edge_color,
            "label": f"{level} {desc[:20]}",
        })
        key = (c["system_a"], c["system_b"])
        conf_count[key] = conf_count.get(key, 0) + 1

    # 底部标注图例
    legend_y = 100
    nodes.append({
        "id": "_legend", "type": "text",
        "text": "图例:  🔴 严重冲突  🟡 中度冲突  🟢 轻微冲突  ✅ 无冲突",
        "x": -300, "y": legend_y, "width": 600, "height": 50, "color": "7",
    })

    return {"nodes": nodes, "edges": edges}


# 系统描述映射（已废弃，改为从 data 中读取）
# ANY_SYSTEM_DESC = {}


def build_experience_map(data):
    """
    玩家体验路径图 — GDD 分析第4步

    输入数据格式:
    {
      "title": "玩家体验路径",
      "stages": [
        {
          "id": "first_contact", "name": "首次接触",
          "desc": "前5分钟·第一操作·首次正反馈",
          "duration": "前5分钟",
          "breakpoints": ["首次操作认知负荷高"],
          "assessment": "🟡"
        },
        ...
      ]
    }
    """
    stages = data.get("stages", [])
    title = data.get("title", "玩家体验路径")

    if not stages:
        return {"nodes": [], "edges": []}

    NODE_W = 260
    NODE_H = 130
    BP_W = 220
    BP_H = 50
    PAD_X = 80
    PAD_Y = 40

    total_w = len(stages) * NODE_W + (len(stages) - 1) * PAD_X
    start_x = -total_w // 2

    nodes = []
    edges = []

    # 标题
    nodes.append({
        "id": "_title", "type": "text",
        "text": f"# {title}\n{datetime.now().strftime('%Y-%m-%d')}",
        "x": -250, "y": -300, "width": 500, "height": 60, "color": "7",
    })

    # 阶段节点 + 断点
    stage_nodes = {}
    for si, st in enumerate(stages):
        sid = st["id"]
        x = start_x + si * (NODE_W + PAD_X)

        # 阶段名 + 时长 + 描述 + 评估
        bp_list = st.get("breakpoints", [])
        bp_text = f"\n⚠️ {bp_list[0][:20]}" if bp_list else ""
        desc = st.get("desc", "").replace("·", " · ")
        assessment = st.get("assessment", "")
        text = (
            f"### {st['name']}\n"
            f"⏱ {st.get('duration', '')}\n"
            f"{desc}\n"
            f"评估: {assessment}{bp_text}"
        )

        stage_nodes[sid] = {
            "id": sid, "type": "text",
            "text": text,
            "x": x, "y": -80,
            "width": NODE_W, "height": NODE_H,
            "color": {"🔴": "1", "🟡": "3", "🟢": "4"}.get(assessment.strip(), "7"),
        }
        nodes.append(stage_nodes[sid])

        # 断点子节点
        for bi, bp in enumerate(bp_list):
            bpid = f"{sid}_bp_{bi}"
            nodes.append({
                "id": bpid, "type": "text",
                "text": f"⚠️ {bp[:25]}",
                "x": x, "y": 80 + bi * (BP_H + 10),
                "width": BP_W, "height": BP_H,
                "color": "1",
            })
            edges.append({
                "id": f"e_{sid}_bp_{bi}",
                "fromNode": sid, "fromSide": "bottom",
                "toNode": bpid, "toSide": "top",
                "color": "#e06c75",
            })

        # 指向下一阶段的箭头
        if si < len(stages) - 1:
            next_sid = stages[si + 1]["id"]
            edges.append({
                "id": f"flow_{si}",
                "fromNode": sid, "fromSide": "right",
                "toNode": next_sid, "toSide": "left",
                "color": "#61afef",
                "label": f"→ {stages[si+1]['name']}",
            })

    # 图例
    ly = 80 + max((len(s.get("breakpoints", [])) for s in stages), default=0) * (BP_H + 10) + 40
    nodes.append({
        "id": "_legend", "type": "text",
        "text": "🟢 良好  🟡 需关注  🔴 风险  断点(⚠️)体验中断点，需优化",
        "x": -350, "y": ly, "width": 700, "height": 50, "color": "7",
    })

    return {"nodes": nodes, "edges": edges}


def build_knowledge_map(data):
    """
    知识映射图 — GDD 系统 ↔ 知识库理论

    输入数据格式:
    {
      "title": "知识映射",
      "systems": [
        {"id": "商店系统", "label": "🏪 商店系统", "desc": "刷新·锁定·消耗"},
        ...
      ],
      "theories": [
        {"id": "权力下放", "label": "📖 权力下放设计", "desc": "22-自主感与选择"},
        ...
      ],
      "mappings": [
        {"from": "商店系统", "to": "权力下放", "label": "✅ 理论验证", "type": "verified"},
        {"from": "商店系统", "to": "投篮效应", "label": "➕ 可补充", "type": "supplement"},
        {"from": "战斗系统", "to": "心流通道", "label": "⚠️ 矛盾", "type": "conflict"},
        ...
      ]
    }
    """
    systems = data.get("systems", [])
    theories = data.get("theories", [])
    mappings = data.get("mappings", [])
    title = data.get("title", "知识映射")

    # 双栏布局：左侧系统，右侧理论
    SYS_X = -600
    THEORY_X = 200
    NODE_W = 260
    NODE_H = 90
    PAD_Y = 20
    START_Y = -((max(len(systems), len(theories)) * (NODE_H + PAD_Y)) // 2)

    nodes = []
    edges = []

    # 标题
    nodes.append({
        "id": "_title", "type": "text",
        "text": f"# {title}\n{datetime.now().strftime('%Y-%m-%d %H:%M')}",
        "x": -300, "y": START_Y - 120, "width": 600, "height": 60, "color": "7",
    })

    # 左栏：GDD 系统
    y = START_Y
    for s in systems:
        bullet = s.get("desc", "").replace("·", "\n- ") if s.get("desc") else ""
        text = f"{s['label']}\n- {bullet}" if bullet else s['label']
        nodes.append({
            "id": s["id"], "type": "text",
            "text": text,
            "x": SYS_X, "y": y, "width": NODE_W, "height": NODE_H, "color": "4",
        })
        y += NODE_H + PAD_Y

    # 右栏：知识库理论
    y = START_Y
    for t in theories:
        text = t['label'] + (f"\n{t.get('desc', '')}" if t.get('desc') else "")
        nodes.append({
            "id": t["id"], "type": "text",
            "text": text,
            "x": THEORY_X, "y": y, "width": NODE_W, "height": NODE_H, "color": "6",
        })
        y += NODE_H + PAD_Y

    # 连线：系统 → 理论
    for mi, m in enumerate(mappings):
        map_type = m.get("type", "")
        color = {"verified": "#98c379", "supplement": "#61afef",
                 "conflict": "#e5c07b", "unsupported": "#e06c75"}.get(map_type, "#5c6370")
        label = m.get("label", "")
        edges.append({
            "id": f"map_{mi}",
            "fromNode": m["from"], "fromSide": "right",
            "toNode": m["to"], "toSide": "left",
            "color": color, "label": label,
        })

    # 图例
    ly = START_Y + max(len(systems), len(theories)) * (NODE_H + PAD_Y) + 20
    nodes.append({
        "id": "_legend", "type": "text",
        "text": "图例:  ✅ 理论验证(绿)  ➕ 可补充(蓝)  ⚠️ 矛盾(黄)  ❌ 无支撑(红)",
        "x": -350, "y": ly + 40, "width": 700, "height": 50, "color": "7",
    })

    return {"nodes": nodes, "edges": edges}


# ═══════════════════════════════════════════════
#  主入口
# ═══════════════════════════════════════════════

def main():
    if len(sys.argv) < 2:
        print("用法:")
        print("  python 脚本/generate_canvas_diagram.py system-map \\")
        print("    --title '标题' --nodes '[...]' --edges '[...]'")
        print()
        print("  或:")
        print("  python 脚本/generate_canvas_diagram.py conflict-map \\")
        print("    --title '标题' --conflicts '[...]'")
        print()
        print("图表类型: system-map | conflict-map | knowledge-map | experience-map")
        sys.exit(1)

    chart_type = sys.argv[1]

    # 解析参数
    args = {}
    i = 2
    while i < len(sys.argv):
        if sys.argv[i].startswith("--"):
            key = sys.argv[i][2:]
            i += 1
            if i < len(sys.argv):
                args[key] = sys.argv[i]
        i += 1

    title = args.get("title", "未命名图表")
    output = args.get("output", "")

    # 根据类型构建
    builders = {
        "system-map": build_system_map,
        "conflict-map": build_conflict_map,
        "knowledge-map": build_knowledge_map,
        "experience-map": build_experience_map,
    }

    builder = builders.get(chart_type)
    if not builder:
        print(f"❌ 未知类型: {chart_type}")
        print(f"   可用: {', '.join(builders.keys())}")
        sys.exit(1)

    # 解析输入数据 — 支持文件路径和直接 JSON
    def load_json_input(val):
        """从文件或字符串加载 JSON"""
        p = Path(val)
        if p.exists():
            return json.loads(p.read_text(encoding="utf-8"))
        return json.loads(val)

    if args.get("nodes"):
        raw_nodes = load_json_input(args["nodes"])
        raw_edges = load_json_input(args.get("edges", "[]"))
        # 如果输入的是完整数据文件，自动提取对应字段
        nodes = raw_nodes if isinstance(raw_nodes, list) else raw_nodes.get("nodes", raw_nodes.get("systems", []))
        edges = raw_edges if isinstance(raw_edges, list) else raw_edges.get("edges", raw_edges.get("mappings", []))
        data = {"title": title, "nodes": nodes, "edges": edges}
    elif args.get("conflicts"):
        data = {
            "title": title,
            "conflicts": load_json_input(args["conflicts"]),
            "systems": load_json_input(args.get("systems", "[]")),
        }
    elif args.get("data"):
        data = load_json_input(args["data"])
        data["title"] = title if title != "未命名图表" else data.get("title", title)
    else:
        print("❌ 请提供 --nodes / --conflicts / --data 参数")
        sys.exit(1)

    # 构建 Canvas
    canvas = builder(data)

    # 确定输出路径
    if not output:
        safe_name = title.replace(" ", "_").replace("/", "-")
        output = str(ROOT / "知识库" / "画布" / f"{safe_name}.canvas")

    output_path = Path(output)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(canvas, ensure_ascii=False, indent=2), encoding="utf-8")

    print(f"✅ Canvas 已生成: {output_path}")
    print(f"   节点: {len(canvas['nodes'])} 个 · 连线: {len(canvas['edges'])} 条")
    print(f"   在 Obsidian 中打开即可查看")


if __name__ == "__main__":
    main()
