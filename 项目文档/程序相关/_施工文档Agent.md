# 施工文档：Agent 抽象化重构

> 版本：v0.1（讨论通过） | 日期：2026-07-09
> 关联：`战斗系统程序文档.md`、`_施工文档Battle架构重构.md`
> 状态：**方案已确认，待施工**

---

## 一、目标

将 `Agent` 从单一具体类 + `AgentType` 枚举重构为抽象基类 + 两个子类，消除 Battle 中的 Agent 职责泄漏。

### 当前问题
- `Agent` 用 `AgentType` 枚举区分 Player/Enemy，内部充斥 `if (IsPlayer)` 分支
- Battle 持有大量本应属于 Agent 的方法（抽牌、AI、伤害、战败效果）
- `EnemyAI` 和 `DefeatEffectExecutor` 是独立静态类，与 Agent 脱节
- `AgentType` 枚举限制扩展（Friendly 类型需要改多处）

---

## 二、目标结构

```
Agent（抽象基类，纯 C#）
 ├─ 构造时注入 Battle 引用
 ├─ Id, Hands[], HasPassed, IsActive
 ├─ abstract bool IsDefeated { get; }
 │
 ├─ PlayerAgent
 │    ├─ Deck（StandardDeck）
 │    ├─ MaxCallCards / RemainingCallCards / CardsPerCall
 │    ├─ CanCallCards
 │    ├─ DrawFromDeck(int n)
 │    ├─ CallCards()
 │    ├─ ResetCallCounts()
 │    └─ IsDefeated = Deck空 && 所有 Hands 空
 │
 └─ EnemyAgent
      ├─ Monsters[]
      ├─ TotalHP
      ├─ DrawInitialHand(Random)
      ├─ DrawGrowthCards(Random)
      ├─ FindBestPlay(CardPattern target)   ← 从 EnemyAI 迁入
      ├─ ApplyDamage(int damage)
      ├─ ExecuteDefeatEffects(PlayerAgent)  ← 从 DefeatEffectExecutor 迁入
      └─ IsDefeated = false
```

---

## 三、API 设计

### 3.1 Agent（抽象基类）

```csharp
public abstract class Agent
{
    public string Id { get; set; } = "";
    public List<HandZone> Hands { get; } = new();
    public Battle Battle { get; }
    
    public bool HasPassed { get; set; }
    public bool IsActive => !HasPassed;
    public abstract bool IsDefeated { get; }
    
    protected Agent(Battle battle, string id)
    {
        Battle = battle;
        Id = id;
    }
}
```

### 3.2 PlayerAgent

```csharp
public class PlayerAgent : Agent
{
    public StandardDeck Deck { get; }
    public int MaxCallCards { get; set; } = 3;
    public int RemainingCallCards { get; set; } = 3;
    public int CardsPerCall { get; set; } = 6;
    
    public bool CanCallCards => RemainingCallCards > 0;
    public override bool IsDefeated => Deck.IsEmpty && Hands.All(h => h.IsEmpty);
    
    public PlayerAgent(Battle battle, StandardDeck deck, string id = "玩家")
        : base(battle, id)
    {
        Deck = deck.Clone();
        Hands.Add(new HandZone());
    }
    
    /// <summary>从牌堆抽 n 张牌</summary>
    public List<Card> DrawFromDeck(int n)
    {
        return Deck.Draw(n);
    }
    
    /// <summary>叫牌：抽 CardsPerCall 张，减次数</summary>
    public List<Card> CallCards()
    {
        if (!CanCallCards) return new List<Card>();
        RemainingCallCards--;
        return Deck.Draw(CardsPerCall);
    }
    
    /// <summary>重置叫牌次数</summary>
    public void ResetCallCounts()
    {
        RemainingCallCards = MaxCallCards;
    }
}
```

### 3.3 EnemyAgent

```csharp
public class EnemyAgent : Agent
{
    public List<Monster> Monsters { get; } = new();
    public int TotalHP => Monsters.Sum(m => m.CurrentHP);
    public override bool IsDefeated => false;
    
    public EnemyAgent(Battle battle, string id, List<Monster> monsters)
        : base(battle, id)
    {
        Hands.Add(new HandZone());
        Monsters.AddRange(monsters);
    }
    
    /// <summary>战斗开始：从怪物牌池抽初始手牌</summary>
    public void DrawInitialHand(Random rng)
    {
        foreach (var monster in Monsters)
            for (int i = 0; i < 3; i++)
                Hands[0].Add(monster.DrawFromPool(rng));
    }
    
    /// <summary>每回合结束：从怪物牌池成长抽牌</summary>
    public void DrawGrowthCards(Random rng)
    {
        foreach (var monster in Monsters)
            for (int i = 0; i < monster.DrawsPerRound; i++)
                Hands[0].Add(monster.DrawFromPool(rng));
    }
    
    /// <summary>AI：寻找能压制 target 的最小牌型，或 null（Pass）</summary>
    public (int handIndex, CardPattern pattern)? FindBestPlay(CardPattern target)
    {
        // 从 EnemyAI 迁入
    }
    
    /// <summary>承受伤害，从第一个怪物开始扣</summary>
    public void ApplyDamage(int damage)
    {
        int remaining = damage;
        foreach (var monster in Monsters)
        {
            if (remaining <= 0) break;
            int deducted = Math.Min(remaining, monster.CurrentHP);
            remaining -= deducted;
            monster.AdjustHP(-deducted);
        }
    }
    
    /// <summary>执行所有怪物的战败效果</summary>
    public void ExecuteDefeatEffects(PlayerAgent player)
    {
        int totalRemoved = 0;
        foreach (var monster in Monsters)
        {
            if (monster.DefeatEffect == null) continue;
            var toRemove = monster.DefeatEffect.SelectCardsToRemove(player.Hands[0]);
            if (toRemove.Count == 0) continue;
            player.Hands[0].Remove(toRemove);
            player.Deck.RemovePermanently(toRemove);
            totalRemoved += toRemove.Count;
        }
        if (totalRemoved > 0)
        {
            var drawn = player.Deck.Draw(totalRemoved);
            player.Hands[0].AddRange(drawn);
        }
    }
}
```

---

## 四、Battle 变化

### 4.1 删除的方法（职责迁移到 Agent）

| 原 Battle 方法 | 迁至 |
|------|------|
| `DrawEnemyInitialHands()` | `EnemyAgent.DrawInitialHand(Random)` |
| `EnemyGrowthDraw()` | `EnemyAgent.DrawGrowthCards(Random)` |
| `StartPlayerDrawSequence()` | 内联为批量 `DrawFromDeck(8)` |
| `DrawNextStartCard()` | **删除**（去掉逐张动画） |
| `OnStartCardDrawn()` | **删除** |
| `CanPlayerDraw()` | `PlayerAgent.Deck.IsEmpty` 判断 |
| `ResetPlayerCallCounts()` | `PlayerAgent.ResetCallCounts()` |
| `ApplyDamageToEnemies(int)` | `EnemyAgent.ApplyDamage(int)` |
| `ExecuteAllDefeatEffects()` | `EnemyAgent.ExecuteDefeatEffects(PlayerAgent)` |
| `PushPlayerPlayDamage()` / `ApplyWinningHandBonus()` | 内联到 `OnTurnPlayerPass` |

### 4.2 简化的调用

```csharp
// OnBattleStart —— 之前 ~20 行，之后
internal void OnBattleStart()
{
    var rng = new Random();
    foreach (var enemy in Agents.OfType<EnemyAgent>())
        enemy.DrawInitialHand(rng);
    
    // 玩家批量抽 8 张（去掉逐张动画）
    var player = PlayerAgent!;
    var drawn = player.DrawFromDeck(8);
    player.Hands[0].AddRange(drawn);
    
    NotifyHandUpdated();
    _fsm.TransitionTo<RoundStartState>();
}

// OnTurnPlayerCall —— 之前
internal string? OnTurnPlayerCall()
{
    if (IsRoundOver) return "回合已结束";
    var player = PlayerAgent;
    if (player == null || !ActiveAgents.Contains(player)) return "现在不是你的回合";
    if (!player.CanCallCards) return "叫牌次数已用完";
    var drawn = player.Deck!.Draw(player.CardsPerCall);
    player.Hands[0].AddRange(drawn);
    player.RemainingCallCards--;
    NotifyHandUpdated();
    return null;
}

// 之后：直接调 Agent 方法
internal string? OnTurnPlayerCall()
{
    if (IsRoundOver) return "回合已结束";
    var player = PlayerAgent;
    if (player == null || !ActiveAgents.Contains(player)) return "现在不是你的回合";
    if (!player.CanCallCards) return "叫牌次数已用完";
    
    var drawn = player.CallCards();
    player.Hands[0].AddRange(drawn);
    NotifyHandUpdated();
    return null;
}
```

### 4.3 删除的字段

| 字段 | 原因 |
|------|------|
| `_drawCardsTotal`, `_drawCardsCurrent` | 逐张动画相关，去掉 |
| `_lastPlayerPlayDamage` | 赢回合 ×2 逻辑内联 |
| `_playerDeck` | 改为从 PlayerAgent 获取 |
| `PlayerAgent` 属性类型 | `Agent?` → `PlayerAgent?` |
| `IsPlayer` / `IsEnemy` 判断 | 改为 `is PlayerAgent` / `is EnemyAgent` 或 `OfType<T>()` |

---

## 五、删除的文件

| 文件 | 原因 |
|------|------|
| `Core/EnemyAI.cs` | 逻辑迁入 `EnemyAgent.FindBestPlay()` |
| `Core/DefeatEffect.cs` 中的 `DefeatEffectExecutor` | 逻辑迁入 `EnemyAgent.ExecuteDefeatEffects()` |

> `DefeatEffect` 基类和子类（RandomDiscardEffect 等）保留，它们是数据定义。

---

## 六、Monster 拆分

`Monster` 类当前混在 `Agent.cs` 中。建议拆出为 `Core/Monster.cs`。

---

## 七、文件变更清单

| 文件 | 操作 | 说明 |
|------|:--:|------|
| `Core/Agent.cs` | **重写** | 抽象基类（Id, Hands, HasPassed, Battle） |
| `Core/PlayerAgent.cs` | **新建** | 玩家特化（Deck, CallCards, DrawFromDeck） |
| `Core/EnemyAgent.cs` | **新建** | 敌人特化（Monsters, AI, 伤害, 战败效果） |
| `Core/Monster.cs` | **新建** | Monster 类从 Agent.cs 拆出 |
| `Core/EnemyAI.cs` | **删除** | 逻辑迁入 EnemyAgent |
| `Core/DefeatEffect.cs` | **修改** | 删除 DefeatEffectExecutor 静态类 |
| `Core/Battle/Battle.cs` | **修改** | 删除 ~10 个方法，精简 ~150 行 |
| `Scenes/BattleUI.cs` | **修改** | `SetPlayerAgent(Agent)` → `SetPlayerAgent(PlayerAgent)` |

---

## 八、实施计划

| Phase | 内容 | 依赖 |
|:--:|------|:--:|
| 1 | 新建 `Monster.cs`，从 `Agent.cs` 移出 Monster 类 | — |
| 2 | 重写 `Agent.cs`：抽象基类 + PlayerAgent + EnemyAgent | Phase 1 |
| 3 | 删除 `EnemyAI.cs`，迁入 EnemyAgent | Phase 2 |
| 4 | 删除 `DefeatEffectExecutor`，迁入 EnemyAgent | Phase 2 |
| 5 | 精简 `Battle.cs`，替换所有 Agent 调用 | Phase 2-4 |
| 6 | 编译验证 + 更新引用 | Phase 5 |

---

*本文档为施工文档，待施工。*
