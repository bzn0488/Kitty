#!/usr/bin/env python3
"""
B站视频字幕获取工具

通过 Bilibili API 获取视频的 CC 字幕/AI 字幕文本，
提取内容用于知识调研。

用法:
  python 脚本/fetch_bilibili_subtitle.py BV1xxxxxxxxxx

依赖:
  pip install requests
"""

import re
import sys
import json
import requests
from urllib.parse import quote

BASE_HEADERS = {
    "User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
    "Referer": "https://www.bilibili.com",
}


def get_cid(bvid: str) -> int | None:
    """从视频页面获取 cid"""
    url = f"https://api.bilibili.com/x/web-interface/view?bvid={bvid}"
    resp = requests.get(url, headers=BASE_HEADERS, timeout=10)
    data = resp.json()
    if data["code"] != 0:
        print(f"❌ API 错误: {data.get('message', '未知')}")
        return None
    return data["data"]["cid"]


def list_subtitles(bvid: str, cid: int) -> list[dict]:
    """列出视频可用的字幕列表"""
    url = f"https://api.bilibili.com/x/player/v2?bvid={bvid}&cid={cid}"
    resp = requests.get(url, headers=BASE_HEADERS, timeout=10)
    data = resp.json()
    if data["code"] != 0:
        print(f"❌ 获取字幕列表失败: {data.get('message', '未知')}")
        return []

    subtitles = data["data"].get("subtitle", {}).get("subtitles", [])
    return subtitles


def fetch_subtitle_text(subtitle_url: str) -> str | None:
    """获取字幕 JSON 并提取纯文本"""
    if subtitle_url.startswith("//"):
        subtitle_url = "https:" + subtitle_url

    resp = requests.get(subtitle_url, headers=BASE_HEADERS, timeout=10)
    data = resp.json()

    # 提取所有片段
    parts = []
    for item in data.get("body", []):
        parts.append({
            "from": item["from"],
            "to": item["to"],
            "content": item["content"],
        })

    return parts


def format_timestamp(seconds: float) -> str:
    """将秒数格式化为 mm:ss"""
    m = int(seconds // 60)
    s = int(seconds % 60)
    return f"{m:02d}:{s:02d}"


def format_text(parts: list[dict], include_time: bool = True) -> str:
    """格式化为可读文本"""
    lines = []
    for p in parts:
        if include_time:
            ts = format_timestamp(p["from"])
            lines.append(f"[{ts}] {p['content']}")
        else:
            lines.append(p["content"])
    return "\n".join(lines)


def get_video_info(bvid: str) -> dict | None:
    """获取视频基本信息"""
    url = f"https://api.bilibili.com/x/web-interface/view?bvid={bvid}"
    resp = requests.get(url, headers=BASE_HEADERS, timeout=10)
    data = resp.json()
    if data["code"] != 0:
        return None
    d = data["data"]
    return {
        "title": d["title"],
        "desc": d["desc"],
        "duration": d["duration"],
        "stat": d["stat"],
        "tname": d.get("tname", ""),
        "aid": d["aid"],
    }


def main():
    if len(sys.argv) < 2:
        print("用法: python fetch_bilibili_subtitle.py <BV号或视频URL>")
        print("示例: python fetch_bilibili_subtitle.py BV1P7411h7FK")
        print("      python fetch_bilibili_subtitle.py https://www.bilibili.com/video/BV1P7411h7FK")
        sys.exit(1)

    arg = sys.argv[1]
    # 从 URL 中提取 BV 号
    bv_match = re.search(r"BV\w{10}", arg)
    if not bv_match:
        print("❌ 未找到有效的 BV 号")
        sys.exit(1)
    bvid = bv_match.group(0)

    print(f"🎬 视频: https://www.bilibili.com/video/{bvid}")
    print()

    # 获取视频信息
    info = get_video_info(bvid)
    if info:
        print(f"📺 标题: {info['title']}")
        print(f"🏷️  分区: {info['tname']}")
        print(f"▶️  时长: {info['duration']}秒")
        print(f"📊 播放: {info['stat'].get('view', '?')}  |  "
              f"弹幕: {info['stat'].get('danmaku', '?')}  |  "
              f"评论: {info['stat'].get('reply', '?')}")
        print()

    # 获取 cid
    cid = get_cid(bvid)
    if not cid:
        sys.exit(1)
    print(f"📌 CID: {cid}")

    # 列出字幕
    subtitles = list_subtitles(bvid, cid)
    if not subtitles:
        print("\n⚠️  该视频没有 CC 字幕或 AI 字幕")
        print("   可能原因：")
        print("   1. UP 主没有上传字幕文件")
        print("   2. B站 AI 字幕仅在 APP 端生成")
        print("   3. 视频开启了 AI 字幕但尚未生成")
        sys.exit(0)

    print(f"\n📜 找到 {len(subtitles)} 个字幕:")
    for i, sub in enumerate(subtitles):
        lang_map = {
            "zh-CN": "中文（简体）",
            "zh-TW": "中文（繁体）",
            "en-US": "英语",
            "ja-JP": "日语",
        }
        lang = lang_map.get(sub.get("lan", ""), sub.get("lan", "未知"))
        print(f"  [{i}] {lang} — {sub.get('subtitle_url', '')}")

    # 默认选择第一个字幕
    chosen = 0
    sub_url = subtitles[chosen].get("subtitle_url", "")
    parts = fetch_subtitle_text(sub_url)

    if not parts:
        print("❌ 获取字幕内容失败")
        sys.exit(1)

    print(f"\n{'='*60}")
    print(f"📝 字幕文本（共 {len(parts)} 条）")
    print(f"{'='*60}\n")

    text = format_text(parts, include_time=True)
    print(text)

    # 同时保存到文件
    safe_title = re.sub(r'[\\/:*?"<>|]', '_', info['title']) if info else bvid
    filename = f"字幕_{safe_title}_{bvid}.txt"
    with open(filename, "w", encoding="utf-8") as f:
        raw_text = format_text(parts, include_time=False)
        # 加标题头
        header = f"标题: {info['title'] if info else bvid}\n"
        header += f"URL: https://www.bilibili.com/video/{bvid}\n"
        header += f"时间: {len(parts)} 条字幕\n"
        header += f"{'='*40}\n\n"
        header += raw_text
        f.write(header)

    print(f"\n💾 已保存到: {filename}")


if __name__ == "__main__":
    main()
