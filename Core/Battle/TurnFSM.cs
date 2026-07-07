using System;
using System.Collections.Generic;
using GuandanKitty.Core;

namespace GuandanKitty;

/// <summary>
/// Turn 子状态机 —— 管理一"轮"的 Judge→Act→Resolve→Advance 生命周期。
/// 所有游戏逻辑在 Battle 中实现，状态只做回调分发。
/// </summary>
public class TurnFSM
{
    /// <summary>持有 Battle 引用</summary>
    public Battle Battle { get; }

    /// <summary>当前状态</summary>
    public TurnState? CurrentState { get; private set; }

    private readonly Dictionary<Type, TurnState> _states = new();

    public TurnFSM(Battle battle)
    {
        Battle = battle;

        AddState(new TurnJudgeState());
        AddState(new TurnActState());
        AddState(new TurnResolveState());
        AddState(new TurnAdvanceState());
    }

    /// <summary>注册一个 Turn 状态</summary>
    public void AddState(TurnState state)
    {
        state.Initialize(this, Battle);
        _states[state.GetType()] = state;
    }

    /// <summary>切换到指定类型的状态</summary>
    public void TransitionTo<T>() where T : TurnState
    {
        CurrentState?.OnExit();
        CurrentState = _states[typeof(T)];
        CurrentState.OnEnter();
    }

    /// <summary>启动一轮，从 Judge 开始</summary>
    public void Start()
    {
        TransitionTo<TurnJudgeState>();
    }

    /// <summary>每帧推进</summary>
    public void Update(float delta)
    {
        CurrentState?.Update(delta);
    }

    /// <summary>玩家出牌（路由到当前状态）</summary>
    public string? HandlePlayerPlay(List<Card> cards)
    {
        return CurrentState?.HandlePlayerPlay(cards) ?? "现在不是你的回合";
    }

    /// <summary>玩家 Pass（路由到当前状态）</summary>
    public string? HandlePlayerPass()
    {
        return CurrentState?.HandlePlayerPass() ?? "现在不是你的回合";
    }

    /// <summary>玩家叫牌（路由到当前状态）</summary>
    public string? HandlePlayerCall()
    {
        return CurrentState?.HandlePlayerCall() ?? "现在不是你的回合";
    }

    /// <summary>请求主 FSM 结束本轮，转到 RoundSettlement</summary>
    public void RequestExitTurn()
    {
        Battle.ExitTurn();
    }
}

// ═════════════════════════════════════════════════
//  Turn 状态基类
// ═════════════════════════════════════════════════

/// <summary>
/// Turn 子状态机的状态基类，与 BattleFSM.BattleState 对应。
/// 禁止包含游戏逻辑，只做回调分发到 Battle。
/// </summary>
public abstract class TurnState
{
    /// <summary>持有 TurnFSM 引用</summary>
    protected TurnFSM FSM { get; private set; } = null!;

    /// <summary>持有 Battle 引用</summary>
    protected Battle Battle { get; private set; } = null!;

    /// <summary>初始化引用</summary>
    public void Initialize(TurnFSM fsm, Battle battle)
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

    /// <summary>玩家出牌回调</summary>
    public virtual string? HandlePlayerPlay(List<Card> cards)
    {
        return "现在不是你的回合";
    }

    /// <summary>玩家 Pass 回调</summary>
    public virtual string? HandlePlayerPass()
    {
        return "现在不是你的回合";
    }

    /// <summary>玩家叫牌回调</summary>
    public virtual string? HandlePlayerCall()
    {
        return "现在不是你的回合";
    }
}

// ═════════════════════════════════════════════════
//  Turn 状态实现 —— 只做回调分发，无游戏逻辑
// ═════════════════════════════════════════════════

/// <summary>
/// Judge —— 判定阶段
/// 跳过已 Pass 的 Agent；若其余全 Pass 则直接结算；否则进入 Act。
/// </summary>
class TurnJudgeState : TurnState
{
    public override void OnEnter()
    {
        Battle.OnTurnJudge();
    }
}

/// <summary>
/// Act —— 行动阶段
/// Player：等输入；Enemy：倒计时后 AI。
/// </summary>
class TurnActState : TurnState
{
    public override void OnEnter()
    {
        Battle.OnTurnAct();
    }

    public override void Update(float delta)
    {
        Battle.OnTurnActUpdate(delta);
    }

    public override string? HandlePlayerPlay(List<Card> cards)
    {
        return Battle.OnTurnPlayerPlay(cards);
    }

    public override string? HandlePlayerPass()
    {
        return Battle.OnTurnPlayerPass();
    }

    public override string? HandlePlayerCall()
    {
        return Battle.OnTurnPlayerCall();
    }
}

/// <summary>
/// Resolve —— 结算阶段
/// 处理行动结果：判断跳结算还是继续轮。
/// </summary>
class TurnResolveState : TurnState
{
    public override void OnEnter()
    {
        Battle.OnTurnResolve();
    }
}

/// <summary>
/// Advance —— 推进阶段
/// 索引前进到下一 Agent，回到 Judge 开始新一轮。
/// </summary>
class TurnAdvanceState : TurnState
{
    public override void OnEnter()
    {
        Battle.OnTurnAdvance();
    }
}
