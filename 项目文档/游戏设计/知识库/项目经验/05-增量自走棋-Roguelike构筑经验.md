---
title: 增量自走棋 — Roguelite构筑经验
tags: [roguelite, roguelike, project-experience]
created: 2026-05-24
status: draft
---

# 增量自走棋 — Roguelite构筑经验

> 项目经验（Roguelite）
> 来源：增量自走棋
> 关联知识：`理论知识/01-Roguelike设计规范.md` §一

**本项目的定位**：增量自走棋属于 **Roguelite**（有局外成长/元进程），而非传统 Roguelike。

## 核心原则

- **失败有成长原则**（Roguelite 典型）——玩家从失败中获得局外解锁/升级，下一次更强
- 随机性与确定性平衡——关键决策应包含可预测的回报

## 构筑设计

- 构筑核心：初始强度 + 增速 + 随机决策
- 协同效应应基于共享中间层，而非指名卡牌联动

（详细内容见 `项目/增量自走棋/系统/单位设计系统/`）
