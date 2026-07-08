# 施工文档：Battle 架构重构

> 版本：v1.0（施工完成） | 日期：2026-07-09
> 关联：`战斗系统程序文档.md`、`内线玩法GDD.md`
> 状态：**已完成**

---

## 一、重构目标

解决当前代码的结构问题：
1. **Battle 不应继承 Node** — 纯逻辑层，无挂载需求
2. **BattleFSM 也不应继承 Node** — 状态机只是数据结构 + 回调分发，无场景依赖
3. **初始化方向反了** — 应是 Battle 创建并注入 UI/FSM，而非 UI 捞 Battle
4. **调用链路过长** — 7 层跳转需精简

---

## 二、三层职责（已确认）

```
Run (Autoload)
 │ 持有 Battle 引用
 │ Run._Process → battle.Update(delta)
 │ Run.EndBattle() → battle.End()
 │
 └─ 创建 → Battle（纯 C# 类）
     │
     ├─ 持有 → BattleFSM（纯 C# 类）
     │          · 注册状态 + 切换 + 回调分发
     │          · 不含游戏逻辑
     │
     ├─ 持有 → BattleUI (Control, 挂在场景树)
     │          · 渲染手牌/牌河/血条/动画
     │          · 发射 C# event（按钮点击等）
     │          · 不含游戏逻辑
     │
     └─ 持有 → 游戏状态
                Agents / CardRiver / ChainTracker
                DamageCalculator / CardPatternDetector
```

### 2.1 Battle（大脑）— 纯 C#
- 不继承任何 Godot 类型
- Run 创建它，Run 驱动它（`Update(delta)`）
- 持有 FSM 和 UI 引用
- 所有游戏逻辑在此
- 通过 UIManager（未来）控制界面；现阶段直接持有 BattleUI 引用

### 2.2 BattleFSM（节拍器）— 纯 C#
- 不继承 Node，只是一个普通 C# 类
- `Battle.Update(delta)` → `_fsm.Update(delta)` → `currentState.Update(delta)`
- 只做：注册状态 → 切换状态 → 回调分发到 Battle
- 不含任何游戏逻辑

### 2.3 BattleUI（显示器 + 输入设备）— Control
- 挂在场景树中，默认隐藏
- 渲染手牌、牌河、血条、伤害跳字、动画
- 通过 C# `event Action` 发射输入信号，Battle 订阅
- 不持有 `_battle` 引用，不含游戏逻辑

---

## 三、已确认的架构决策

### 决策 A：delta time 驱动链
```
Run._Process → battle.Update(delta) → _fsm.Update(delta)
```
Run 是 Autoload，全程存活，负责每帧驱动 Battle。

### 决策 B：UI 信号流
```
BattleUI.ButtonPressed → event Action.Invoke() → Battle 订阅处理
```
BattleUI 只发射事件，Battle 订阅。UI 不持有 Battle 引用。

### 决策 C：BattleFSM 不继承 Node
BattleFSM 是纯 C# 类，Battle 直接 `new` 并持有，无场景树依赖。

### 决策 D：UI 控制
- 远期：UIManager 单例管理所有界面
- 现阶段：BattleUI 是一个 Control，挂在场景里，Battle 调 `Show()`/`Hide()`

---

## 四、Battle 生命周期（已确认）

```
Run.StartBattle(player.Deck)
  │
  ├─ battle = new Battle(deck)
  ├─ battle.Initialize()
  │    ├─ 呼出/显示 BattleUI
  │    ├─ _fsm = new BattleFSM(this)
  │    ├─ 注册状态
  │    ├─ 生成测试敌人
  │    └─ _fsm.TransitionTo<BattleStartState>()
  │
  ├─ Run 持有 battle 引用
  │
  ├─ 每帧: Run._Process → battle.Update(delta)
  │
  └─ 战斗结束（胜负判定后）
       ├─ Run.EndBattle() 调用 battle.End()
       ├─ 销毁 _fsm
       ├─ 隐藏 BattleUI
       └─ 传参回 Run（后续扩展：金币/奖励等）
```

### 4.1 Battle 与 Run 的关系
- Battle 是 Run 的下属，由 Run 创建和销毁
- Battle 可在必要时回调 Run（如：战斗效果增加金币）
- 其他情况尽量不调 Run

### 4.2 传参
- **入参**：至少 `StandardDeck`（玩家牌组）
- **怪物**：现阶段 Battle 内部生成测试假人；后续由 Run 传入怪物组合
- **出参**：后续扩展（战斗结果、金币变化等）

---

## 五、场景结构（草案）

```csharp
// Run.cs（Autoload）
public partial class Run : Node
{
    private Battle? _currentBattle;

    public void StartBattle()
    {
        _currentBattle = new Battle(PlayerData.Deck);
        _currentBattle.Initialize(); // 内部显示 UI、创建 FSM、开始
    }

    public void EndBattle()
    {
        _currentBattle?.End();
        _currentBattle = null;
    }

    public override void _Process(double delta)
    {
        _currentBattle?.Update((float)delta);
    }
}
```

```csharp
// Battle.cs（纯 C#）
public class Battle
{
    private readonly StandardDeck _playerDeck;
    private BattleFSM _fsm = null!;
    private BattleUI _ui = null!;
    
    public List<Agent> Agents { get; } = new();
    public CardRiver River { get; } = new();
    public ChainTracker Chain { get; } = new();
    // ...

    public Battle(StandardDeck playerDeck)
    {
        _playerDeck = playerDeck;
    }

    public void Initialize()
    {
        _ui = /* 获取/显示 BattleUI */;
        _ui.PlayRequested += OnPlayRequested;
        _ui.PassRequested += OnPassRequested;
        _ui.CallRequested += OnCallRequested;

        _fsm = new BattleFSM(this);
        _fsm.AddState(new BattleStartState());
        // ... 注册其他状态

        CreateTestAgents();
        _fsm.TransitionTo<BattleStartState>();
    }

    public void Update(float delta)
    {
        _fsm.Update(delta);
    }

    public void End()
    {
        _ui.PlayRequested -= OnPlayRequested;
        // ... 取消订阅
        _ui.Hide();
        _fsm = null!;
    }

    // 游戏逻辑方法...
}
```

---

## 六、已确认的补充决策

### Q1：Battle 如何获取 BattleUI 引用？ → 方案 A
- BattleUI 预先挂在主场景里（默认隐藏），Run 持有引用，创建 Battle 时传入
- 远期由 UIManager 单例替代

### Q2：TurnFSM 归属 → BattleFSM 的子状态机
- TurnFSM 表达一个"轮次"（Judge→Act→Resolve→Advance），是 AgentTurn 的内部循环
- **TurnFSM 由 BattleFSM 持有和管理**，不是 Battle 的直接下属
- BattleFSM 进入 AgentTurnState 时启动 TurnFSM，TurnFSM 判定轮次结束时通知 BattleFSM 转到 RoundSettlement

### Q3：FSM 状态文件组织 → 放在一起
- BattleState 基类 + 所有状态子类放在 `BattleFSM.cs` 同一文件
- TurnState 基类 + 所有 Turn 状态子类放在 `TurnFSM.cs` 同一文件

---

## 七、FSM 层级关系（更新）

```
Battle（纯 C#）
 │ 持有 _fsm
 │
 └─ BattleFSM（纯 C#）
     │ 注册 6 个 BattleState
     │ Update(delta) → currentState.Update(delta)
     │
     ├─ BattleStartState    → Battle.OnBattleStart()
     ├─ RoundStartState     → Battle.OnRoundStart()
     ├─ AgentTurnState      → 启动子状态机 TurnFSM  ← 关键！
     ├─ RoundSettlementState → Battle.OnRoundSettlement()
     ├─ RoundEndState       → Battle.OnRoundEnd()
     └─ BattleEndState      → Battle.OnBattleEnd()
     │
     └─ 持有 TurnFSM（纯 C#，子状态机）
         │ 仅在 AgentTurnState 期间活跃
         │ 注册 4 个 TurnState
         │
         ├─ TurnJudgeState   → Battle.OnTurnJudge()
         ├─ TurnActState     → Battle.OnTurnAct() + 等待输入/AI
         ├─ TurnResolveState → Battle.OnTurnResolve()
         └─ TurnAdvanceState → Battle.OnTurnAdvance()
```

### 关键点
- **Battle 只知道 BattleFSM**，不知道 TurnFSM 的存在
- BattleFSM 的 AgentTurnState 内部管理 TurnFSM 的启停
- TurnFSM 完成时，通过回调通知 BattleFSM 转到 RoundSettlement
- 游戏逻辑始终在 Battle 中，FSM 层只做回调分发

---

## 九、Round 淘汰制规则（已确认）

### 9.1 基础概念
- Round = 包含所有 Agent 的若干 Turn
- Battle 维护 `ActiveAgents` 集合（按座次排序）
- Agent Pass → 从 `ActiveAgents` 移除（淘汰）
- Agent 出牌 → 保留在 `ActiveAgents`，轮到下一个

### 9.2 Turn 判定规则

```
TurnJudge（当前 Agent = ActiveAgents[i]）
  │
  ├─ ActiveAgents.Count == 1
  │    ├─ 唯一 Agent 是玩家 → 继续 TurnAct（允许自压）
  │    └─ 唯一 Agent 是敌人 → 回合结束，进 Settlement
  │
  └─ ActiveAgents.Count > 1 → TurnAct（正常出牌/Pass）
```

### 9.3 自压场景示例

```
ActiveAgents = [玩家, 敌人]

玩家出 ♠5     → 敌人无法压制 → Pass → ActiveAgents = [玩家]
                                          ↓
                                   玩家是唯一活跃者
                                          ↓
                                    继续玩家 Turn（自压机会）
                                          ↓
                              玩家出 ♠K 压自己的 ♠5
                                          ↓
                              玩家仍是唯一活跃者 → 可继续自压
                                          ↓
                              玩家 Pass → 回合结束 → 玩家赢
```

### 9.4 对应 GDD 规则
- "敌人 Pass → 控制权交还玩家，回合不结束" ✅
- "玩家可继续用同牌型更大数值压制自己"（自压）✅
- "玩家 Pass → 回合结束（仅有玩家有权终结回合）" ✅
---

## 十、文件变更清单 + 实施计划

### 10.1 变更总览

| # | 文件 | 操作 | 说明 |
|:--:|------|:--:|------|
| 1 | `Core/Battle/TurnFSM.cs` | **重写** | 去掉 Battle 引用，通过 event 与外部通信 |
| 2 | `Core/Battle/BattleFSM.cs` | **重写** | 去掉 `: Node`，纯 C#；持有 TurnFSM，订阅其事件 |
| 3 | `Core/Battle/Battle.cs` | **重写** | 去掉 `: Node`，纯 C#；ActiveAgents；自压特例 |
| 4 | `Scenes/BattleUI.cs` | **修改** | 去掉 `_battle`，改为 `event Action` |
| 5 | `Core/Run/Run.cs` | **修改** | 创建/驱动/销毁 Battle |
| 6 | `Scenes/BattleScene.tscn` | **修改** | 去掉 Battle 节点 |

### 10.2 Phase 1: TurnFSM 重写
- 去掉 `Battle` 构造参数和属性
- 新增 9 个 `event`：JudgeRequested, ActRequested, ActUpdateRequested, PlayerPlayRequested, PlayerPassRequested, PlayerCallRequested, ResolveRequested, AdvanceRequested, TurnComplete
- TurnState 基类去掉 `Battle` 属性，只持有 `TurnFSM`
- 各 TurnState 改为 `FSM.RaiseXxx()` 而非 `Battle.OnXxx()`

### 10.3 Phase 2: BattleFSM 重写
- 去掉 `: Node`，纯 C# 类
- 构造函数注入 `Battle`，内部 `new TurnFSM()`
- 订阅 TurnFSM 事件转发到 Battle
- 订阅 `TurnComplete` → `TransitionTo<RoundSettlementState>()`
- 暴露 `TurnTransitionTo<T>()` 供 Battle 调度子状态机

### 10.4 Phase 3: Battle 重写
- 去掉 `: Node`，纯 C# 类
- 构造函数接收 `(StandardDeck, BattleUI)`
- 新增 `ActiveAgents` 集合，Round 开始全员加入，Pass 即移除
- TurnJudge 实现自压特例
- 去掉 `public TurnFSM Turn`、`ExitTurn()`、`_turnResolvedXxx` 等

### 10.5 Phase 4: BattleUI 修改
- 去掉 `_battle` 字段和 `InitBattle()`
- 新增 C# event：`PlayRequested`, `PassRequested`, `CallRequested`
- 保留所有 `OnXxx()` 显示方法

### 10.6 Phase 5: Run 修改
- `StartBattle(deck, ui)` 创建 Battle、初始化、驱动
- `_Process` → `_currentBattle?.Update(delta)`
- `EndBattle()` → `battle.End()`

### 10.7 Phase 6: 场景 + 编译验证
- 更新 BattleScene.tscn
- 编译验证全项目

---

## 十二、施工完成总结

### 变更文件

| 文件 | 变更 |
|------|------|
| `Core/Battle/TurnFSM.cs` | 重写：去掉 Battle 依赖，9 个 event + TurnComplete |
| `Core/Battle/BattleFSM.cs` | 重写：去掉 Node，纯 C#，持有 TurnFSM，订阅事件 |
| `Core/Battle/Battle.cs` | 重写：去掉 Node，纯 C#，ActiveAgents 集合，自压特例 |
| `Scenes/BattleUI.cs` | 修改：event 驱动输入，去掉 _battle 引用 |
| `Core/Run/Run.cs` | 修改：创建/驱动/销毁 Battle，_Process 驱动 |
| `Scenes/MainScene.cs` | 修改：创建 BattleUI，注册到 Run |

### 已修复的必修项目（17 项）

| 类别 | 项数 | 状态 |
|------|:--:|:--:|
| A 架构级（Battle/FSM 去 Node） | 8 | ✅ |
| B 职责边界 | 3 | ✅ |
| C 数据流/调用链 | 4 | ✅ |
| D 规则实现（ActiveAgents + 自压） | 3 | ✅ |

### 已知后续工作
- B4/B5：敌人 AI 计时器和回合延时仍在 Battle 中，可迁至表现层
- E1~E3：DamageCalculator/CardPatternDetector 改为实例类 + 修正器管道
- BattleScene.tscn 清理

---

*本文档为施工文档，施工完成。*
