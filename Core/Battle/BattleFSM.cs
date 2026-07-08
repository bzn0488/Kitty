using Godot;
using GuandanKitty.Core;
using System;
using System.Collections.Generic;

namespace GuandanKitty;

/// <summary>
/// 战斗状态机 —— 只负责状态流转，不包含游戏逻辑。
/// 所有逻辑在 Battle 中实现，状态只做回调分发。
/// </summary>
public partial class BattleFSM : Node
{
    /// <summary>持有 Battle 引用</summary>
    public Battle Battle { get; private set; } = null!;

    /// <summary>当前状态</summary>
    public BattleState? CurrentState { get; private set; }

    private readonly Dictionary<Type, BattleState> _states = new();

    public override void _Ready()
    {
        Battle = GetParent<Battle>();
    }

    /// <summary>注册一个状态</summary>
    public void AddState(BattleState state)
    {
        state.Initialize(this, Battle);
        _states[state.GetType()] = state;
    }

    /// <summary>切换到指定类型的状态</summary>
    public void TransitionTo<T>() where T : BattleState
    {
        CurrentState?.OnExit();
        CurrentState = _states[typeof(T)];
        GD.Print($"[BattleFSM] → {typeof(T).Name}");
        CurrentState.OnEnter();
    }

    public override void _Process(double delta)
    {
        CurrentState?.Update((float)delta);
    }
}

// ═════════════════════════════════════════════════
//  状态基类
// ═════════════════════════════════════════════════

/// <summary>
/// 状态基类。所有状态继承此类，重写对应回调方法。
/// 禁止包含游戏逻辑，只做回调分发到 Battle。
/// </summary>
public abstract class BattleState
{
    /// <summary>持有 FSM 引用</summary>
    protected BattleFSM FSM { get; private set; } = null!;

    /// <summary>持有 Battle 引用</summary>
    protected Battle Battle { get; private set; } = null!;

    /// <summary>初始化状态引用</summary>
    public void Initialize(BattleFSM fsm, Battle battle)
    {
        FSM = fsm;
        Battle = battle;
    }

    /// <summary>进入状态时调用</summary>
    public virtual void OnEnter() { }

    /// <summary>离开状态时调用</summary>
    public virtual void OnExit() { }

    /// <summary>每帧更新</summary>
    public virtual void Update(float delta) { }

    /// <summary>玩家出牌回调（仅 PlayerTurn 时有效）</summary>
    public virtual string? OnPlayerPlay(List<Card> cards)
    {
        return "现在不是你的回合";
    }

    /// <summary>玩家 Pass 回调</summary>
    public virtual string? OnPlayerPass()
    {
        return "现在不是你的回合";
    }

    /// <summary>玩家叫牌回调</summary>
    public virtual string? OnPlayerCallCards()
    {
        return "现在不是你的回合";
    }
}

// ═════════════════════════════════════════════════
//  状态实现 —— 每个状态只做回调分发，无游戏逻辑
// ═════════════════════════════════════════════════


/// <summary>2. 战斗开始（布置 + 抽起始手牌）</summary>
class BattleStartState : BattleState
{
    public override void OnEnter()
    {
        Battle.OnBattleStart();
    }
}

/// <summary>3. 回合开始</summary>
class RoundStartState : BattleState
{
    public override void OnEnter()
    {
        Battle.OnRoundStart();
    }
}

/// <summary>
/// 4. Agent 轮次
/// 统一处理玩家、敌人、友方的出牌轮次。
/// </summary>
class AgentTurnState : BattleState
{
    public override void OnEnter()
    {
        Battle.OnAgentTurnStart();
    }

    public override void Update(float delta)
    {
        Battle.OnAgentTurnUpdate(delta);
    }

    public override string? OnPlayerPlay(List<Card> cards)
    {
        return Battle.OnPlayerPlayCards(cards);
    }

    public override string? OnPlayerPass()
    {
        return Battle.OnPlayerPassTurn();
    }

    public override string? OnPlayerCallCards()
    {
        return Battle.OnPlayerCall();
    }
}

/// <summary>4. 回合结算</summary>
class RoundSettlementState : BattleState
{
    public override void OnEnter()
    {
        Battle.OnRoundSettlement();
    }
}

/// <summary>5. 回合结束</summary>
class RoundEndState : BattleState
{
    public override void OnEnter()
    {
        Battle.OnRoundEnd();
    }

    public override void Update(float delta)
    {
        Battle.OnRoundEndUpdate(delta);
    }
}

/// <summary>6. 战斗结束</summary>
class BattleEndState : BattleState
{
    public override void OnEnter()
    {
        Battle.OnBattleEnd();
    }
}
