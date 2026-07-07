using Godot;
using GuandanKitty.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GuandanKitty;

/// <summary>
/// 战斗逻辑控制器 —— 持有所有战斗数据，暴露 C# 事件供 UI 绑定。
/// 不直接依赖 Godot 信号，通过 Fire* 方法触发事件。
/// </summary>
public partial class Battle : Node
{
    // ═══════════════════════════════════════════
    //  战斗数据
    // ═══════════════════════════════════════════

    public List<Agent> Agents { get; } = new();
    public CardRiver River { get; } = new();
    public ChainTracker Chain { get; } = new();
    public int CurrentAgentIndex { get; set; }

    private BattleFSM _fsm = null!;
    private int _lastPlayerPlayDamage;  // 最后一手玩家出牌的即时伤害值

    public Agent? PlayerAgent => Agents.FirstOrDefault(a => a.IsPlayer);
    public Agent? CurrentAgent =>
        CurrentAgentIndex >= 0 && CurrentAgentIndex < Agents.Count
            ? Agents[CurrentAgentIndex] : null;

    public int TotalEnemyHP => Agents
        .Where(a => a.IsEnemy)
        .Sum(a => a.Monsters.Sum(m => m.CurrentHP));

    public bool IsPlayerInputEnabled { get; private set; }

    // ═══════════════════════════════════════════
    //  C# 事件（UI 订阅）
    // ═══════════════════════════════════════════

    /// <summary>请求播放摸牌动画。回调通知完成。</summary>
    public event Action<Card, Action>? CardDrawRequested;
    /// <summary>状态消息更新</summary>
    public event Action<string>? StatusMessageChanged;
    /// <summary>伤害 (damage, remainingHP)</summary>
    public event Action<int, int>? DamageDealt;
    /// <summary>手牌更新</summary>
    public event Action? HandUpdated;
    /// <summary>牌河更新</summary>
    public event Action? RiverUpdated;
    /// <summary>Agent 出牌 (agentId, patternDesc, cardCount)</summary>
    public event Action<string, string, int>? AgentPlayed;
    /// <summary>Agent Pass (agentId)</summary>
    public event Action<string>? AgentPassed;
    /// <summary>回合结果 (playerWon, message)</summary>
    public event Action<bool, string>? RoundResult;
    /// <summary>战斗结束 (playerWon)</summary>
    public event Action<bool>? BattleEnded;
    /// <summary>敌人 HP 变化 (totalHP)</summary>
    public event Action<int>? EnemyHpChanged;
    /// <summary>玩家输入启用/禁用 (enabled)</summary>
    public event Action<bool>? PlayerInputChanged;

    // ═══════════════════════════════════════════
    //  生命周期
    // ═══════════════════════════════════════════

    public override void _Ready()
    {
        _fsm = new BattleFSM();
        AddChild(_fsm);

        _fsm.AddState(new InitState());
        _fsm.AddState(new DrawInitialState());
        _fsm.AddState(new RoundStartState());
        _fsm.AddState(new PlayerTurnState());
        _fsm.AddState(new EnemyTurnState());
        _fsm.AddState(new RoundSettlementState());
        _fsm.AddState(new RoundEndState());
        _fsm.AddState(new BattleEndState());
    }

    /// <summary>
    /// 启动战斗。由外部（UI 或 RunManager）调用，传入参战 Agent 列表。
    /// </summary>
    public void StartBattle(List<Agent> agents)
    {
        Agents.Clear();
        Agents.AddRange(agents);
        CurrentAgentIndex = 0;
        _lastPlayerPlayDamage = 0;

        _fsm.TransitionTo<InitState>();
    }

    // ═══════════════════════════════════════════
    //  玩家输入（UI 调用 → 转发给当前状态）
    // ═══════════════════════════════════════════

    public string? PlayerPlay(List<Card> cards) => _fsm.CurrentState?.OnPlayerPlay(cards);
    public string? PlayerPass() => _fsm.CurrentState?.OnPlayerPass();
    public string? PlayerCallCards() => _fsm.CurrentState?.OnPlayerCallCards();

    // ═══════════════════════════════════════════
    //  被状态调用的游戏逻辑方法
    // ═══════════════════════════════════════════

    /// <summary>注册一次玩家出牌的即时伤害值，用于赢回合后补 ×2</summary>
    public void PushPlayerPlayDamage(int damage)
    {
        _lastPlayerPlayDamage = damage;
    }

    /// <summary>
    /// 对最后一手补乘 ×2（赢回合 bonus）。
    /// 清空手牌已在出牌时 ×10，不重复叠加。
    /// </summary>
    public void ApplyWinningHandBonus()
    {
        if (_lastPlayerPlayDamage > 0 && !Chain.ClearHandPlayed)
        {
            ApplyDamageToEnemies(_lastPlayerPlayDamage);
        }
    }

    /// <summary>
    /// 对敌人造成伤害（从第一个怪物的 HP 开始扣）。
    /// </summary>
    public void ApplyDamageToEnemies(int damage)
    {
        int remaining = damage;
        foreach (var enemy in Agents.Where(a => a.IsEnemy))
        {
            foreach (var monster in enemy.Monsters)
            {
                if (remaining <= 0) break;
                int currentHP = monster.CurrentHP;
                int deducted = Math.Min(remaining, currentHP);
                remaining -= deducted;
                monster.AdjustHP(-deducted);
            }
            if (remaining <= 0) break;
        }

        FireEnemyHpChanged(TotalEnemyHP);
    }

    /// <summary>控制玩家输入启用/禁用</summary>
    public void EnablePlayerInput(bool enabled)
    {
        IsPlayerInputEnabled = enabled;
        PlayerInputChanged?.Invoke(enabled);
    }

    // ═══════════════════════════════════════════
    //  事件发射
    // ═══════════════════════════════════════════

    public void RequestCardDrawAnimation(Card card, Action onComplete)
    {
        CardDrawRequested?.Invoke(card, onComplete);
    }

    public void FireStatusMessage(string msg)
    {
        StatusMessageChanged?.Invoke(msg);
    }

    public void FireDamageDealt(int damage, int remainingHP)
    {
        DamageDealt?.Invoke(damage, remainingHP);
    }

    public void FireHandUpdated()
    {
        HandUpdated?.Invoke();
    }

    public void FireRiverUpdated()
    {
        RiverUpdated?.Invoke();
    }

    public void FireAgentPlayed(string agentId, string desc, int cardCount)
    {
        AgentPlayed?.Invoke(agentId, desc, cardCount);
    }

    public void FireAgentPassed(string agentId)
    {
        AgentPassed?.Invoke(agentId);
    }

    public void FireRoundResult(bool playerWon, string msg)
    {
        RoundResult?.Invoke(playerWon, msg);
    }

    public void FireBattleEnded(bool playerWon)
    {
        BattleEnded?.Invoke(playerWon);
    }

    public void FireEnemyHpChanged(int totalHP)
    {
        EnemyHpChanged?.Invoke(totalHP);
    }
}
