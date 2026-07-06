#!/usr/bin/env python3
"""
会话结束处理器 — 在每次会话结束时运行，保存工作摘要到 记忆/会话/。

用法:
  python 脚本/session_end.py --summary "本会话完成的工作摘要"

或者在 Agent 的 copilot-instructions.md 中配置为会话结束自动运行。
"""

import argparse
import json
import sys
from datetime import datetime
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SESSIONS_DIR = ROOT / "记忆" / "会话"
DECISIONS_DIR = ROOT / "记忆" / "决策"


def get_workflow_state():
    """获取当前工作流状态"""
    state_file = ROOT / "工作流" / "_state.json"
    if not state_file.exists():
        return None
    return json.loads(state_file.read_text(encoding="utf-8"))


def save_session(summary, workflow_info=None):
    """保存会话摘要"""
    SESSIONS_DIR.mkdir(parents=True, exist_ok=True)
    
    today = datetime.now().strftime("%Y-%m-%d")
    now = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
    filepath = SESSIONS_DIR / f"{today}.md"
    
    # 检查是否已有今日记录
    if filepath.exists():
        existing = filepath.read_text(encoding="utf-8")
        content = existing.rstrip() + "\n\n"
    else:
        content = f"# 会话记录 - {today}\n\n"
    
    content += f"## {now}\n\n"
    content += f"{summary}\n\n"
    
    if workflow_info:
        content += f"### 工作流状态\n"
        content += f"- 活跃工作流: {workflow_info.get('active_workflow', '无')}\n"
        content += f"- 当前步骤: {workflow_info.get('current_step', '无')}\n"
        content += f"- 状态: {workflow_info.get('status', '未知')}\n\n"
    
    filepath.write_text(content, encoding="utf-8")
    print(f"✅ 会话摘要已保存: {filepath}")
    return filepath


def main():
    parser = argparse.ArgumentParser(description="保存会话结束摘要")
    parser.add_argument("--summary", "-s", required=True, help="本次会话的工作摘要")
    args = parser.parse_args()
    
    workflow_state = get_workflow_state()
    save_session(args.summary, workflow_state)
    
    return 0


if __name__ == "__main__":
    sys.exit(main())
