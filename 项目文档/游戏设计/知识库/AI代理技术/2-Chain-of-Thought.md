---
title: Chain-of-Thought 逐步推理
tags: [ai-agent, reasoning, prompt-pattern]
created: 2026-06-09
status: draft
---

# Chain-of-Thought（思维链）

> Wei et al., 2022 · Google Research

## 核心思想

通过在 prompt 中显式要求 LLM 输出中间推理步骤（而非直接给出答案），显著提升复杂推理任务的准确性。

## 基本形式

```
Q: 问题
A: 让我们一步步思考。
第一步：...
第二步：...
因此答案是：...
```

## 变体

| 变体                      | 描述                             | 适用场景     |
| ------------------------- | -------------------------------- | ------------ |
| Zero-shot CoT             | 仅加「Let's think step by step」 | 通用推理     |
| Few-shot CoT              | 提供示例推理链                   | 需要领域格式 |
| Auto-CoT                  | 自动生成示例                     | 减少人工     |
| CoT-SC (Self-Consistency) | 多条路径+投票                    | 高精度需求   |

## 在 Agent 中的应用

CoT 是所有复杂 Agent prompt 的基础模式。我们的工作流 prompt 中随处可见 CoT 的影子：
- `read_context` 步骤要求「逐条摘录」「逐篇读取」——显式分步
- 原则 4（叙事弧线）的起承转合——也是 CoT 的结构化变体
- 对弈自检的 probe→answer→judge→improve——CoT 的步骤化

## 局限性

- 不能消除幻觉（错误推理链看起来也合理）
- 长链可能偏离（error propagation）
- 与 ReAct 结合可缓解（通过外部验证打断错误链）

## 参考

- [Chain-of-Thought Prompting Elicits Reasoning in Large Language Models](https://arxiv.org/abs/2201.11903)
- 关联概念：`ReAct推理行动协同.md`, `Self-Refine自改进.md`