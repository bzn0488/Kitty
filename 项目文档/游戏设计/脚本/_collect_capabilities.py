"""收集 _共享资源/ 中所有能力模块的描述信息"""
import os, glob, re, json

SHARED = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def read_description(filepath, max_len=500):
    """从 YAML frontmatter 中提取 description 字段"""
    with open(filepath, encoding='utf-8') as f:
        content = f.read(max_len)
    # 格式1: description: "带引号的值"
    m = re.search(r'description:\s*"([^"]+)"', content)
    if m:
        return m.group(1)
    # 格式2: description: '带引号的值'
    m = re.search(r"description:\s*'([^']+)'", content)
    if m:
        return m.group(1)
    # 格式3: description: 无引号的值（直到换行或逗号）
    m = re.search(r'description:\s*([^\n"\',}]+)', content)
    if m:
        return m.group(1).strip()
    return None

def get_json_description(filepath):
    """从 JSON 文件中提取 description 字段（支持修复的格式）"""
    try:
        with open(filepath, encoding='utf-8') as f:
            raw = f.read()
        # Handle files that have literal \\u201c sequences instead of real quotes
        # (fix_json_quotes.py 之前处理过这类文件，可能留下了转义序列)
        bs = chr(92)  # backslash
        raw = raw.replace(bs + 'u201c', '\"').replace(bs + 'u201d', '\"')
        sq = chr(39)  # single quote
        raw = raw.replace(bs + 'u2018', sq).replace(bs + 'u2019', sq)
        data = json.loads(raw)
        return data.get('description')
    except json.JSONDecodeError as e:
        # Try to extract description via regex as fallback
        m = re.search(r'\"description\"\s*:\s*\"([^\"]+)\"', raw)
        if m:
            return m.group(1)
        return None

def get_py_description(filepath):
    """从 Python 文件中提取 docstring 或说明"""
    with open(filepath, encoding='utf-8') as f:
        content = f.read(2000)
    # Try module docstring
    m = re.search(r'"""(.+?)"""', content, re.DOTALL)
    if m:
        return m.group(1).strip().split('\n')[0]
    # Try single-line comment description
    m = re.search(r'^#\s*(.+)', content, re.MULTILINE)
    if m:
        return m.group(1).strip()
    return None

def get_md_title(filepath):
    """从 Markdown 文件中提取标题"""
    with open(filepath, encoding='utf-8') as f:
        content = f.read(500)
    m = re.search(r'^#\s+(.+)', content, re.MULTILINE)
    if m:
        return m.group(1).strip()
    return None

def collect_all():
    result = {
        "agents": [],
        "skills": [],
        "prompts": [],
        "workflows": [],
        "scripts": [],
        "documents": [],
    }
    
    # Agents
    agents_dir = os.path.join(SHARED, 'agents')
    if os.path.isdir(agents_dir):
        for f in sorted(glob.glob(os.path.join(agents_dir, '*.agent.md'))):
            name = os.path.basename(f)
            desc = read_description(f)
            result["agents"].append({"name": name, "description": desc or ""})
    
    # Skills
    skills_dir = os.path.join(SHARED, '技能')
    if os.path.isdir(skills_dir):
        for f in sorted(glob.glob(os.path.join(skills_dir, '**', '*.skill.md'), recursive=True)):
            name = os.path.basename(f)
            # Skip template files
            if name.startswith('template'):
                continue
            desc = read_description(f)
            result["skills"].append({"name": name, "description": desc or ""})
    
    # Prompts
    prompts_dir = os.path.join(SHARED, 'prompts')
    if os.path.isdir(prompts_dir):
        for f in sorted(glob.glob(os.path.join(prompts_dir, '*.prompt.md'))):
            name = os.path.basename(f)
            desc = read_description(f)
            result["prompts"].append({"name": name, "description": desc or ""})
    
    # Workflows (JSON files + engine .py files in 工作流/)
    wf_dir = os.path.join(SHARED, '工作流')
    if os.path.isdir(wf_dir):
        for f in sorted(glob.glob(os.path.join(wf_dir, '*.json')) + glob.glob(os.path.join(wf_dir, '*.py'))):
            name = os.path.basename(f)
            if name.startswith('_'):
                continue  # skip internal config files
            if f.endswith('.json'):
                desc = get_json_description(f)
            else:
                desc = get_py_description(f)
            result["workflows"].append({"name": name, "description": desc or ""})
    
    # Scripts
    scripts_dir = os.path.join(SHARED, '脚本')
    if os.path.isdir(scripts_dir):
        for f in sorted(glob.glob(os.path.join(scripts_dir, '*.py')) + glob.glob(os.path.join(scripts_dir, '*.ps1'))):
            name = os.path.basename(f)
            if name.startswith('_'):
                continue
            desc = get_py_description(f) if f.endswith('.py') else None
            result["scripts"].append({"name": name, "description": desc or ""})
    
    # Documents
    docs_dir = os.path.join(SHARED, '文档')
    if os.path.isdir(docs_dir):
        for f in sorted(glob.glob(os.path.join(docs_dir, '*.md'))):
            name = os.path.basename(f)
            title = get_md_title(f)
            result["documents"].append({"name": name, "description": title or name})
    
    return result

def generate_markdown(data):
    """生成能力清单 Markdown"""
    lines = []
    lines.append("# 共享资源能力清单")
    lines.append("")
    lines.append("> 由 `脚本/_collect_capabilities.py` 自动生成")
    lines.append(f"> 更新于 {__import__('datetime').datetime.now().strftime('%Y-%m-%d %H:%M')}")
    lines.append("")

    # ---- 分类说明 ----
    lines.append("## 分类说明")
    lines.append("")
    lines.append("| 分类 | 用途 | 类比 |")
    lines.append("|------|------|------|")
    lines.append('| **Agents**（智能角色） | 定义可调用的 AI 专业角色，封装完整工作流。用户可直接 `@角色名` 召唤，自动执行整套设计流程 | 雇佣一个\u201c专家\u201d——他知道整个流程怎么走 |')
    lines.append('| **Skills**（技能包） | 领域知识和方法论沉淀，包含设计原则、自检清单、已犯错误记录。被 Agent 和 Prompts 按需调用 | 专家的\u201c工具箱\u201d——里面是专业知识和经验总结 |')
    lines.append('| **Prompts**（提示指令） | 面向 AI 的详细操作指令，规定某类任务的执行步骤、输出格式、约束条件 | 专家的\u201c操作手册\u201d——告诉他具体每一步怎么做 |')
    lines.append('| **Workflows**（工作流） | 可执行的工作流定义（JSON）和引擎（Python），定义步骤依赖链，强制顺序执行、防止跳步 | 专家的\u201c流程清单\u201d——确保不遗漏任何步骤 |')
    lines.append('| **Scripts**（自动化脚本） | 独立的自动化工具，用于索引生成、备份、自检、同步等运维任务 | 专家的\u201c自动化工具\u201d——一键完成重复性工作 |')
    lines.append('| **Documents**（文档） | 工作区使用说明、架构设计、定制指南等供人阅读的文档 | 专家的\u201c说明书\u201d——告诉你这个工作区怎么用 |')
    lines.append("")
    lines.append("### 协作关系")
    lines.append("")
    lines.append("```")
    lines.append("Agent（角色）")
    lines.append(" ├─ Skill（领域知识）")
    lines.append(" ├─ Prompt（执行指令）—— 调用设计引擎推进步骤")
    lines.append(" │   └─ Workflow / Engine（步骤定义 + 强制检查）")
    lines.append(" └─ Script（运维工具）")
    lines.append("```")
    lines.append("")

    # Agents
    lines.append("## Agents（智能角色）")
    lines.append("")
    lines.append("> 可被 `@` 直接调用的 AI 角色。当前可用：")
    lines.append("")
    lines.append("| 文件 | 描述 |")
    lines.append("|------|------|")
    for a in data["agents"]:
        lines.append(f"| `{a['name']}` | {a['description']} |")
    lines.append("")
    
    # Skills
    lines.append("## Skills（技能包）")
    lines.append("")
    lines.append("| 文件 | 描述 |")
    lines.append("|------|------|")
    for s in data["skills"]:
        lines.append(f"| `{s['name']}` | {s['description']} |")
    lines.append("")
    
    # Prompts
    lines.append("## Prompts（提示指令）")
    lines.append("")
    lines.append("| 文件 | 描述 |")
    lines.append("|------|------|")
    for p in data["prompts"]:
        lines.append(f"| `{p['name']}` | {p['description']} |")
    lines.append("")
    
    # Workflows
    lines.append("## Workflows（工作流）")
    lines.append("")
    lines.append("| 文件 | 描述 |")
    lines.append("|------|------|")
    for w in data["workflows"]:
        lines.append(f"| `{w['name']}` | {w['description']} |")
    lines.append("")
    
    # Scripts
    lines.append("## Scripts（自动化脚本）")
    lines.append("")
    lines.append("| 文件 | 描述 |")
    lines.append("|------|------|")
    for s in data["scripts"]:
        desc = s['description'] if s['description'] else "（无描述）"
        lines.append(f"| `{s['name']}` | {desc} |")
    lines.append("")
    
    # Documents
    lines.append("## Documents（文档）")
    lines.append("")
    lines.append("| 文件 | 标题 |")
    lines.append("|------|------|")
    for d in data["documents"]:
        lines.append(f"| `{d['name']}` | {d['description']} |")
    lines.append("")
    
    return "\n".join(lines)

if __name__ == "__main__":
    data = collect_all()
    md = generate_markdown(data)
    output_path = os.path.join(SHARED, "能力清单.md")
    with open(output_path, 'w', encoding='utf-8') as f:
        f.write(md)
    print(f"✅ 能力清单已生成: {output_path}")
    print(f"   Agents: {len(data['agents'])} | Skills: {len(data['skills'])} | Prompts: {len(data['prompts'])}")
    print(f"   Workflows: {len(data['workflows'])} | Scripts: {len(data['scripts'])} | Documents: {len(data['documents'])}")
