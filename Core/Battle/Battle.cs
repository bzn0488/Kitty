using Godot;
using GuandanKitty.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GuandanKitty;

/// <summary>
/// 战斗逻辑控制器 —— 所有游戏逻辑在此，状态机只负责回调。
/// 持有 TurnFSM 子状态机，处理 Agent 轮次内部的 Judge→Act→Resolve→Advance 循环。
/// </summary>
public partial class Battle : Node
{
    // ═══════════════════════════════════════════
    //  公开属性
    // ═══════════════════════════════════════════

    /// <summary>所有参战方列表</summary>
    public List<Agent> Agents { get; } = new();

    /// <summary>牌河</summary>
    public CardRiver River { get; } = new();

    /// <summary>接龙追踪器</summary>
    public ChainTracker Chain { get; } = new();

    /// <summary>当前 Agent 在列表中的索引</summary>
    public int CurrentAgentIndex { get; set; }

    /// <summary>玩家 Agent</summary>
    public Agent? PlayerAgent => Agents.FirstOrDefault(a => a.IsPlayer);

    /// <summary>当前轮到的 Agent</summary>
    public Agent? CurrentAgent =>
        CurrentAgentIndex >= 0 && CurrentAgentIndex < Agents.Count
            ? Agents[CurrentAgentIndex] : null;

    /// <summary>敌方总 HP</summary>
    public int TotalEnemyHP => Agents
        .Where(a => a.IsEnemy)
        .Sum(a => a.Monsters.Sum(m => m.CurrentHP));

    /// <summary>玩家输入是否启用</summary>
    public bool IsPlayerInputEnabled { get; private set; }

    /// <summary>UI 引用，场景加载后自动从父节点获取</summary>
    public BattleUI? UI { get; set; }

    /// <summary>Turn 子状态机，处理一轮内的 Judge→Act→Resolve→Advance</summary>
    public TurnFSM Turn { get; private set; } = null!;

    // ═══════════════════════════════════════════
    //  私有字段
    // ═══════════════════════════════════════════

    private BattleFSM _fsm = null!;
    private int _lastPlayerPlayDamage;
    private float _roundEndDelay;

    // Turn 阶段数据传递
    private bool _turnResolvedIsPass;
    private bool _turnResolvedIsClearHand;
    private CardPattern? _turnResolvedPattern;

    // ═══════════════════════════════════════════
    //  生命周期
    // ═══════════════════════════════════════════

    public override void _Ready()
    {
        // 自动获取 UI 引用
        UI = GetParent<BattleUI>();

        Turn = new TurnFSM(this);

        _fsm = new BattleFSM();
        AddChild(_fsm);

        _fsm.AddState(new BattleStartState());
        _fsm.AddState(new RoundStartState());
        _fsm.AddState(new AgentTurnState());
        _fsm.AddState(new RoundSettlementState());
        _fsm.AddState(new RoundEndState());
        _fsm.AddState(new BattleEndState());

        // 从 Run 获取玩家数据，创建测试敌人，自动开始
        AutoStartBattle();
    }

    /// <summary>
    /// 自动开始战斗：用工厂方法创建玩家 Agent 和测试敌人 Agent，启动 FSM。
    /// </summary>
    private void AutoStartBattle()
    {
        var playerData = Run.Instance.PlayerData;
        if (playerData == null) return;

        Agents.Clear();
        Agents.Add(Agent.SpawnAgentFromPlayer(playerData));
        Agents.Add(Agent.SpawnAgentFromEnemy("训练假人",
            new List<Monster> { MonsterDatabase.CreateTestMonster() }));

        CurrentAgentIndex = 0;
        _lastPlayerPlayDamage = 0;

        ResetAgentRoundStates();
        ResetPlayerCallCounts();

        _fsm.TransitionTo<BattleStartState>();
    }

    // ═══════════════════════════════════════════
    //  玩家输入入口（UI 调用 → 当前状态 → Battle → TurnFSM）
    // ═══════════════════════════════════════════

    /// <summary>玩家出牌</summary>
    public string? PlayerPlay(List<Card> cards)
    {
        return _fsm.CurrentState?.OnPlayerPlay(cards);
    }

    /// <summary>玩家 Pass</summary>
    public string? PlayerPass()
    {
        return _fsm.CurrentState?.OnPlayerPass();
    }

    /// <summary>玩家叫牌</summary>
    public string? PlayerCallCards()
    {
        return _fsm.CurrentState?.OnPlayerCallCards();
    }

    // ═══════════════════════════════════════════
    //  TurnFSM 出口 —— 结束一轮，回到主 FSM
    // ═══════════════════════════════════════════

    /// <summary>Turn 子状态机请求结束本轮，主 FSM 转到回合结算</summary>
    public void ExitTurn()
    {
        _fsm.TransitionTo<RoundSettlementState>();
    }

    // ═══════════════════════════════════════════
    //  状态机回调 —— 由各个状态 OnEnter/Update 调用
    // ═══════════════════════════════════════════



    // ────────── BattleStart ──────────

    /// <summary>战斗开始：布置敌人初始手牌、玩家逐张摸起始牌</summary>
    public void OnBattleStart()
    {
        DrawEnemyInitialHands();
        StartPlayerDrawSequence();
    }

    // ────────── RoundStart ──────────

    /// <summary>回合开始：清空牌河和接龙记录，重置 Pass 标记</summary>
    public void OnRoundStart()
    {
        ResetRoundState();
        NotifyHandUpdated();
        NotifyRiverUpdated();
        NotifyStatus("回合开始");
        _fsm.TransitionTo<AgentTurnState>();
    }

    // ────────── AgentTurn（委托给 TurnFSM） ──────────

    /// <summary>启动 Turn 子状态机</summary>
    public void OnAgentTurnStart()
    {
        Turn.Start();
    }

    /// <summary>Turn 子状态机 Update</summary>
    public void OnAgentTurnUpdate(float delta)
    {
        Turn.Update(delta);
    }

    /// <summary>玩家出牌（委托给 TurnFSM）</summary>
    public string? OnPlayerPlayCards(List<Card> cards)
    {
        return Turn.HandlePlayerPlay(cards);
    }

    /// <summary>玩家 Pass（委托给 TurnFSM）</summary>
    public string? OnPlayerPassTurn()
    {
        return Turn.HandlePlayerPass();
    }

    /// <summary>玩家叫牌（委托给 TurnFSM）</summary>
    public string? OnPlayerCall()
    {
        return Turn.HandlePlayerCall();
    }

    // ═══════════════════════════════════════════
    //  Turn 阶段回调 —— 由 TurnFSM 状态调用
    // ═══════════════════════════════════════════

    // ────────── TurnJudge ──────────

    /// <summary>
    /// Turn Judge：跳过已 Pass 的 Agent；其余全 Pass 则结算；否则进入 Act。
    /// </summary>
    public void OnTurnJudge()
    {
        var agent = CurrentAgent;

        // 跳过已 Pass 的 Agent
        if (agent?.HasPassed == true)
        {
            _turnSkipCount++;
            if (_turnSkipCount > Agents.Count)
            {
                ExitTurn();
                return;
            }
            CurrentAgentIndex++;
            Turn.TransitionTo<TurnJudgeState>();
            return;
        }
        _turnSkipCount = 0;

        // 其余全部 Pass → 当前 Agent 自动获胜
        bool othersAllPassed = Agents
            .Where(a => a != agent)
            .All(a => a.HasPassed);

        if (othersAllPassed)
        {
            ExitTurn();
            return;
        }

        Turn.TransitionTo<TurnActState>();
    }

    private int _turnSkipCount;

    // ────────── TurnAct ──────────

    /// <summary>
    /// Turn Act：根据 Agent 类型开始行动。
    /// Player 启用输入；Enemy 启动 AI 倒计时。
    /// </summary>
    public void OnTurnAct()
    {
        var agent = CurrentAgent;
        if (agent == null)
        {
            Turn.TransitionTo<TurnAdvanceState>();
            return;
        }

        if (agent.IsPlayer)
        {
            EnablePlayerInput(true);
            NotifyStatus("请出牌或跳过");
        }
        else
        {
            EnablePlayerInput(false);
            _turnEnemyTimer = 0.6f;
            _turnEnemyActed = false;
            NotifyStatus($"{agent.Id} 思考中...");
        }
    }

    private float _turnEnemyTimer;
    private bool _turnEnemyActed;

    /// <summary>
    /// Turn Act Update：敌人 AI 倒计时，到时间后自动决策。
    /// </summary>
    public void OnTurnActUpdate(float delta)
    {
        var agent = CurrentAgent;
        if (agent == null || agent.IsPlayer || _turnEnemyActed) return;

        _turnEnemyTimer -= delta;
        if (_turnEnemyTimer > 0) return;
        _turnEnemyActed = true;

        // 执行敌人 AI
        if (Chain.LastPlayed == null)
        {
            // 敌人不能先手（玩家总是先手）
            agent.HasPassed = true;
            Chain.RecordPass(agent);
            NotifyAgentPassed(agent.Id);
            _turnResolvedIsPass = true;
            _turnResolvedPattern = null;
        }
        else
        {
            var result = EnemyAI.FindBestPlay(agent.Hands, Chain.LastPlayed);
            if (result != null)
            {
                var (handIdx, pattern) = result.Value;
                agent.Hands[handIdx].Remove(pattern.Cards);
                River.Add(pattern, agent);
                Chain.RecordPlay(pattern, agent);
                NotifyAgentPlayed(agent.Id, pattern.ToString(), pattern.CardCount);
                NotifyRiverUpdated();
                _turnResolvedIsPass = false;
                _turnResolvedPattern = pattern;
            }
            else
            {
                agent.HasPassed = true;
                Chain.RecordPass(agent);
                NotifyAgentPassed(agent.Id);
                _turnResolvedIsPass = true;
                _turnResolvedPattern = null;
            }
        }

        Turn.TransitionTo<TurnResolveState>();
    }

    /// <summary>
    /// Turn 玩家出牌：验证牌型合法性、扣除手牌、即时伤害、记录牌河。
    /// </summary>
    public string? OnTurnPlayerPlay(List<Card> cards)
    {
        var player = PlayerAgent;
        if (player == null || !player.IsActive) return "现在不是你的回合";

        var pattern = CardPatternDetector.Detect(cards);
        if (pattern == null) return "不是合法牌型";

        if (Chain.LastPlayed != null &&
            !SuppressionJudge.CanSuppress(pattern, Chain.LastPlayed))
        {
            return "无法压制上一手牌";
        }

        player.Hands[0].Remove(cards);

        bool isClearHand = player.Hands[0].IsEmpty;
        int damage = DamageCalculator.Calculate(
            pattern,
            Chain.DepthMultiplier,
            isWinningHand: false,
            isClearHand: isClearHand);

        if (damage > 0)
        {
            ApplyDamageToEnemies(damage);
        }

        River.Add(pattern, player);
        Chain.RecordPlay(pattern, player);
        PushPlayerPlayDamage(damage);

        NotifyAgentPlayed(player.Id, pattern.ToString(), pattern.CardCount);
        NotifyDamage(damage, TotalEnemyHP);
        NotifyHandUpdated();
        NotifyRiverUpdated();

        _turnResolvedIsPass = false;
        _turnResolvedIsClearHand = isClearHand;
        _turnResolvedPattern = pattern;

        Turn.TransitionTo<TurnResolveState>();
        return null;
    }

    /// <summary>Turn 玩家 Pass：标记已 Pass，拿赢回合 bonus，进结算</summary>
    public string? OnTurnPlayerPass()
    {
        var player = PlayerAgent;
        if (player == null || !player.IsActive) return "现在不是你的回合";

        player.HasPassed = true;
        Chain.RecordPass(player);
        NotifyAgentPassed(player.Id);

        if (Chain.LastPlayedBy?.IsPlayer == true)
        {
            ApplyWinningHandBonus();
        }

        ExitTurn();
        return null;
    }

    /// <summary>Turn 玩家叫牌：从牌堆抽 6 张</summary>
    public string? OnTurnPlayerCall()
    {
        var player = PlayerAgent;
        if (player == null || !player.IsActive) return "现在不是你的回合";
        if (!player.CanCallCards) return "叫牌次数已用完";

        var drawn = player.Deck!.Draw(player.CardsPerCall);
        player.Hands[0].AddRange(drawn);
        player.RemainingCallCards--;
        NotifyHandUpdated();
        return null;
    }

    // ────────── TurnResolve ──────────

    /// <summary>
    /// Turn Resolve：结算行动结果。
    /// 玩家 Pass/清空 → 结束回合；敌人 Pass → 继续；正常出牌 → 继续。
    /// </summary>
    public void OnTurnResolve()
    {
        var agent = CurrentAgent;
        if (agent == null)
        {
            ExitTurn();
            return;
        }

        if (_turnResolvedIsPass)
        {
            if (agent.IsPlayer)
            {
                // 玩家 Pass 已在 OnTurnPlayerPass 中处理
                ExitTurn();
            }
            else
            {
                // 敌人 Pass → 继续轮
                Turn.TransitionTo<TurnAdvanceState>();
            }
        }
        else
        {
            if (agent.IsPlayer && _turnResolvedIsClearHand)
            {
                // 玩家清空手牌：补抽 8 张，直接结算
                var player = PlayerAgent!;
                Chain.ClearHandPlayed = true;
                var drawn = player.Deck!.Draw(8);
                player.Hands[0].AddRange(drawn);
                NotifyHandUpdated();
                ExitTurn();
            }
            else
            {
                // 正常出牌 → 继续轮
                Turn.TransitionTo<TurnAdvanceState>();
            }
        }
    }

    // ────────── TurnAdvance ──────────

    /// <summary>Turn Advance：索引前进到下一 Agent，回到 Judge</summary>
    public void OnTurnAdvance()
    {
        CurrentAgentIndex++;
        Turn.TransitionTo<TurnJudgeState>();
    }

    // ────────── RoundSettlement ──────────

    /// <summary>回合结算：判定胜负，执行伤害或战败效果</summary>
    public void OnRoundSettlement()
    {
        var winner = DetermineRoundWinner();

        if (winner.IsPlayer)
        {
            HandlePlayerWin();
        }
        else
        {
            HandleEnemyWin();
        }
    }

    // ────────── RoundEnd ──────────

    /// <summary>回合结束：牌河洗回牌堆，敌人成长抽牌</summary>
    public void OnRoundEnd()
    {
        _roundEndDelay = 0.5f;
        ShuffleRiverBackToDeck();
        EnemyGrowthDraw();
        NotifyHandUpdated();
        NotifyRiverUpdated();
    }

    /// <summary>回合结束延时，到时间后开始新回合</summary>
    public void OnRoundEndUpdate(float delta)
    {
        _roundEndDelay -= delta;
        if (_roundEndDelay <= 0)
        {
            _fsm.TransitionTo<RoundStartState>();
        }
    }

    // ────────── BattleEnd ──────────

    /// <summary>战斗结束：通知 UI 胜负结果，禁用输入</summary>
    public void OnBattleEnd()
    {
        bool playerWon = TotalEnemyHP <= 0;
        NotifyBattleEnded(playerWon);
        EnablePlayerInput(false);
    }

    // ═══════════════════════════════════════════
    //  私有封装方法 —— Init
    // ═══════════════════════════════════════════

    /// <summary>重置所有 Agent 的回合 Pass 状态</summary>
    private void ResetAgentRoundStates()
    {
        foreach (var agent in Agents)
        {
            agent.HasPassed = false;
        }
    }

    /// <summary>重置玩家叫牌次数</summary>
    private void ResetPlayerCallCounts()
    {
        var player = PlayerAgent;
        if (player != null)
        {
            player.RemainingCallCards = player.MaxCallCards;
        }
    }

    // ═══════════════════════════════════════════
    //  私有封装方法 —— BattleStart
    // ═══════════════════════════════════════════

    /// <summary>敌人从各自怪物牌池抽取初始手牌</summary>
    private void DrawEnemyInitialHands()
    {
        var rng = new Random();
        foreach (var agent in Agents.Where(a => a.IsEnemy))
        {
            foreach (var monster in agent.Monsters)
            {
                for (int i = 0; i < 3; i++)
                {
                    agent.Hands[0].Add(monster.DrawFromPool(rng));
                }
            }
        }
    }

    /// <summary>开始玩家逐张摸牌序列</summary>
    private void StartPlayerDrawSequence()
    {
        _drawCardsTotal = 8;
        _drawCardsCurrent = 0;
        DrawNextStartCard();
    }

    // 初始抽牌相关字段
    private int _drawCardsTotal;
    private int _drawCardsCurrent;

    /// <summary>抽一张起始牌（逐张动画），抽完进入 RoundStart</summary>
    private void DrawNextStartCard()
    {
        if (_drawCardsCurrent >= _drawCardsTotal)
        {
            NotifyHandUpdated();
            _fsm.TransitionTo<RoundStartState>();
            return;
        }

        var player = PlayerAgent;
        if (!CanPlayerDraw())
        {
            NotifyHandUpdated();
            _fsm.TransitionTo<RoundStartState>();
            return;
        }

        var card = player!.Deck!.Draw();
        if (card == null)
        {
            NotifyHandUpdated();
            _fsm.TransitionTo<RoundStartState>();
            return;
        }

        player.Hands[0].Add(card);
        RequestCardDrawAnimation(card, OnStartCardDrawn);
    }

    /// <summary>判断玩家牌堆是否可抽牌</summary>
    private bool CanPlayerDraw()
    {
        var player = PlayerAgent;
        return player?.Deck != null && !player.Deck.IsEmpty;
    }

    /// <summary>一张起始牌抽完后回调</summary>
    private void OnStartCardDrawn()
    {
        _drawCardsCurrent++;
        DrawNextStartCard();
    }

    // ═══════════════════════════════════════════
    //  私有封装方法 —— RoundStart
    // ═══════════════════════════════════════════

    /// <summary>重置回合相关状态</summary>
    private void ResetRoundState()
    {
        River.Clear();
        Chain.Reset();
        foreach (var agent in Agents)
        {
            agent.HasPassed = false;
        }
        CurrentAgentIndex = 0;
    }

    // ═══════════════════════════════════════════
    //  私有封装方法 —— RoundSettlement
    // ═══════════════════════════════════════════

    /// <summary>判定回合胜者</summary>
    private Agent DetermineRoundWinner()
    {
        var lastPlayedBy = Chain.LastPlayedBy;
        var activeAgents = Agents.Where(a => a.IsActive).ToList();

        if (activeAgents.Count == 1)
        {
            return activeAgents[0];
        }
        if (activeAgents.Count == 0)
        {
            return lastPlayedBy ?? Agents[0];
        }
        return PlayerAgent!;
    }

    /// <summary>处理玩家获胜：通知结果，检查是否终结战斗</summary>
    private void HandlePlayerWin()
    {
        NotifyRoundResult(true, $"你赢得了本回合！剩余 HP: {TotalEnemyHP}");

        if (TotalEnemyHP <= 0)
        {
            _fsm.TransitionTo<BattleEndState>();
            return;
        }

        _fsm.TransitionTo<RoundEndState>();
    }

    /// <summary>处理敌人获胜：执行战败效果，检查是否游戏结束</summary>
    private void HandleEnemyWin()
    {
        ExecuteAllDefeatEffects();

        NotifyRoundResult(false,
            $"敌人赢得了本回合！牌堆剩余: {PlayerAgent?.Deck?.Count ?? 0}");

        if (PlayerAgent?.IsDefeated == true)
        {
            _fsm.TransitionTo<BattleEndState>();
            return;
        }

        _fsm.TransitionTo<RoundEndState>();
    }

    /// <summary>执行所有怪物的战败效果</summary>
    private void ExecuteAllDefeatEffects()
    {
        foreach (var enemy in Agents.Where(a => a.IsEnemy))
        {
            if (enemy.Monsters.Count > 0)
            {
                DefeatEffectExecutor.Execute(
                    enemy.Monsters,
                    PlayerAgent!.Hands[0],
                    PlayerAgent.Deck!);
            }
        }
    }

    // ═══════════════════════════════════════════
    //  私有封装方法 —— RoundEnd
    // ═══════════════════════════════════════════

    /// <summary>将牌河中所有牌洗回玩家牌堆</summary>
    private void ShuffleRiverBackToDeck()
    {
        var allCards = River.GetAllCards();
        PlayerAgent?.Deck?.ShuffleBack(allCards);
        River.Clear();
    }

    /// <summary>敌人从所有怪物牌池中抽牌（成长机制）</summary>
    private void EnemyGrowthDraw()
    {
        var rng = new Random();
        foreach (var enemy in Agents.Where(a => a.IsEnemy))
        {
            foreach (var monster in enemy.Monsters)
            {
                for (int i = 0; i < monster.DrawsPerRound; i++)
                {
                    enemy.Hands[0].Add(monster.DrawFromPool(rng));
                }
            }
        }
    }

    // ═══════════════════════════════════════════
    //  公共工具方法
    // ═══════════════════════════════════════════

    /// <summary>记录最后一手玩家出牌的伤害值，用于赢回合 ×2 补算</summary>
    public void PushPlayerPlayDamage(int damage)
    {
        _lastPlayerPlayDamage = damage;
    }

    /// <summary>对最后一手补乘 ×2 伤害（清空手牌不重复叠加）</summary>
    public void ApplyWinningHandBonus()
    {
        if (_lastPlayerPlayDamage > 0 && !Chain.ClearHandPlayed)
        {
            ApplyDamageToEnemies(_lastPlayerPlayDamage);
        }
    }

    /// <summary>对敌方全体造成伤害，从第一个怪物开始扣</summary>
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

    /// <summary>启用/禁用玩家输入</summary>
    public void EnablePlayerInput(bool enabled)
    {
        IsPlayerInputEnabled = enabled;
        UI?.OnPlayerInputChanged(enabled);
    }

    /// <summary>请求 UI 播放摸牌动画</summary>
    public void RequestCardDrawAnimation(Card card, Action onComplete)
    {
        UI?.OnCardDrawRequested(card, onComplete);
    }

    // ═══════════════════════════════════════════
    //  UI 通知方法
    // ═══════════════════════════════════════════

    /// <summary>更新状态消息</summary>
    public void NotifyStatus(string msg) => UI?.OnStatusMessage(msg);

    /// <summary>通知伤害数值</summary>
    public void NotifyDamage(int dmg, int remaining) => UI?.OnDamageDealt(dmg, remaining);

    /// <summary>通知 Agent 出牌</summary>
    public void NotifyAgentPlayed(string id, string desc, int cardCount)
        => UI?.OnAgentPlayed(id, desc, cardCount);

    /// <summary>通知 Agent Pass</summary>
    public void NotifyAgentPassed(string id) => UI?.OnAgentPassed(id);

    /// <summary>通知回合结果</summary>
    public void NotifyRoundResult(bool playerWon, string msg) => UI?.OnRoundResult(playerWon, msg);

    /// <summary>通知战斗结束</summary>
    public void NotifyBattleEnded(bool playerWon) => UI?.OnBattleEnded(playerWon);

    /// <summary>通知手牌刷新</summary>
    public void NotifyHandUpdated() => UI?.OnHandUpdated();

    /// <summary>通知牌河刷新</summary>
    public void NotifyRiverUpdated() => UI?.OnRiverUpdated();
}
