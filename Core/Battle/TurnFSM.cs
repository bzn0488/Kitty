using System;
using System.Collections.Generic;

namespace GuandanKitty;

/// <summary>
/// Turn 子状态机 —— Judge→Start→AfterPlay→End 循环。
/// 只有状态切换，不含游戏逻辑。逻辑在 Battle 和 Agent 中。
/// </summary>
public class TurnFSM
{
    /// <summary>本轮结束通知 BattleFSM</summary>
    public event Action? TurnComplete;

    /// <summary>当前状态</summary>
    public TurnState? CurrentState { get; private set; }

    private readonly Dictionary<Type, TurnState> _states = new();

    public TurnFSM()
    {
        AddState(new TurnJudgeState());
        AddState(new TurnStartState());
        AddState(new TurnAfterPlayState());
        AddState(new TurnEndState());
    }

    private void AddState(TurnState state)
    {
        state.Initialize(this);
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

    /// <summary>触发 TurnComplete 事件</summary>
    internal void CompleteTurn()
    {
        TurnComplete?.Invoke();
    }
}

// ═════════════════════════════════════════════════
//  Turn 状态基类
// ═════════════════════════════════════════════════

public abstract class TurnState
{
    /// <summary>持有 TurnFSM 引用</summary>
    protected TurnFSM FSM { get; private set; } = null!;

    public void Initialize(TurnFSM fsm)
    {
        FSM = fsm;
    }

    public virtual void OnEnter() { }
    public virtual void OnExit() { }
    public virtual void Update(float delta) { }
}

// ═════════════════════════════════════════════════
//  Turn 状态实现
// ═════════════════════════════════════════════════

/// <summary>
/// Judge —— 判定阶段。调 Battle.JudgeTurn()，决定继续还是结束回合。
/// </summary>
class TurnJudgeState : TurnState
{
    public override void OnEnter()
    {
        if (Battle.Current.JudgeTurn())
            FSM.TransitionTo<TurnStartState>();
        else
            FSM.CompleteTurn();
    }
}

/// <summary>
/// Start —— 行动阶段。Battle 设置当前 Agent，玩家等输入或敌人 AI 自动决策。
/// </summary>
class TurnStartState : TurnState
{
    public override void OnEnter()
    {
        Battle.Current.OnTurnStart();
    }

    public override void Update(float delta)
    {
        Battle.Current.OnTurnStartUpdate(delta);
    }
}

/// <summary>
/// AfterPlay —— 出牌后结算。计算伤害、触发效果，然后转到 End。
/// </summary>
class TurnAfterPlayState : TurnState
{
    public override void OnEnter()
    {
        Battle.Current.OnTurnAfterPlay();
        FSM.TransitionTo<TurnEndState>();
    }
}

/// <summary>
/// End —— 轮结束。触发结束效果、推进下一 Agent、回到 Judge。
/// </summary>
class TurnEndState : TurnState
{
    public override void OnEnter()
    {
        Battle.Current.OnTurnEnd();
        Battle.Current.AdvanceToNext();
        FSM.TransitionTo<TurnJudgeState>();
    }
}
