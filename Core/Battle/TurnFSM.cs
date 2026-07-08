using System;
using System.Collections.Generic;
using GuandanKitty.Core;

namespace GuandanKitty;

/// <summary>
/// Turn 子状态机 —— 管理一轮 Agent 的 Judge→Act→Resolve→Advance 循环。
/// 由 BattleFSM 持有和驱动，通过事件与外部通信。不含游戏逻辑。
/// </summary>
public class TurnFSM
{
    // ═══════════════════════════════════════════
    //  事件 —— BattleFSM 订阅，转发到 Battle
    // ═══════════════════════════════════════════

    /// <summary>TurnJudge 阶段回调</summary>
    public event Action? JudgeRequested;

    /// <summary>TurnAct 阶段回调</summary>
    public event Action? ActRequested;

    /// <summary>TurnAct Update 回调</summary>
    public event Action<float>? ActUpdateRequested;

    /// <summary>玩家出牌回调，返回错误信息或 null（成功）</summary>
    public event Func<List<Card>, string?>? PlayerPlayRequested;

    /// <summary>玩家 Pass 回调</summary>
    public event Func<string?>? PlayerPassRequested;

    /// <summary>玩家叫牌回调</summary>
    public event Func<string?>? PlayerCallRequested;

    /// <summary>TurnResolve 阶段回调</summary>
    public event Action? ResolveRequested;

    /// <summary>TurnAdvance 阶段回调</summary>
    public event Action? AdvanceRequested;

    /// <summary>本轮结束，通知 BattleFSM 转到 RoundSettlement</summary>
    public event Action? TurnComplete;

    // ═══════════════════════════════════════════
    //  Turn 阶段数据（Battle 写入，TurnResolve 读取）
    // ═══════════════════════════════════════════

    /// <summary>上一手行动是否为 Pass</summary>
    public bool LastActionWasPass { get; set; }

    /// <summary>上一手行动是否清空了手牌</summary>
    public bool LastActionIsClearHand { get; set; }

    /// <summary>上一手行动的牌型</summary>
    public CardPattern? LastActionPattern { get; set; }

    // ═══════════════════════════════════════════
    //  状态管理
    // ═══════════════════════════════════════════

    /// <summary>当前状态</summary>
    public TurnState? CurrentState { get; private set; }

    private readonly Dictionary<Type, TurnState> _states = new();

    public TurnFSM()
    {
        AddState(new TurnJudgeState());
        AddState(new TurnActState());
        AddState(new TurnResolveState());
        AddState(new TurnAdvanceState());
    }

    /// <summary>注册一个 Turn 状态</summary>
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

    // ═══════════════════════════════════════════
    //  内部方法 —— 供 TurnState 触发事件
    // ═══════════════════════════════════════════

    internal void RaiseJudge() => JudgeRequested?.Invoke();
    internal void RaiseAct() => ActRequested?.Invoke();
    internal void RaiseActUpdate(float delta) => ActUpdateRequested?.Invoke(delta);
    internal string? RaisePlayerPlay(List<Card> cards) => PlayerPlayRequested?.Invoke(cards);
    internal string? RaisePlayerPass() => PlayerPassRequested?.Invoke();
    internal string? RaisePlayerCall() => PlayerCallRequested?.Invoke();
    internal void RaiseResolve() => ResolveRequested?.Invoke();
    internal void RaiseAdvance() => AdvanceRequested?.Invoke();
    internal void RaiseTurnComplete() => TurnComplete?.Invoke();
}

// ═════════════════════════════════════════════════
//  Turn 状态基类
// ═════════════════════════════════════════════════

/// <summary>
/// Turn 子状态机的状态基类。
/// 禁止包含游戏逻辑，只做回调分发到 TurnFSM 事件。
/// </summary>
public abstract class TurnState
{
    /// <summary>持有 TurnFSM 引用</summary>
    protected TurnFSM FSM { get; private set; } = null!;

    /// <summary>初始化引用</summary>
    public void Initialize(TurnFSM fsm)
    {
        FSM = fsm;
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
//  Turn 状态实现 —— 只做事件触发，无游戏逻辑
// ═════════════════════════════════════════════════

/// <summary>
/// Judge —— 判定阶段。检查活跃 Agent 数量，决定进 Act 还是结束回合。
/// </summary>
class TurnJudgeState : TurnState
{
    public override void OnEnter()
    {
        FSM.RaiseJudge();
    }
}

/// <summary>
/// Act —— 行动阶段。Player 等输入；Enemy 倒计时后 AI。
/// </summary>
class TurnActState : TurnState
{
    public override void OnEnter()
    {
        FSM.RaiseAct();
    }

    public override void Update(float delta)
    {
        FSM.RaiseActUpdate(delta);
    }

    public override string? HandlePlayerPlay(List<Card> cards)
    {
        return FSM.RaisePlayerPlay(cards);
    }

    public override string? HandlePlayerPass()
    {
        return FSM.RaisePlayerPass();
    }

    public override string? HandlePlayerCall()
    {
        return FSM.RaisePlayerCall();
    }
}

/// <summary>
/// Resolve —— 结算阶段。处理行动结果，判断继续轮还是结束回合。
/// </summary>
class TurnResolveState : TurnState
{
    public override void OnEnter()
    {
        FSM.RaiseResolve();
    }
}

/// <summary>
/// Advance —— 推进阶段。索引前进到下一 Agent，回到 Judge。
/// </summary>
class TurnAdvanceState : TurnState
{
    public override void OnEnter()
    {
        FSM.RaiseAdvance();
    }
}
