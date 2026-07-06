#!/usr/bin/env python3
"""
生成 GDD 分析 4 种 Canvas 图表 — 供 Agent 在工作流结束时一键调用

用法:
  python 脚本/generate_gdd_canvases.py <项目名称>

示例:
  python 脚本/generate_gdd_canvases.py 增量自走棋

数据来源:
  从 知识库/画布/_data_*.json 读取结构化数据并生成 Canvas，
  或者根据参数生成带占位数据的模板 Canvas。

生成文件:
  知识库/画布/{项目名称}_系统关系.canvas
  知识库/画布/{项目名称}_冲突检测.canvas
  知识库/画布/{项目名称}_体验路径.canvas
  知识库/画布/{项目名称}_知识映射.canvas
"""

import sys
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SCRIPT = ROOT / "脚本" / "generate_canvas_diagram.py"
DATA_DIR = ROOT / "知识库" / "画布"
OUTPUT_DIR = ROOT / "知识库" / "画布"

TYPES = ["system-map", "conflict-map", "experience-map", "knowledge-map"]


def generate_canvas(chart_type, title, data_file):
    """调用 generate_canvas_diagram.py 生成单个 Canvas"""
    cmd = [
        sys.executable,
        str(SCRIPT),
        chart_type,
        "--title", title,
        "--data", str(data_file),
    ]
    result = subprocess.run(cmd, capture_output=True, text=True)
    if result.returncode == 0:
        print(f"  ✅ {chart_type}: 成功")
        return True
    else:
        print(f"  ❌ {chart_type}: 失败")
        print(f"     {result.stderr.split(chr(10))[0] if result.stderr else result.stdout.split(chr(10))[0]}")
        return False


def guess_data_file(chart_type, project=None):
    """猜测对应的 _data_ 文件"""
    mapping = {
        "system-map": "_data_系统关系.json",
        "conflict-map": "_data_冲突检测.json",
        "experience-map": "_data_体验路径.json",
        "knowledge-map": "_data_知识映射.json",
    }
    return DATA_DIR / mapping.get(chart_type, f"_data_{chart_type}.json")


def create_template_data(chart_type, project):
    """如果数据文件不存在，创建模板数据"""
    # 简化处理：只提示用户缺少数据文件，不自动创建空模板
    return None


def main():
    if len(sys.argv) < 2:
        project = "GDD分析"
    else:
        project = sys.argv[1]

    print(f"🎨 生成 {project} 的 Canvas 图表")
    print("=" * 40)

    results = []
    for ct in TYPES:
        data_file = guess_data_file(ct)
        if not data_file.exists():
            print(f"  ⏭️  {ct}: 数据文件不存在 → 跳过 ({data_file.name})")
            results.append(False)
            continue

        title = f"{project}_{ct.replace('-map', '')}"
        print(f"  🔄 {ct}...", end=" ")
        ok = generate_canvas(ct, title, data_file)
        results.append(ok)

    print("=" * 40)
    success = sum(1 for r in results if r)
    total = len(results)
    print(f"📊 完成: {success}/{total} 个图表已生成")
    print(f"   位置: {OUTPUT_DIR}")
    print(f"   在 Obsidian 中打开 知识库/画布/ 查看")


if __name__ == "__main__":
    main()
