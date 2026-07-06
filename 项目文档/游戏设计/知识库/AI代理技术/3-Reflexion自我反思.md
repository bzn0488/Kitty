---
title: Reflexion 自我反思
tags: [ai-agent, self-improvement, reflection]
created: 2026-06-09
status: draft
---

# Reflexion：Agent 自我反思机制

> Shinn et al., 2023 · arXiv:2303.11366

## 核心思想

Agent 在执行任务失败后，不是简单地重试，而是**生成一段文字反思**（self-reflection），存入记忆。下次遇到类似任务时，读取之前的反思来指导行动。

```
试错 → 失败 → 生成反思 → 存入记忆 → 下次参考 → 改进
```

## 三要素

| 要素          | 说明                                                       |
| ------------- | ---------------------------------------------------------- |
| **Actor**     | 执行任务的 LLM（根据当前状态和记忆选择行动）               |
| **Evaluator** | 判断任务成功/失败的评估器（奖励函数/启发式规则/LLM judge） |
| **Memory**    | 存储成功轨迹和失败反思的经验池                             |

## 反思的两种类型

1. **语义反思（Semantic）**：用自然语言描述哪里做错了、应该怎么做——可迁移到类似任务
2. **轨迹反思（Episodic）**：记录完整的决策轨迹——具体到某个环境状态

## 与我们系统的关联

我们的**对弈自检工作流**本质上是 Reflexion 的一个实例：

```
Actor → Agent 执行探针任务
Evaluator → 裁判步骤判定通过/不通过
Memory → 本次对弈记录存入改进清单
```

**改进方向**：当前对弈自检是手动触发的一轮流程。如果能自动积累失败模式，在不断的工作流执行中学习，就实现了 Reflexion 的完整循环。

## 应用场景

- 工作流执行失败后自动记录原因，下次避免同样错误
- 设计质量检查：每次发现设计缺陷，生成反思存入知识库
- Agent 行为优化：根据用户反馈（"太啰嗦""不够具体"）自我调整写作风格

## 参考

- [Reflexion: Language Agents with Verbal Reinforcement Learning](https://arxiv.org/abs/2303.11366)
- 关联概念：`Self-Refine自改进.md`, `ReAct推理行动协同.md`
