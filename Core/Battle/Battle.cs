using Godot;
using GuandanKitty.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GuandanKitty;

/// <summary>
/// 战斗逻辑控制器 —— 持有战斗数据，直接调用 BattleUI 方法刷新显示。
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
    private int _lastPlayerPlayDamage;

    public Agent? PlayerAgent => Agents.FirstOrDefault(a => a.IsPlayer);
    public Agent? CurrentAgent =>
        CurrentAgentIndex >= 0 && CurrentAgentIndex < Agents.Count
            ? Agents[CurrentAgentIndex] : null;

    public int TotalEnemyHP => Agents
        .Where(a => a.IsEnemy)
        .Sum(a => a.Monsters.Sum(m => m.CurrentHP));

    public bool IsPlayerInputEnabled { get; private set; }

    /// <summary>UI 引用，由 BattleUI 创建后注入</summary>
    public BattleUI? UI { get; set; }

    // ═══════════════════════════════════════════
    //  生命周期
    // ═══════════════════════════════════════════

    public override void _Ready()
    {
        _fsm = new BattleFSM();
        AddChild(_fsm);

        _fsm.AddState(new InitState());
        _fsm.AddState(new BattleStartState());
        _fsm.AddState(new RoundStartState());
        _fsm.AddState(new AgentTurnState());
        _fsm.AddState(new RoundSettlementState());
        _fsm.AddState(new RoundEndState());
        _fsm.AddState(new BattleEndState());

        _fsm.TransitionTo<InitState>();
    }

    public void StartBattle(List<Agent> agents)
    {
        Agents.Clear();
        Agents.AddRange(agents);
        CurrentAgentIndex = 0;
        _lastPlayerPlayDamage = 0;
    }

    // ═══════════════════════════════════════════
    //  玩家输入（UI 调用 → 当前状态）
    // ═══════════════════════════════════════════

    public string? PlayerPlay(List<Card> cards) => _fsm.CurrentState?.OnPlayerPlay(cards);
    public string? PlayerPass() => _fsm.CurrentState?.OnPlayerPass();
    public string? PlayerCallCards() => _fsm.CurrentState?.OnPlayerCallCards();

    // ═══════════════════════════════════════════
    //  逻辑方法（状态机调用）
    // ═══════════════════════════════════════════

    public void PushPlayerPlayDamage(int damage) => _lastPlayerPlayDamage = damage;

    public void ApplyWinningHandBonus()
    {
        if (_lastPlayerPlayDamage > 0 && !Chain.ClearHandPlayed)
            ApplyDamageToEnemies(_lastPlayerPlayDamage);
    }

    public void ApplyDamageToEnemies(int damage)
    {
        int remaining = damage;
        foreach (var enemy in Agents.Where(a => a.IsEnemy))
        {
            foreach (var monster in enemy.Monsters)
            {
                if (remaining <= 0) break;
                int deducted = Math.Min(remaining, monster.CurrentHP);
                remaining -= deducted;
                monster.AdjustHP(-deducted);
            }
            if (remaining <= 0) break;
        }
        UI?.OnEnemyHpChanged(TotalEnemyHP);
    }

    public void EnablePlayerInput(bool enabled)
    {
        IsPlayerInputEnabled = enabled;
        UI?.OnPlayerInputChanged(enabled);
    }

    /// <summary>请求 UI 播放摸牌动画，动画完成后回调 onComplete</summary>
    public void RequestCardDrawAnimation(Card card, Action onComplete)
        => UI?.OnCardDrawRequested(card, onComplete);

    // ═══════════════════════════════════════════
    //  UI 通知（直接调用 BattleUI 方法）
    // ═══════════════════════════════════════════

    public void NotifyStatus(string msg)                 => UI?.OnStatusMessage(msg);
    public void NotifyDamage(int dmg, int rem)            => UI?.OnDamageDealt(dmg, rem);
    public void NotifyAgentPlayed(string id, string desc, int cnt) => UI?.OnAgentPlayed(id, desc, cnt);
    public void NotifyAgentPassed(string id)              => UI?.OnAgentPassed(id);
    public void NotifyRoundResult(bool won, string msg)   => UI?.OnRoundResult(won, msg);
    public void NotifyBattleEnded(bool won)               => UI?.OnBattleEnded(won);
    public void NotifyHandUpdated()                       => UI?.OnHandUpdated();
    public void NotifyRiverUpdated()                      => UI?.OnRiverUpdated();
}
