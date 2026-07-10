using System;
using System.Collections.Generic;
using GuandanKitty.Core;

namespace GuandanKitty;

/// <summary>
/// 战斗状态机 —— 纯 C#，只负责状态流转和回调分发。
/// 持有 TurnFSM 子状态机，不含游戏逻辑。
/// </summary>
public class BattleFSM
{
    /// <summary>当前状态</summary>
    public BattleState? CurrentState { get; private set; }

    /// <summary>Turn 子状态机（仅在 AgentTurnState 期间活跃）</summary>
    internal TurnFSM TurnFSM { get; } = new();

    private readonly Dictionary<Type, BattleState> _states = new();
    private readonly Battle _battle;

    public BattleFSM(Battle battle)
    {
        _battle = battle;

        AddState(new BattleStartState());
        AddState(new RoundStartState());
        AddState(new AgentTurnState());
        AddState(new RoundSettlementState());
        AddState(new RoundEndState());
        AddState(new BattleEndState());

        // 唯一事件：TurnFSM 完成一整轮
        TurnFSM.TurnComplete += () => TransitionTo<RoundSettlementState>();
    }

    /// <summary>注册一个 BattleState</summary>
    public void AddState(BattleState state)
    {
        state.Initialize(this, _battle);
        _states[state.GetType()] = state;
    }

    /// <summary>切换到指定类型的状态</summary>
    public void TransitionTo<T>() where T : BattleState
    {
        CurrentState?.OnExit();
        CurrentState = _states[typeof(T)];
        CurrentState.OnEnter();
    }

    /// <summary>每帧更新，由 Battle.Update() 驱动</summary>
    public void Update(float delta)
    {
        CurrentState?.Update(delta);
    }
}

// ═════════════════════════════════════════════════
//  状态基类
// ═════════════════════════════════════════════════

/// <summary>
/// 战斗状态基类。所有状态继承此类，重写对应回调方法。
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

    /// <summary>玩家出牌回调（仅 AgentTurnState 有效）</summary>
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

/// <summary>战斗开始（布置 + 抽起始手牌）</summary>
class BattleStartState : BattleState
{
    public override void OnEnter()
    {
        Battle.OnBattleStart();
    }
}

/// <summary>回合开始</summary>
class RoundStartState : BattleState
{
    public override void OnEnter()
    {
        Battle.OnRoundStart();
    }
}

/// <summary>
/// Agent 轮次 —— 启动 TurnFSM 子状态机。
/// </summary>
class AgentTurnState : BattleState
{
    public override void OnEnter()
    {
        FSM.TurnFSM.Start();
    }

    public override void Update(float delta)
    {
        FSM.TurnFSM.Update(delta);
    }
}

/// <summary>回合结算</summary>
class RoundSettlementState : BattleState
{
    public override void OnEnter()
    {
        Battle.OnRoundSettlement();
    }
}

/// <summary>回合结束</summary>
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

/// <summary>战斗结束</summary>
class BattleEndState : BattleState
{
    public override void OnEnter()
    {
        Battle.OnBattleEnd();
    }
}
