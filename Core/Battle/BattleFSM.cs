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

    public override void _Ready()
    {
        Battle = GetParent<Battle>();
    }

    public void AddState(BattleState state)
    {
        state.Initialize(this, Battle);
        _states[state.GetType()] = state;
    }

    public void TransitionTo<T>() where T : BattleState
    {
        var prev = CurrentState;
        CurrentState?.OnExit();
        CurrentState = _states[typeof(T)];
        CurrentState.OnEnter();
    }

    public override void _Process(double delta)
    {
        CurrentState?.Update((float)delta);
    }
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

    /// <summary>进入状态时调用</summary>
    public virtual void OnEnter() { }
    /// <summary>离开状态时调用</summary>
    public virtual void OnExit() { }
    /// <summary>每帧更新（用于计时器等）</summary>
    public virtual void Update(float delta) { }

    // 玩家输入回调（默认无操作，仅 PlayerTurnState 重写）
    public virtual string? OnPlayerPlay(List<Card> cards) => "现在不是你的回合";
    public virtual string? OnPlayerPass() => "现在不是你的回合";
    public virtual string? OnPlayerCallCards() => "现在不是你的回合";
}

// =================================================================
//  Init —— 战斗初始化
// =================================================================

class InitState : BattleState
{
    public override void OnEnter()
    {
        foreach (var agent in Battle.Agents)
        {
            agent.Deck?.Initialize();
            agent.HasPassed = false;

            // 敌人初始从怪物牌池抽牌
            if (agent.IsEnemy)
            {
                var rng = new Random();
                foreach (var monster in agent.Monsters)
                {
                    // 每个怪物贡献 3 张初始手牌
                    for (int i = 0; i < 3; i++)
                        agent.Hands[0].Add(monster.DrawFromPool(rng));
                }
            }
        }

        FSM.TransitionTo<DrawInitialState>();
    }
}

// =================================================================
//  DrawInitial —— 初始抽牌（逐张动画）
// =================================================================

class DrawInitialState : BattleState
{
    private int _cardsToDraw;
    private int _cardsDrawn;

    public override void OnEnter()
    {
        _cardsToDraw = 8;
        _cardsDrawn = 0;
        DrawNextCard();
    }

    private void DrawNextCard()
    {
        if (_cardsDrawn >= _cardsToDraw)
        {
            Battle.FireHandUpdated();
            FSM.TransitionTo<RoundStartState>();
            return;
        }

        var player = Battle.PlayerAgent;
        if (player?.Deck == null || player.Deck.IsEmpty)
        {
            Battle.FireHandUpdated();
            FSM.TransitionTo<RoundStartState>();
            return;
        }

        var card = player.Deck.Draw();
        if (card == null)
        {
            Battle.FireHandUpdated();
            FSM.TransitionTo<RoundStartState>();
            return;
        }

        player.Hands[0].Add(card);

        // 请求 UI 播摸牌动画，动画完成后回调继续抽下一张
        Battle.RequestCardDrawAnimation(card, () =>
        {
            _cardsDrawn++;
            DrawNextCard();
        });
    }
}

// =================================================================
//  RoundStart —— 回合开始
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
        Battle.FireHandUpdated();
        Battle.FireRiverUpdated();
        Battle.FireStatusMessage("回合开始，请出牌");

        // 总是玩家先手
        FSM.TransitionTo<PlayerTurnState>();
    }
}

// =================================================================
//  PlayerTurn —— 玩家出牌阶段
// =================================================================

class PlayerTurnState : BattleState
{
    public override void OnEnter()
    {
        Battle.EnablePlayerInput(true);
        Battle.FireStatusMessage("请出牌或跳过");
    }

    public override void OnExit()
    {
        Battle.EnablePlayerInput(false);
    }

    // ── 玩家出牌 ──
    public override string? OnPlayerPlay(List<Card> cards)
    {
        var player = Battle.PlayerAgent;
        if (player == null || !player.IsActive)
            return "现在不是你的回合";

        var pattern = CardPatternDetector.Detect(cards);
        if (pattern == null)
            return "不是合法牌型";

        // 压制判定
        if (Battle.Chain.LastPlayed != null)
        {
            if (!SuppressionJudge.CanSuppress(pattern, Battle.Chain.LastPlayed))
                return "无法压制上一手牌";
        }

        // 从手牌移除
        player.Hands[0].Remove(cards);

        // 即时伤害计算 & 应用
        bool isClearHand = player.Hands[0].IsEmpty;
        int depthMul = Battle.Chain.DepthMultiplier;

        int immediateDamage = DamageCalculator.Calculate(pattern, depthMul,
            isWinningHand: false, isClearHand: isClearHand);

        if (immediateDamage > 0)
            Battle.ApplyDamageToEnemies(immediateDamage);

        // 记录牌河 & 接龙
        Battle.River.Add(pattern, player);
        Battle.Chain.RecordPlay(pattern, player);

        // 保存最后一手伤害，用于赢回合 ×2 补算
        Battle.PushPlayerPlayDamage(immediateDamage);

        Battle.FireAgentPlayed(player.Id, pattern.ToString(), pattern.CardCount);
        Battle.FireDamageDealt(immediateDamage, Battle.TotalEnemyHP);
        Battle.FireHandUpdated();
        Battle.FireRiverUpdated();

        // 清空手牌 → 立即获胜
        if (isClearHand)
        {
            Battle.Chain.ClearHandPlayed = true;

            // 补抽至 8 张
            var drawn = player.Deck!.Draw(8);
            player.Hands[0].AddRange(drawn);
            Battle.FireHandUpdated();

            // 清空已自带 ×10，直接进结算
            FSM.TransitionTo<RoundSettlementState>();
            return null;
        }

        // 转到下一个 Agent
        AdvanceAfterPlay();
        return null;
    }

    // ── 玩家 Pass ──
    public override string? OnPlayerPass()
    {
        var player = Battle.PlayerAgent;
        if (player == null || !player.IsActive)
            return "现在不是你的回合";

        player.HasPassed = true;
        Battle.Chain.RecordPass(player);
        Battle.FireAgentPassed(player.Id);

        // 玩家 Pass 即终结本回合。谁有最后一手合法出牌谁赢。
        bool lastPlayIsPlayer = Battle.Chain.LastPlayedBy?.IsPlayer == true;

        if (lastPlayIsPlayer)
        {
            // 最后一手是玩家出的 → 玩家赢得本回合
            // 对最后一手补乘 ×2（清空已在出牌时 ×10，不重复）
            Battle.ApplyWinningHandBonus();
        }
        // else: 最后一手是敌人出的 → 敌人赢（RoundSettlement 中处理）

        FSM.TransitionTo<RoundSettlementState>();
        return null;
    }

    // ── 叫牌 ──
    public override string? OnPlayerCallCards()
    {
        var player = Battle.PlayerAgent;
        if (player == null || !player.IsActive)
            return "现在不是你的回合";
        if (!player.CanCallCards)
            return "叫牌次数已用完";

        var drawn = player.Deck!.Draw(player.CardsPerCall);
        player.Hands[0].AddRange(drawn);
        player.RemainingCallCards--;
        Battle.FireHandUpdated();
        return null;
    }

    // ── 出牌后前进到下一个 Agent ──
    private void AdvanceAfterPlay()
    {
        Battle.CurrentAgentIndex++;

        // 检查是否所有敌人都已 Pass
        var activeNonPlayer = Battle.Agents
            .Where(a => a.IsActive && !a.IsPlayer).ToList();

        if (activeNonPlayer.Count == 0)
        {
            // 所有敌人已 Pass → 玩家可自压或 Pass
            Battle.FireStatusMessage("所有敌人都已 Pass，你可以自压或跳过");
            // 留在 PlayerTurnState，玩家可继续出牌（自压）或 Pass
            return;
        }

        FSM.TransitionTo<EnemyTurnState>();
    }
}

// =================================================================
//  EnemyTurn —— 敌人出牌阶段
// =================================================================

class EnemyTurnState : BattleState
{
    private float _timer;
    private bool _hasActed;

    public override void OnEnter()
    {
        _timer = 0.6f; // 思考延迟
        _hasActed = false;
        var enemy = Battle.CurrentAgent;
        Battle.FireStatusMessage($"{enemy?.Id ?? "敌人"} 思考中...");
        Battle.EnablePlayerInput(false);
    }

    public override void Update(float delta)
    {
        if (_hasActed) return;
        _timer -= delta;
        if (_timer > 0) return;

        _hasActed = true;
        ProcessEnemy();
    }

    private void ProcessEnemy()
    {
        var enemy = Battle.CurrentAgent;
        if (enemy == null) { FallbackToPlayer(); return; }

        if (Battle.Chain.LastPlayed == null)
        {
            // 敌人不能先手（玩家总是先手），自动 Pass
            enemy.HasPassed = true;
            Battle.Chain.RecordPass(enemy);
            Battle.FireAgentPassed(enemy.Id);
            AfterEnemyAction();
            return;
        }

        var result = EnemyAI.FindBestPlay(enemy.Hands, Battle.Chain.LastPlayed);
        if (result != null)
        {
            var (handIdx, pattern) = result.Value;
            enemy.Hands[handIdx].Remove(pattern.Cards);
            Battle.River.Add(pattern, enemy);
            Battle.Chain.RecordPlay(pattern, enemy);
            Battle.FireAgentPlayed(enemy.Id, pattern.ToString(), pattern.CardCount);
            Battle.FireRiverUpdated();
            Battle.FireHandUpdated();

            // 敌人出牌后转回玩家
            Battle.CurrentAgentIndex++;
            FSM.TransitionTo<PlayerTurnState>();
        }
        else
        {
            // 敌人无法压制 → Pass
            enemy.HasPassed = true;
            Battle.Chain.RecordPass(enemy);
            Battle.FireAgentPassed(enemy.Id);
            AfterEnemyAction();
        }
    }

    private void AfterEnemyAction()
    {
        // 敌人 Pass 后检查是否所有敌人都已 Pass
        var activeNonPlayer = Battle.Agents
            .Where(a => a.IsActive && !a.IsPlayer).ToList();

        if (activeNonPlayer.Count == 0)
        {
            // 所有敌人都已 Pass，转回玩家让其自压或 Pass
            Battle.FireStatusMessage("敌人都已 Pass，轮到你了");
            FSM.TransitionTo<PlayerTurnState>();
        }
        else
        {
            // 还有活跃敌人，继续下一个
            Battle.CurrentAgentIndex++;
            FSM.TransitionTo<EnemyTurnState>();
        }
    }

    private void FallbackToPlayer()
    {
        FSM.TransitionTo<PlayerTurnState>();
    }
}

// =================================================================
//  RoundSettlement —— 回合结算
// =================================================================

class RoundSettlementState : BattleState
{
    private bool _isSettled;

    public override void OnEnter()
    {
        if (_isSettled) return;
        _isSettled = true;

        // 判断胜负
        // 赢家 = LastPlayedBy 的一方（如果双方都 Pass 了）
        // 或者唯一没 Pass 的一方
        var lastPlayedBy = Battle.Chain.LastPlayedBy;
        var activeAgents = Battle.Agents.Where(a => a.IsActive).ToList();

        Agent winner;
        if (activeAgents.Count == 1)
        {
            // 只剩一个活跃 Agent
            winner = activeAgents[0];
        }
        else if (activeAgents.Count == 0)
        {
            // 全部 Pass → 最后出牌的一方赢
            winner = lastPlayedBy ?? Battle.Agents[0];
        }
        else
        {
            // 还有多个活跃（理论上不会走到这里）
            winner = Battle.PlayerAgent!;
        }

        bool playerWon = winner.IsPlayer;

        if (playerWon)
        {
            Battle.FireRoundResult(true, $"你赢得了本回合！剩余敌人 HP: {Battle.TotalEnemyHP}");

            if (Battle.TotalEnemyHP <= 0)
            {
                FSM.TransitionTo<BattleEndState>();
                return;
            }
        }
        else
        {
            // 敌人赢得回合 → 执行所有怪物的战败效果
            foreach (var enemy in Battle.Agents.Where(a => a.IsEnemy))
            {
                if (enemy.Monsters.Count > 0)
                {
                    DefeatEffectExecutor.Execute(enemy.Monsters,
                        Battle.PlayerAgent!.Hands[0], Battle.PlayerAgent.Deck!);
                }
            }

            Battle.FireRoundResult(false,
                $"敌人赢得了本回合！牌堆剩余: {Battle.PlayerAgent?.Deck?.Count ?? 0}");

            if (Battle.PlayerAgent?.IsDefeated == true)
            {
                FSM.TransitionTo<BattleEndState>();
                return;
            }
        }

        FSM.TransitionTo<RoundEndState>();
    }

    public override void OnExit()
    {
        _isSettled = false;
    }
}

// =================================================================
//  RoundEnd —— 回合结束
// =================================================================

class RoundEndState : BattleState
{
    private float _delay = 0.5f;

    public override void OnEnter()
    {
        _delay = 0.5f;

        // 牌河洗回抽牌堆
        var allRiverCards = Battle.River.GetAllCards();
        Battle.PlayerAgent?.Deck?.ShuffleBack(allRiverCards);
        Battle.River.Clear();

        // 敌方成长：每回合从怪物牌池中抽牌
        var rng = new Random();
        foreach (var enemy in Battle.Agents.Where(a => a.IsEnemy))
        {
            foreach (var monster in enemy.Monsters)
            {
                for (int i = 0; i < monster.DrawsPerRound; i++)
                {
                    var card = monster.DrawFromPool(rng);
                    enemy.Hands[0].Add(card);
                }
            }
        }

        Battle.FireHandUpdated();
        Battle.FireRiverUpdated();
    }

    public override void Update(float delta)
    {
        _delay -= delta;
        if (_delay <= 0)
        {
            FSM.TransitionTo<RoundStartState>();
        }
    }
}

// =================================================================
//  BattleEnd —— 战斗结束
// =================================================================

class BattleEndState : BattleState
{
    private bool _playerWon;

    public override void OnEnter()
    {
        _playerWon = Battle.TotalEnemyHP <= 0;
        Battle.FireBattleEnded(_playerWon);
        Battle.EnablePlayerInput(false);
    }
}
