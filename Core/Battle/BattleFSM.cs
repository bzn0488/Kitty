using Godot;
using GuandanKitty.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GuandanKitty;

/// <summary>
/// 战斗状态机 —— 管理状态注册、流转、生命周期
/// </summary>
public partial class BattleFSM : Node
{
    public Battle Battle { get; private set; } = null!;
    public BattleState? CurrentState { get; private set; }
    private readonly Dictionary<Type, BattleState> _states = new();

    public override void _Ready() => Battle = GetParent<Battle>();

    public void AddState(BattleState state)
    {
        state.Initialize(this, Battle);
        _states[state.GetType()] = state;
    }

    public void TransitionTo<T>() where T : BattleState
    {
        CurrentState?.OnExit();
        CurrentState = _states[typeof(T)];
        CurrentState.OnEnter();
    }

    public override void _Process(double delta) => CurrentState?.Update((float)delta);
}

// =================================================================
//  状态基类
// =================================================================

public abstract class BattleState
{
    protected BattleFSM FSM { get; private set; } = null!;
    protected Battle Battle { get; private set; } = null!;

    public void Initialize(BattleFSM fsm, Battle battle)
    {
        FSM = fsm;
        Battle = battle;
    }

    public virtual void OnEnter() { }
    public virtual void OnExit() { }
    public virtual void Update(float delta) { }

    // 玩家输入回调（仅当当前 Agent 是 Player 时由 UI 调用）
    public virtual string? OnPlayerPlay(List<Card> cards) => "现在不是你的回合";
    public virtual string? OnPlayerPass() => "现在不是你的回合";
    public virtual string? OnPlayerCallCards() => "现在不是你的回合";
}

// =================================================================
//  1. Init —— 逻辑初始化
// =================================================================
// 游戏性上战斗还没开始。做一些逻辑的初始化工作。

class InitState : BattleState
{
    public override void OnEnter()
    {
        foreach (var agent in Battle.Agents)
        {
            agent.Deck?.Initialize();
            agent.HasPassed = false;

            // 重置叫牌次数
            if (agent.IsPlayer)
            {
                agent.RemainingCallCards = agent.MaxCallCards;
            }
        }

        FSM.TransitionTo<BattleStartState>();
    }
}

// =================================================================
//  2. BattleStart —— 战斗开始
// =================================================================
// 布置怪物、布置玩家、抽取玩家起始手牌（逐张动画）。

class BattleStartState : BattleState
{
    private int _cardsToDraw;
    private int _cardsDrawn;

    public override void OnEnter()
    {
        // 敌人从怪物牌池抽初始手牌
        var rng = new Random();
        foreach (var agent in Battle.Agents.Where(a => a.IsEnemy))
        {
            foreach (var monster in agent.Monsters)
            {
                for (int i = 0; i < 3; i++)
                    agent.Hands[0].Add(monster.DrawFromPool(rng));
            }
        }

        // 玩家初始抽 8 张（逐张动画）
        _cardsToDraw = 8;
        _cardsDrawn = 0;
        DrawNextCard();
    }

    private void DrawNextCard()
    {
        if (_cardsDrawn >= _cardsToDraw)
        {
            Battle.NotifyHandUpdated();
            FSM.TransitionTo<RoundStartState>();
            return;
        }

        var player = Battle.PlayerAgent;
        if (player?.Deck == null || player.Deck.IsEmpty)
        {
            Battle.NotifyHandUpdated();
            FSM.TransitionTo<RoundStartState>();
            return;
        }

        var card = player.Deck.Draw();
        if (card == null)
        {
            Battle.NotifyHandUpdated();
            FSM.TransitionTo<RoundStartState>();
            return;
        }

        player.Hands[0].Add(card);
        Battle.RequestCardDrawAnimation(card, () =>
        {
            _cardsDrawn++;
            DrawNextCard();
        });
    }
}

// =================================================================
//  3. RoundStart —— 回合开始
// =================================================================

class RoundStartState : BattleState
{
    public override void OnEnter()
    {
        Battle.River.Clear();
        Battle.Chain.Reset();
        foreach (var agent in Battle.Agents)
            agent.HasPassed = false;

        Battle.CurrentAgentIndex = 0;
        Battle.NotifyHandUpdated();
        Battle.NotifyRiverUpdated();
        Battle.NotifyStatus("回合开始");

        FSM.TransitionTo<AgentTurnState>();
    }
}

// =================================================================
//  4. AgentTurn —— Agent 轮次（通用：玩家/敌人/友方）
// =================================================================
//
//  判定阶段：跳过已 Pass 的 Agent；若其他全部 Pass 则直接结算。
//  轮开始  ：触发轮开始事件，若为玩家则启用输入。
//  出牌后  ：即时伤害、牌河记录、接龙追踪。
//  轮结束  ：触发轮结束事件，转到下一 Agent。

class AgentTurnState : BattleState
{
    // 防止全 Pass 时无限循环
    private int _skipCount;

    // 敌人 AI 延时
    private float _enemyTimer;
    private bool _enemyActed;

    // ═══════════════════════════════════════════
    //  判定阶段
    // ═══════════════════════════════════════════

    public override void OnEnter()
    {
        var agent = Battle.CurrentAgent;

        // 跳过已 Pass 的 Agent
        if (agent?.HasPassed == true)
        {
            _skipCount++;
            if (_skipCount > Battle.Agents.Count)
            {
                // 全 Pass → 按最后出牌方决定胜负
                FSM.TransitionTo<RoundSettlementState>();
                return;
            }
            Battle.CurrentAgentIndex++;
            FSM.TransitionTo<AgentTurnState>();
            return;
        }
        _skipCount = 0;

        // 检查其他 Agent 是否全部 Pass
        bool othersAllPassed = Battle.Agents
            .Where(a => a != agent)
            .All(a => a.HasPassed);

        if (othersAllPassed)
        {
            // 当前 Agent 是唯一活跃 → 赢
            FSM.TransitionTo<RoundSettlementState>();
            return;
        }

        // ═══════════════════════════════════════
        //  轮开始
        // ═══════════════════════════════════════

        if (agent!.IsPlayer)
        {
            Battle.EnablePlayerInput(true);
            Battle.NotifyStatus("请出牌或跳过");
        }
        else
        {
            Battle.NotifyStatus($"{agent.Id} 思考中...");
            Battle.EnablePlayerInput(false);
            _enemyTimer = 0.6f;
            _enemyActed = false;
        }
    }

    public override void OnExit()
    {
        Battle.EnablePlayerInput(false);
    }

    // ═══════════════════════════════════════════
    //  敌人 AI（用 Update 做延时）
    // ═══════════════════════════════════════════

    public override void Update(float delta)
    {
        var agent = Battle.CurrentAgent;
        if (agent == null || agent.IsPlayer || _enemyActed) return;

        _enemyTimer -= delta;
        if (_enemyTimer > 0) return;
        _enemyActed = true;

        ProcessEnemy(agent);
    }

    private void ProcessEnemy(Agent enemy)
    {
        if (Battle.Chain.LastPlayed == null)
        {
            // 敌人不能先手（玩家总是先手）
            enemy.HasPassed = true;
            Battle.Chain.RecordPass(enemy);
            Battle.NotifyAgentPassed(enemy.Id);
            EndTurn();
            return;
        }

        var result = EnemyAI.FindBestPlay(enemy.Hands, Battle.Chain.LastPlayed);
        if (result != null)
        {
            var (handIdx, pattern) = result.Value;
            enemy.Hands[handIdx].Remove(pattern.Cards);
            Battle.River.Add(pattern, enemy);
            Battle.Chain.RecordPlay(pattern, enemy);

            // 出牌后即时结算
            Battle.NotifyAgentPlayed(enemy.Id, pattern.ToString(), pattern.CardCount);
            Battle.NotifyRiverUpdated();
        }
        else
        {
            enemy.HasPassed = true;
            Battle.Chain.RecordPass(enemy);
            Battle.NotifyAgentPassed(enemy.Id);
        }

        EndTurn();
    }

    // ═══════════════════════════════════════════
    //  玩家操作回调
    // ═══════════════════════════════════════════

    public override string? OnPlayerPlay(List<Card> cards)
    {
        var player = Battle.PlayerAgent;
        if (player == null || !player.IsActive)
            return "现在不是你的回合";

        var pattern = CardPatternDetector.Detect(cards);
        if (pattern == null) return "不是合法牌型";

        if (Battle.Chain.LastPlayed != null &&
            !SuppressionJudge.CanSuppress(pattern, Battle.Chain.LastPlayed))
            return "无法压制上一手牌";

        // 移除手牌
        player.Hands[0].Remove(cards);

        // 即时伤害
        bool isClearHand = player.Hands[0].IsEmpty;
        int damage = DamageCalculator.Calculate(pattern, Battle.Chain.DepthMultiplier,
            isWinningHand: false, isClearHand: isClearHand);

        if (damage > 0) Battle.ApplyDamageToEnemies(damage);

        // 记录牌河 & 接龙
        Battle.River.Add(pattern, player);
        Battle.Chain.RecordPlay(pattern, player);
        Battle.PushPlayerPlayDamage(damage);

        // 出牌后即时结算
        Battle.NotifyAgentPlayed(player.Id, pattern.ToString(), pattern.CardCount);
        Battle.NotifyDamage(damage, Battle.TotalEnemyHP);
        Battle.NotifyHandUpdated();
        Battle.NotifyRiverUpdated();

        // 清空手牌：自动赢，补抽 8 张
        if (isClearHand)
        {
            Battle.Chain.ClearHandPlayed = true;
            var drawn = player.Deck!.Draw(8);
            player.Hands[0].AddRange(drawn);
            Battle.NotifyHandUpdated();
            FSM.TransitionTo<RoundSettlementState>();
            return null;
        }

        // 轮结束 → 下一 Agent
        EndTurn();
        return null;
    }

    public override string? OnPlayerPass()
    {
        var player = Battle.PlayerAgent;
        if (player == null || !player.IsActive)
            return "现在不是你的回合";

        player.HasPassed = true;
        Battle.Chain.RecordPass(player);
        Battle.NotifyAgentPassed(player.Id);

        // 玩家 Pass → 回合结束。最后出牌方赢。
        if (Battle.Chain.LastPlayedBy?.IsPlayer == true)
            Battle.ApplyWinningHandBonus();

        FSM.TransitionTo<RoundSettlementState>();
        return null;
    }

    public override string? OnPlayerCallCards()
    {
        var player = Battle.PlayerAgent;
        if (player == null || !player.IsActive) return "现在不是你的回合";
        if (!player.CanCallCards) return "叫牌次数已用完";

        var drawn = player.Deck!.Draw(player.CardsPerCall);
        player.Hands[0].AddRange(drawn);
        player.RemainingCallCards--;
        Battle.NotifyHandUpdated();
        return null;
    }

    // ═══════════════════════════════════════════
    //  轮结束
    // ═══════════════════════════════════════════

    private void EndTurn()
    {
        Battle.CurrentAgentIndex++;
        FSM.TransitionTo<AgentTurnState>();
    }
}

// =================================================================
//  5. RoundSettlement —— 回合结算
// =================================================================

class RoundSettlementState : BattleState
{
    private bool _settled;

    public override void OnEnter()
    {
        if (_settled) return;
        _settled = true;

        var lastPlayedBy = Battle.Chain.LastPlayedBy;
        var active = Battle.Agents.Where(a => a.IsActive).ToList();

        Agent winner;
        if (active.Count == 1)
            winner = active[0];
        else if (active.Count == 0)
            winner = lastPlayedBy ?? Battle.Agents[0];
        else
            winner = Battle.PlayerAgent!; // 清空手牌场景

        if (winner.IsPlayer)
        {
            Battle.NotifyRoundResult(true, $"你赢得了本回合！剩余 HP: {Battle.TotalEnemyHP}");
            if (Battle.TotalEnemyHP <= 0)
            {
                FSM.TransitionTo<BattleEndState>();
                return;
            }
        }
        else
        {
            foreach (var enemy in Battle.Agents.Where(a => a.IsEnemy))
            {
                if (enemy.Monsters.Count > 0)
                    DefeatEffectExecutor.Execute(enemy.Monsters,
                        Battle.PlayerAgent!.Hands[0], Battle.PlayerAgent.Deck!);
            }
            Battle.NotifyRoundResult(false,
                $"敌人赢了！牌堆剩余: {Battle.PlayerAgent?.Deck?.Count ?? 0}");
            if (Battle.PlayerAgent?.IsDefeated == true)
            {
                FSM.TransitionTo<BattleEndState>();
                return;
            }
        }

        FSM.TransitionTo<RoundEndState>();
    }

    public override void OnExit() => _settled = false;
}

// =================================================================
//  6. RoundEnd —— 回合结束
// =================================================================

class RoundEndState : BattleState
{
    private float _delay = 0.5f;

    public override void OnEnter()
    {
        _delay = 0.5f;

        // 牌河洗回
        var all = Battle.River.GetAllCards();
        Battle.PlayerAgent?.Deck?.ShuffleBack(all);
        Battle.River.Clear();

        // 敌人成长
        var rng = new Random();
        foreach (var enemy in Battle.Agents.Where(a => a.IsEnemy))
        {
            foreach (var monster in enemy.Monsters)
            {
                for (int i = 0; i < monster.DrawsPerRound; i++)
                    enemy.Hands[0].Add(monster.DrawFromPool(rng));
            }
        }

        Battle.NotifyHandUpdated();
        Battle.NotifyRiverUpdated();
    }

    public override void Update(float delta)
    {
        _delay -= delta;
        if (_delay <= 0) FSM.TransitionTo<RoundStartState>();
    }
}

// =================================================================
//  7. BattleEnd —— 战斗结束（后续接外线系统）
// =================================================================

class BattleEndState : BattleState
{
    public override void OnEnter()
    {
        Battle.NotifyBattleEnded(Battle.TotalEnemyHP <= 0);
        Battle.EnablePlayerInput(false);
    }
}
