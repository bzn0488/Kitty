#!/usr/bin/env python3
"""
批量添加 YAML frontmatter 到知识库文件。
从理论知识/_索引.md 的标签路由表中提取标签信息。
"""
import re
import json
from pathlib import Path
from datetime import date

ROOT = Path(__file__).resolve().parent.parent
KNOWLEDGE_DIR = ROOT / "知识库"

# ── 理论知识标签映射（从 _索引.md 提取） ──
THEORY_TAGS = {
    "父系与母系设计哲学.md": ["design-philosophy"],
    "自我决定理论SDT.md": ["player-psychology"],
    "过度理由效应.md": ["player-psychology"],
    "信号与强化体系.md": ["player-psychology", "reinforcement"],
    "正反馈与负反馈循环.md": ["player-psychology", "reinforcement"],
    "驱动力模型.md": ["player-psychology"],
    "学习与遗忘曲线.md": ["player-psychology", "design-philosophy"],
    "心流通道.md": ["flow", "rhythm"],
    "游戏节奏设计.md": ["rhythm", "flow"],
    "难度调节方案.md": ["difficulty", "flow"],
    "权力下放设计.md": ["empowerment", "player-psychology"],
    "快车道设计.md": ["quick-lane", "happiness-total"],
    "无效进程与三层乐趣体系.md": ["invalid-process", "happiness-total"],
    "渐进式生成.md": ["procedural-gen"],
    "体验4X.md": ["experience-4x"],
    "涌现式设计.md": ["emergence", "system-interaction"],
    "系统交互与体验调节.md": ["system-interaction", "player-psychology"],
    "玩具系统.md": ["toy-system", "player-psychology"],
    "游戏化叙事.md": ["narrative"],
    "玩家投影与游戏肌理.md": ["narrative"],
    "叙事中的父系结构设计.md": ["narrative", "design-philosophy"],
    "宏观层与微观层叙事.md": ["narrative", "system-interaction"],
    "他者确认与社交反馈.md": ["player-psychology", "social"],
    "创造性表达.md": ["player-psychology", "expression"],
    "协同效应.md": ["synergy", "system-interaction"],
    "双轨收益与强化程式.md": ["reinforcement", "player-psychology"],
    "叠层设计与延迟满足.md": ["reinforcement", "happiness-total"],
    "快乐总量.md": ["happiness-total"],
    "浪费规避设计.md": ["waste-avoidance", "player-psychology"],
    "额外挑战与及格线.md": ["difficulty", "player-psychology"],
    "精准操作设计.md": ["mechanics", "player-skill"],
    "投篮效应.md": ["player-psychology", "randomness"],
}

# ── 跨项目知识标签 ──
CROSS_PROJECT_TAGS = {
    "卡牌设计通用规则.md": ["card-design", "game-design"],
    "Boss设计通用规则.md": ["boss-design", "game-design"],
    "部位破坏设计通用规则.md": ["part-break", "combat-design"],
    "Roguelike设计通用规则.md": ["roguelike", "game-design"],
    "协同效应设计通用规则.md": ["synergy", "game-design"],
}

# ── 游戏设计理论主文档标签 ──
THEORY_MAIN_TAGS = {
    "00-总览与阅读导引.md": ["meta", "index"],
    "01-核心概念与思维模型.md": ["design-philosophy", "theory"],
    "01-核心概念与思维模型_完整版.md": ["design-philosophy", "theory"],
    "02-玩家心理学.md": ["player-psychology", "theory"],
    "02-玩家心理学_完整版.md": ["player-psychology", "theory"],
    "03-游戏机制设计技法.md": ["game-mechanics", "theory"],
    "03-游戏机制设计技法_完整版.md": ["game-mechanics", "theory"],
    "04-游戏节奏与心流设计.md": ["flow", "rhythm", "theory"],
    "04-游戏节奏与心流设计_完整版.md": ["flow", "rhythm", "theory"],
    "05-叙事设计.md": ["narrative", "theory"],
    "05-叙事设计_完整版.md": ["narrative", "theory"],
    "06-附录：原文出处索引.md": ["reference", "index"],
    "游戏设计知识测试卷.md": ["test", "assessment"],
}

# ── 归纳文档标签 ──
SUMMARY_TAGS = {
    "01-设计哲学：父系与母系思维.md": ["design-philosophy"],
    "02-玩家心理学与行为模型.md": ["player-psychology"],
    "03-心流与游戏节奏设计.md": ["flow", "rhythm"],
    "04-快乐总量与正反馈技法.md": ["happiness-total", "reinforcement"],
    "05-涌现式与系统交互设计.md": ["emergence", "system-interaction"],
    "06-游戏叙事与投影设计.md": ["narrative"],
}

CREATED_DEFAULT = "2026-05-24"


def has_frontmatter(content: str) -> bool:
    """检查文件是否已有 frontmatter"""
    return content.startswith("---")


def add_frontmatter(filepath: Path, tags: list[str], status: str = "draft"):
    """为文件添加 YAML frontmatter"""
    content = filepath.read_text(encoding="utf-8")
    if has_frontmatter(content):
        print(f"  ⏭️  已有 frontmatter: {filepath.name}")
        return False

    # 提取标题（第一个 # 行的内容）
    title_match = re.search(r"^#\s+(.+)$", content, re.MULTILINE)
    title = title_match.group(1).strip() if title_match else filepath.stem

    frontmatter = f"""---
title: {title}
tags: [{', '.join(tags)}]
created: {CREATED_DEFAULT}
status: {status}
---

"""
    filepath.write_text(frontmatter + content, encoding="utf-8")
    print(f"  ✅ 添加 frontmatter: {filepath.name} (tags: {tags})")
    return True


def process_directory(dirname: str, tag_map: dict, status: str = "draft"):
    """处理目录下的所有文件"""
    theory_dir = KNOWLEDGE_DIR / dirname
    if not theory_dir.exists():
        print(f"  ⚠️  目录不存在: {dirname}")
        return 0

    count = 0
    for filename, tags in sorted(tag_map.items()):
        filepath = theory_dir / filename
        if not filepath.exists():
            print(f"  ⚠️  文件不存在: {dirname}/{filename}")
            continue
        if add_frontmatter(filepath, tags, status):
            count += 1

    return count


def main():
    total = 0
    today = date.today().isoformat()

    print("=" * 50)
    print("📝 批量添加 Frontmatter")
    print(f"  日期: {today}")
    print("=" * 50)

    print("\n📚 理论知识/")
    total += process_directory("理论知识", THEORY_TAGS, "verified")

    print("\n🔗 跨项目知识/")
    total += process_directory("跨项目知识", CROSS_PROJECT_TAGS, "verified")

    print("\n📖 游戏设计理论/")
    total += process_directory("游戏设计理论", THEORY_MAIN_TAGS)

    print("\n📋 归纳文档/")
    total += process_directory("游戏设计理论/归纳文档", SUMMARY_TAGS, "verified")

    print(f"\n{'=' * 50}")
    print(f"✅ 完成！共添加 {total} 个文件的 frontmatter")
    print(f"{'=' * 50}")

    return 0


if __name__ == "__main__":
    exit(main())
