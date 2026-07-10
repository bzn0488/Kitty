# TurnFSM 设计文档

> 版本：v1.0 | 日期：2026-07-11
> 关联：`战斗系统程序文档.md`、`_施工文档Battle架构重构.md`
> 状态：**设计确认，待施工**

---

## 一、设计原则

| 原则 | 说明 |
|------|------|
| **状态机只管切换** | TurnFSM 只在状态间跳转，不处理任何游戏逻辑 |
| **逻辑在 Battle** | 所有验证、伤害、效果判定都在 Battle 的方法里 |
| **无事件穿透** | 玩家操作不通过事件链传递，TurnState 直接调 Battle 方法 |
| **敌人 AI 同步处理** | 敌人回合不在 TurnStart 等待输入，AI 决策后同样走 AfterPlay→End |

---

## 二、状态流转

```
                    ┌──────────────────────────────────┐
                    │          TurnFSM 循环             │
                    │                                  │
    BattleFSM       │  ┌──────────┐                   │
  进入 AgentTurn ──→│  │TurnJudge │←─────────────────┐│
                    │  └────┬─────┘                  ││
                    │       │                        ││
                    │       ↓ true                   ││
                    │  ┌──────────┐                  ││
                    │  │TurnStart │ 玩家/敌人行动     ││
                    │  └────┬─────┘                  ││
                    │       │                        ││
                    │   出牌 ↓      叫牌/Pass         ││
                    │  ┌──────────────┐    │          ││
                    │  │TurnAfterPlay │    │          ││
                    │  └──────┬───────┘    │          ││
                    │         │            │          ││
                    │         └────┬───────┘          ││
                    │              ↓                  ││
                    │       ┌──────────┐              ││
                    │       │ TurnEnd  │──────────────┘│
                    │       └────┬─────┘               │
                    │            │                     │
                    │            ↓ false               │
                    │    RaiseTurnComplete()           │
                    └──────────┬───────────────────────┘
                               ↓
                    BattleFSM → RoundSettlement
```

### 2.1 路径说明

| 路径 | 条件 | 下一状态 |
|------|------|:--:|
| **Judge→Start** | `Battle.JudgeTurn()` 返回 true | TurnStart |
| **Judge→结束** | `Battle.JudgeTurn()` 返回 false | RaiseTurnComplete → RoundSettlement |
| **Start→AfterPlay** | 出牌成功（玩家或敌人） | TurnAfterPlay |
| **Start→End** | 叫牌成功 或 Pass | TurnEnd |
| **AfterPlay→End** | 伤害结算完成 | TurnEnd |
| **End→Judge** | 推进到下一 Agent 后，还有活跃者 | TurnJudge |
| **End→结束** | 推进后无更多活跃者 | RaiseTurnComplete → RoundSettlement |

### 2.2 当前不包含（预留）

| 特性 | 状态 |
|------|:--:|
| 自压（玩家唯一活跃时继续出牌） | 暂时禁用，代码注释保留 |
| 回合开始/结束效果事件 | 预留接口，构筑层实现 |

---

## 三、TurnFSM 类设计

```csharp
public class TurnFSM
{
    public TurnState? CurrentState { get; }
    
    // 仅状态切换
    void TransitionTo<T>() where T : TurnState;
    void Start();        // 进入 TurnJudge
    void Update(float delta); // 每帧推进(仅 TurnStart 用于敌人倒计时)
    
    // 结束本轮
    void CompleteTurn();  // 触发 TurnComplete 事件 → BattleFSM
    
    // 唯一事件：通知 BattleFSM 本轮结束
    event Action? TurnComplete;
}
```

**去掉了 8 个事件**（JudgeRequested, ActRequested, PlayerPlayRequested 等）。
**去掉了 Turn 阶段数据字段**（LastActionWasPass 等）——这些改为 Battle 的局部状态。

---

## 四、TurnState 基类

```csharp
public abstract class TurnState
{
    protected TurnFSM FSM { get; }
    
    void Initialize(TurnFSM fsm);
    void OnEnter();
    void OnExit();
    void Update(float delta); // 仅 TurnStartState 需要
}
```

不再暴露 `HandlePlayerPlay` / `HandlePlayerPass` / `HandlePlayerCall` —— 这些由 Battle 的公开方法直接处理。

---

## 五、各状态与 Battle 方法对应

### 5.1 TurnJudgeState

```csharp
class TurnJudgeState : TurnState
{
    void OnEnter()
    {
        bool canContinue = Battle.JudgeTurn();
        if (canContinue)
            FSM.TransitionTo<TurnStartState>();
        else
            FSM.CompleteTurn();
    }
}
```

**`Battle.JudgeTurn()`**：
```
ActiveAgents 中跳过 Passed 的，取第一个未 Pass 的
  ├─ 没有任何未 Pass 的 → return false（回合结束）
  ├─ 唯一未 Pass 的是敌人 → return false（回合结束，敌人赢）
  └─ 存在未 Pass 的玩家 → return true（继续）
```

### 5.2 TurnStartState

```csharp
class TurnStartState : TurnState
{
    void OnEnter()
    {
        Battle.OnTurnStart(); // 触发"回合开始"效果 + 设置当前操作 Agent
    }
    
    void Update(float delta)
    {
        Battle.OnTurnStartUpdate(delta); // 敌人 AI 倒计时
    }
}
```

**Battle 在 TurnStart 期间暴露的方法**（由 UI 或敌人 AI 调用）：

| 方法 | 调用者 | 效果 |
|------|:--:|------|
| `Agent.TryPlayCards(List<Card>) → string?` | UI 按钮 / 敌人 AI | 验证→出牌→`FSM.TransitionTo<TurnAfterPlay>()` |
| `Agent.TryPass() → string?` | UI 按钮 / 敌人 AI | Pass→`FSM.TransitionTo<TurnEndState>()` |
| `PlayerAgent.TryCallCards() → string?` | UI 按钮 | 叫牌→`FSM.TransitionTo<TurnEndState>()` |

敌人 AI 在 `OnTurnStartUpdate` 中自动决策：能出牌则调用 `TryPlayCards`（→ AfterPlay），不能则调用 `TryPass`（→ End）。

### 5.3 TurnAfterPlayState

```csharp
class TurnAfterPlayState : TurnState
{
    void OnEnter()
    {
        Battle.OnTurnAfterPlay(); // 结算伤害 + 出牌后效果
        FSM.TransitionTo<TurnEndState>();
    }
}
```

**`Battle.OnTurnAfterPlay()`**：
- 计算即时伤害 → 扣敌方 HP
- 通知 UI（伤害跳字、HP 更新）
- 检查清空手牌 → ×10 + 补抽
- 触发"出牌后"效果事件（预留）

### 5.4 TurnEndState

```csharp
class TurnEndState : TurnState
{
    void OnEnter()
    {
        Battle.OnTurnEnd();      // 触发"轮结束"效果
        Battle.AdvanceToNext();  // 推进到下一 Agent
        FSM.TransitionTo<TurnJudgeState>();
    }
}
```

**`Battle.AdvanceToNext()`**：
- 当前 Agent 移到 ActiveAgents 末尾（循环）
- 如果 ActiveAgents 只剩玩家一个 → `FSM.CompleteTurn()`

---

## 六、Agent 行动方法

所有 Try 方法都在 Agent 上，统一流程，Battle 只做回合级编排。

### 6.1 Agent（基类）

| 方法 | 说明 |
|------|------|
| `TryPlayCards(List<Card>) → string?` | 验证牌型+压制 → 成功则扣手牌、写入牌河/Chain → null；失败返回错误信息 |
| `TryPass() → string?` | 标记 Pass → 从 ActiveAgents 移除 |
| `CanSuppressCurrent(CardPattern) → bool` | 检查能否压制当前 Chain.LastPlayed |

### 6.2 PlayerAgent

| 方法 | 说明 |
|------|------|
| `TryCallCards() → string?` | 叫牌 → 从 Deck 抽牌 |

### 6.3 EnemyAgent

| 方法 | 说明 |
|------|------|
| `DecideAndPlay() → bool` | AI 决策：搜索手牌 → 找到则调用 `TryPlayCards` → 转 AfterPlay；找不到则 `TryPass` → 转 End。返回是否出了牌 |

---

## 七、Battle 新增/修改的方法

| 方法 | 类型 | 说明 |
|------|:--:|------|
| `JudgeTurn() → bool` | **新增** | 判断当前轮是否继续 |
| `OnTurnStart()` | **新增** | 轮开始回调，区分玩家/敌人 |
| `OnTurnStartUpdate(float)` | **新增** | 敌人 AI 倒计时，到时间调 `EnemyAgent.DecideAndPlay()` |
| `OnTurnAfterPlay()` | **新增** | 结算当前手牌的伤害和效果 |
| `AdvanceToNext()` | **新增** | 推进到下一 Agent |
| `OnRoundSettlement()` | 保留 | 回合胜负判定 |
| `OnRoundEnd()` | 保留 | 回合结束清理 |

---

## 八、和旧架构对比

| 维度 | 旧（事件穿透） | 新（直接调用） |
|------|------|------|
| TurnFSM 事件数 | 9 个 | 1 个（TurnComplete） |
| 玩家出牌调用链 | 7 层 | 3 层（UI→Battle.TryPlayCards→TurnFSM.TransitionTo） |
| 返回值路径 | Func 事件链回传 | 直接 return |
| Turn 阶段数据 | 存在 TurnFSM 上 | Battle 局部变量/字段 |
| 敌人 AI | 在 TurnAct.Update 中 | 在 OnTurnStartUpdate 中 |

---

*本文档为设计文档，待施工。*
