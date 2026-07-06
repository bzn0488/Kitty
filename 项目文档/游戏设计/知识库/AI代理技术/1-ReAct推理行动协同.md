---
title: ReAct 推理行动协同
tags: [ai-agent, reasoning, action-synergy]
created: 2026-06-09
status: draft
---

# ReAct：推理与行动协同（Reasoning + Acting）

> Yao et al., 2023 · ICLR 2023 · arXiv:2210.03629

## 核心思想

LLM 的推理能力（Chain-of-Thought）和行动能力（工具使用/环境交互）通常被分开研究。ReAct 的核心创新是将两者**交错执行**：

```
思考(推理) → 行动(调用工具) → 观察(获取反馈) → 思考(更新计划) → ...
```

## 工作机制

1. **推理轨迹（Reasoning Traces）**：跟踪行动计划、处理异常、更新策略
2. **行动（Actions）**：调用外部 API/工具获取信息
3. **观察（Observations）**：将外部反馈注入推理链

## 关键技术优势

```
优势             说明
────────────────────────────────────────────────────
减少幻觉         外部验证防止 CoT 式的错误传播
可解释性         推理轨迹让决策过程透明
错误恢复         观察到异常后可自动调整策略
少样本适配       1-2 个示例即可工作，无需微调
```

## 与工作流引擎的关系

我们当前的 `_engine.py` 本质上是 ReAct 模式的一个特例：
- **步骤定义** = 推理轨迹（每一步有 prompt、tools、output_schema）
- **next 命令** = 行动（执行当前步骤）
- **状态检查** = 观察（读取执行结果）

改进方向：让 engine 支持**动态步骤选择**（而非固定线性顺序），增加**异常恢复**机制。

## 应用场景

- 知识调研：搜索→阅读→评估→再搜索→整合
- 设计工作流：发散→收敛→验证→修正→预览
- 调试：假设→检查→观察→修正假设

## 参考

- [ReAct: Synergizing Reasoning and Acting in Language Models](https://arxiv.org/abs/2210.03629)
- 关联概念：`Chain-of-Thought.md`, `Self-Refine.md`