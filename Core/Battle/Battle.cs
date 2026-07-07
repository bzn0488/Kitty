using Godot;
using GuandanKitty.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GuandanKitty;

/// <summary>
/// 战斗逻辑控制器 —— 所有游戏逻辑在此，状态机只负责回调。
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
    /// <summary>UI 引用，由 BattleUI 创建后注入</summary>
    public BattleUI? UI { get; set; }

    // ═══════════════════════════════════════════
    //  私有字段
    // ═══════════════════════════════════════════

    private BattleFSM _fsm = null!;
    private int _lastPlayerPlayDamage;

    // 敌人 AI 计时
    private float _enemyTimer;
    private bool _enemyActed;

    // 初始抽牌
    private int _drawCardsTotal;
    private int _drawCardsCurrent;

    // 安全计数器
    private int _skipCount;

    // 回合结束延时
    private float _roundEndDelay;

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

    /// <summary>
    /// 启动战斗。由外部（UI 或 RunManager）调用。
    /// </summary>
    public void StartBattle(List<Agent> agents)
    {
        Agents.Clear();
        Agents.AddRange(agents);
        CurrentAgentIndex = 0;
        _lastPlayerPlayDamage = 0;
    }

    // ═══════════════════════════════════════════
    //  玩家输入入口（UI 调用 → 当前状态 → Battle）
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

    // ═══════════════════════════════════════════════
    //  状态机回调 —— 由各个状态 OnEnter/Update 调用
    // ═══════════════════════════════════════════════

    // ────────── Init ──────────

    /// <summary>
    /// 初始化战斗：准备牌堆、重置 Agent 状态、重置叫牌次数。
    /// </summary>
    public void OnInit()
    {
        InitializeAgentDecks();
        ResetAgentRoundStates();
        ResetPlayerCallCounts();
        _fsm.TransitionTo<BattleStartState>();
    }

    // ────────── BattleStart ──────────

    /// <summary>
    /// 战斗开始：布置敌人初始手牌、玩家逐张摸起始牌。
    /// </summary>
    public void OnBattleStart()
    {
        DrawEnemyInitialHands();
        StartPlayerDrawSequence();
    }

    // ────────── RoundStart ──────────

    /// <summary>
    /// 回合开始：清空牌河和接龙记录，重置 Pass 标记，开始首轮 Agent。
    /// </summary>
    public void OnRoundStart()
    {
        ResetRoundState();
        NotifyHandUpdated();
        NotifyRiverUpdated();
        NotifyStatus("回合开始");
        _fsm.TransitionTo<AgentTurnState>();
    }

    // ────────── AgentTurn ──────────

    /// <summary>
    /// Agent 轮次开始：跳过已 Pass 的 Agent，其余全 Pass 则直接结算。
    /// 否则根据 Agent 类型（玩家/敌人）执行不同流程。
    /// </summary>
    public void OnAgentTurnStart()
    {
        if (TrySkipPassedAgent()) return;
        if (TryAutoWinForLastAgent()) return;

        StartAgentTurn();
    }

    /// <summary>
    /// Agent 轮次 Update：敌人 AI 延时后自动决策。
    /// </summary>
    public void OnAgentTurnUpdate(float delta)
    {
        var agent = CurrentAgent;
        if (agent == null || agent.IsPlayer || _enemyActed) return;

        _enemyTimer -= delta;
        if (_enemyTimer > 0) return;
        _enemyActed = true;

        ExecuteEnemyTurn(agent);
    }

    /// <summary>玩家出牌处理</summary>
    public string? OnPlayerPlayCards(List<Card> cards)
    {
        var error = ValidatePlayerPlay(cards);
        if (error != null) return error;

        ExecutePlayerPlay(cards);
        return null;
    }

    /// <summary>玩家 Pass 处理</summary>
    public string? OnPlayerPassTurn()
    {
        var player = PlayerAgent;
        if (player == null || !player.IsActive)
        {
            return "现在不是你的回合";
        }

        MarkAgentPassed(player);
        ApplyWinningHandBonusIfNeeded();

        _fsm.TransitionTo<RoundSettlementState>();
        return null;
    }

    /// <summary>玩家叫牌处理</summary>
    public string? OnPlayerCall()
    {
        var player = PlayerAgent;
        if (player == null || !player.IsActive)
        {
            return "现在不是你的回合";
        }
        if (!player.CanCallCards)
        {
            return "叫牌次数已用完";
        }

        ExecuteCallCards(player);
        return null;
    }

    // ────────── RoundSettlement ──────────

    /// <summary>
    /// 回合结算：判定胜负，执行伤害或战败效果。
    /// </summary>
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

    /// <summary>
    /// 回合结束：牌河洗回牌堆，敌人成长抽牌。
    /// </summary>
    public void OnRoundEnd()
    {
        _roundEndDelay = 0.5f;
        ShuffleRiverBackToDeck();
        EnemyGrowthDraw();
        NotifyHandUpdated();
        NotifyRiverUpdated();
    }

    /// <summary>
    /// 回合结束延时 Update，延时后开始新回合。
    /// </summary>
    public void OnRoundEndUpdate(float delta)
    {
        _roundEndDelay -= delta;
        if (_roundEndDelay <= 0)
        {
            _fsm.TransitionTo<RoundStartState>();
        }
    }

    // ────────── BattleEnd ──────────

    /// <summary>
    /// 战斗结束：通知 UI 胜负结果，禁用输入。
    /// </summary>
    public void OnBattleEnd()
    {
        bool playerWon = TotalEnemyHP <= 0;
        NotifyBattleEnded(playerWon);
        EnablePlayerInput(false);
    }

    // ═══════════════════════════════════════════
    //  私有封装方法 —— Init
    // ═══════════════════════════════════════════

    /// <summary>初始化所有 Agent 的牌堆</summary>
    private void InitializeAgentDecks()
    {
        foreach (var agent in Agents)
        {
            agent.Deck?.Initialize();
        }
    }

    /// <summary>重置所有 Agent 的回合状态</summary>
    private void ResetAgentRoundStates()
    {
        foreach (var agent in Agents)
        {
            agent.HasPassed = false;
        }
    }

    /// <summary>重置玩家的叫牌次数</summary>
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

    /// <summary>一张起始牌抽完后回调，继续抽下一张</summary>
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
    //  私有封装方法 —— AgentTurn
    // ═══════════════════════════════════════════

    /// <summary>
    /// 尝试跳过已 Pass 的 Agent。
    /// 返回 true 表示已跳过（状态已切换），调用方无需继续。
    /// </summary>
    private bool TrySkipPassedAgent()
    {
        if (CurrentAgent?.HasPassed != true) return false;

        _skipCount++;
        if (_skipCount > Agents.Count)
        {
            // 全 Pass，按最后出牌方决定胜负
            _fsm.TransitionTo<RoundSettlementState>();
            return true;
        }

        CurrentAgentIndex++;
        _fsm.TransitionTo<AgentTurnState>();
        return true;
    }

    /// <summary>
    /// 当其余 Agent 全部 Pass，当前 Agent 自动获胜。
    /// 返回 true 表示已结算，调用方无需继续。
    /// </summary>
    private bool TryAutoWinForLastAgent()
    {
        var agent = CurrentAgent;
        if (agent == null) return false;

        bool othersAllPassed = Agents
            .Where(a => a != agent)
            .All(a => a.HasPassed);

        if (!othersAllPassed) return false;

        _fsm.TransitionTo<RoundSettlementState>();
        return true;
    }

    /// <summary>根据 Agent 类型开始轮次</summary>
    private void StartAgentTurn()
    {
        var agent = CurrentAgent;
        if (agent == null) return;

        if (agent.IsPlayer)
        {
            BeginPlayerTurn();
        }
        else
        {
            BeginEnemyTurn(agent);
        }
    }

    /// <summary>开始玩家回合：启用输入</summary>
    private void BeginPlayerTurn()
    {
        EnablePlayerInput(true);
        NotifyStatus("请出牌或跳过");
    }

    /// <summary>开始敌人回合：启动 AI 计时</summary>
    private void BeginEnemyTurn(Agent enemy)
    {
        EnablePlayerInput(false);
        _enemyTimer = 0.6f;
        _enemyActed = false;
        NotifyStatus($"{enemy.Id} 思考中...");
    }

    /// <summary>执行敌人 AI 决策并出牌或 Pass</summary>
    private void ExecuteEnemyTurn(Agent enemy)
    {
        if (Chain.LastPlayed == null)
        {
            // 敌人不能先手（玩家总是先手）
            MarkAgentPassed(enemy);
            EndAgentTurn();
            return;
        }

        var result = EnemyAI.FindBestPlay(enemy.Hands, Chain.LastPlayed);
        if (result != null)
        {
            var (handIdx, pattern) = result.Value;
            PlayEnemyCards(enemy, handIdx, pattern);
        }
        else
        {
            MarkAgentPassed(enemy);
        }

        EndAgentTurn();
    }

    /// <summary>敌人出牌并记录</summary>
    private void PlayEnemyCards(Agent enemy, int handIdx, CardPattern pattern)
    {
        enemy.Hands[handIdx].Remove(pattern.Cards);
        River.Add(pattern, enemy);
        Chain.RecordPlay(pattern, enemy);

        NotifyAgentPlayed(enemy.Id, pattern.ToString(), pattern.CardCount);
        NotifyRiverUpdated();
    }

    /// <summary>验证玩家出牌是否合法</summary>
    private string? ValidatePlayerPlay(List<Card> cards)
    {
        var player = PlayerAgent;
        if (player == null || !player.IsActive)
        {
            return "现在不是你的回合";
        }

        var pattern = CardPatternDetector.Detect(cards);
        if (pattern == null)
        {
            return "不是合法牌型";
        }

        if (Chain.LastPlayed != null &&
            !SuppressionJudge.CanSuppress(pattern, Chain.LastPlayed))
        {
            return "无法压制上一手牌";
        }

        return null;
    }

    /// <summary>执行玩家出牌逻辑：移除手牌、计算伤害、记录牌河</summary>
    private void ExecutePlayerPlay(List<Card> cards)
    {
        var player = PlayerAgent!;
        var pattern = CardPatternDetector.Detect(cards)!;
        player.Hands[0].Remove(cards);

        bool isClearHand = player.Hands[0].IsEmpty;
        int damage = CalculatePlayerDamage(pattern, isClearHand);

        if (damage > 0)
        {
            ApplyDamageToEnemies(damage);
        }

        RecordPlayerPlay(pattern, damage);
        NotifyAfterPlayerPlay(pattern, damage);

        if (isClearHand)
        {
            HandlePlayerClearHand(player);
        }
        else
        {
            EndAgentTurn();
        }
    }

    /// <summary>计算玩家出牌伤害</summary>
    private int CalculatePlayerDamage(CardPattern pattern, bool isClearHand)
    {
        return DamageCalculator.Calculate(
            pattern,
            Chain.DepthMultiplier,
            isWinningHand: false,
            isClearHand: isClearHand);
    }

    /// <summary>记录玩家的出牌到牌河和接龙追踪</summary>
    private void RecordPlayerPlay(CardPattern pattern, int damage)
    {
        River.Add(pattern, PlayerAgent!);
        Chain.RecordPlay(pattern, PlayerAgent!);
        PushPlayerPlayDamage(damage);
    }

    /// <summary>玩家出牌后的 UI 通知</summary>
    private void NotifyAfterPlayerPlay(CardPattern pattern, int damage)
    {
        NotifyAgentPlayed(PlayerAgent!.Id, pattern.ToString(), pattern.CardCount);
        NotifyDamage(damage, TotalEnemyHP);
        NotifyHandUpdated();
        NotifyRiverUpdated();
    }

    /// <summary>处理清空手牌：补抽 8 张，直接进结算</summary>
    private void HandlePlayerClearHand(Agent player)
    {
        Chain.ClearHandPlayed = true;
        var drawn = player.Deck!.Draw(8);
        player.Hands[0].AddRange(drawn);
        NotifyHandUpdated();
        _fsm.TransitionTo<RoundSettlementState>();
    }

    /// <summary>标记 Agent 已 Pass</summary>
    private void MarkAgentPassed(Agent agent)
    {
        agent.HasPassed = true;
        Chain.RecordPass(agent);
        NotifyAgentPassed(agent.Id);
    }

    /// <summary>
    /// 如果最后一手是玩家出的，补乘 ×2 伤害。
    /// 清空手牌已在出牌时 ×10，不重复叠加。
    /// </summary>
    private void ApplyWinningHandBonusIfNeeded()
    {
        if (Chain.LastPlayedBy?.IsPlayer == true)
        {
            ApplyWinningHandBonus();
        }
    }

    /// <summary>执行叫牌：从牌堆抽 6 张加入手牌</summary>
    private void ExecuteCallCards(Agent player)
    {
        var drawn = player.Deck!.Draw(player.CardsPerCall);
        player.Hands[0].AddRange(drawn);
        player.RemainingCallCards--;
        NotifyHandUpdated();
    }

    /// <summary>结束当前 Agent 轮次，前进到下一个</summary>
    private void EndAgentTurn()
    {
        CurrentAgentIndex++;
        _fsm.TransitionTo<AgentTurnState>();
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
        // 还有多个活跃（清空手牌场景），玩家自动赢
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

    /// <summary>记录最后一手玩家出牌的伤害值</summary>
    public void PushPlayerPlayDamage(int damage)
    {
        _lastPlayerPlayDamage = damage;
    }

    /// <summary>对最后一手补乘 ×2 伤害</summary>
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
    public void NotifyStatus(string msg)
    {
        UI?.OnStatusMessage(msg);
    }

    /// <summary>通知伤害数值</summary>
    public void NotifyDamage(int dmg, int remaining)
    {
        UI?.OnDamageDealt(dmg, remaining);
    }

    /// <summary>通知 Agent 出牌</summary>
    public void NotifyAgentPlayed(string id, string desc, int cardCount)
    {
        UI?.OnAgentPlayed(id, desc, cardCount);
    }

    /// <summary>通知 Agent Pass</summary>
    public void NotifyAgentPassed(string id)
    {
        UI?.OnAgentPassed(id);
    }

    /// <summary>通知回合结果</summary>
    public void NotifyRoundResult(bool playerWon, string msg)
    {
        UI?.OnRoundResult(playerWon, msg);
    }

    /// <summary>通知战斗结束</summary>
    public void NotifyBattleEnded(bool playerWon)
    {
        UI?.OnBattleEnded(playerWon);
    }

    /// <summary>通知手牌刷新</summary>
    public void NotifyHandUpdated()
    {
        UI?.OnHandUpdated();
    }

    /// <summary>通知牌河刷新</summary>
    public void NotifyRiverUpdated()
    {
        UI?.OnRiverUpdated();
    }
}
