using System;
using System.Collections.Generic;
using System.Linq;
using GuandanKitty.Core;

namespace GuandanKitty;

/// <summary>
/// 战斗逻辑控制器 —— 纯 C# 类，不继承 Node。
/// 持有所有游戏状态（Agents/River/Chain），通过 FSM 控制流转，通过 UI 驱动显示。
/// </summary>
public class Battle
{
    // ═══════════════════════════════════════════
    //  公开属性
    // ═══════════════════════════════════════════

    /// <summary>所有参战方列表（含座次顺序）</summary>
    public List<Agent> Agents { get; } = new();

    /// <summary>当前回合的活跃 Agent 集合（Pass 即移除）</summary>
    public List<Agent> ActiveAgents { get; } = new();

    /// <summary>牌河</summary>
    public CardRiver River { get; } = new();

    /// <summary>接龙追踪器</summary>
    public ChainTracker Chain { get; } = new();

    /// <summary>玩家 Agent</summary>
    public Agent? PlayerAgent => Agents.FirstOrDefault(a => a.IsPlayer);

    /// <summary>敌方总 HP</summary>
    public int TotalEnemyHP => Agents
        .Where(a => a.IsEnemy)
        .Sum(a => a.Monsters.Sum(m => m.CurrentHP));

    /// <summary>玩家输入是否启用</summary>
    public bool IsPlayerInputEnabled { get; private set; }

    /// <summary>当前回合是否已结束（防止重复结算）</summary>
    public bool IsRoundOver { get; private set; }

    // ═══════════════════════════════════════════
    //  私有字段
    // ═══════════════════════════════════════════

    private readonly StandardDeck _playerDeck;
    private readonly BattleUI _ui;
    private BattleFSM _fsm = null!;
    private int _lastPlayerPlayDamage;
    private float _roundEndDelay;

    // 敌人 AI 相关
    private float _turnEnemyTimer;
    private bool _turnEnemyActed;

    // 初始抽牌序列
    private int _drawCardsTotal;
    private int _drawCardsCurrent;

    // ═══════════════════════════════════════════
    //  构造与生命周期
    // ═══════════════════════════════════════════

    /// <summary>
    /// 创建 Battle 实例。需调用 Initialize() 后才开始运行。
    /// </summary>
    public Battle(StandardDeck playerDeck, BattleUI ui)
    {
        _playerDeck = playerDeck;
        _ui = ui;
    }

    /// <summary>
    /// 初始化战斗：创建 Agent、注册 FSM、订阅 UI 事件、开始战斗。
    /// </summary>
    public void Initialize()
    {
        CreateAgents();

        // 设置 UI 数据引用
        _ui.SetPlayerAgent(PlayerAgent!);

        _ui.PlayRequested += OnPlayRequested;
        _ui.PassRequested += OnPassRequested;
        _ui.CallRequested += OnCallRequested;
        _ui.ShowBattle();

        _fsm = new BattleFSM(this);
        _fsm.TransitionTo<BattleStartState>();
    }

    /// <summary>
    /// 每帧更新，由 Run._Process 驱动。
    /// </summary>
    public void Update(float delta)
    {
        _fsm.Update(delta);
    }

    /// <summary>
    /// 结束战斗：取消订阅、隐藏 UI、清理引用。
    /// </summary>
    public void End()
    {
        _ui.PlayRequested -= OnPlayRequested;
        _ui.PassRequested -= OnPassRequested;
        _ui.CallRequested -= OnCallRequested;
        _ui.HideBattle();
        _fsm = null!;
    }

    // ═══════════════════════════════════════════
    //  UI 事件处理（玩家输入入口）
    // ═══════════════════════════════════════════

    private void OnPlayRequested(List<Card> cards)
    {
        var err = _fsm.CurrentState?.OnPlayerPlay(cards);
        if (err != null)
            NotifyStatus(err);
    }

    private void OnPassRequested()
    {
        var err = _fsm.CurrentState?.OnPlayerPass();
        if (err != null)
            NotifyStatus(err);
    }

    private void OnCallRequested()
    {
        var err = _fsm.CurrentState?.OnPlayerCallCards();
        if (err != null)
            NotifyStatus(err);
    }

    // ═══════════════════════════════════════════
    //  FSM 回调 —— BattleStart
    // ═══════════════════════════════════════════

    /// <summary>战斗开始：布置敌人初始手牌、玩家逐张摸起始牌</summary>
    internal void OnBattleStart()
    {
        DrawEnemyInitialHands();
        DrawStartHand();
    }

    // ═══════════════════════════════════════════
    //  FSM 回调 —— RoundStart
    // ═══════════════════════════════════════════

    /// <summary>回合开始：重建 ActiveAgents、清空牌河和接龙记录</summary>
    internal void OnRoundStart()
    {
        ResetRoundState();
        NotifyHandUpdated();
        NotifyRiverUpdated();
        NotifyStatus("回合开始");
        _fsm.TransitionTo<AgentTurnState>();
    }

    // ═══════════════════════════════════════════
    //  FSM 回调 —— RoundSettlement
    // ═══════════════════════════════════════════

    /// <summary>回合结算：判定胜负，执行伤害或战败效果</summary>
    internal void OnRoundSettlement()
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

    // ═══════════════════════════════════════════
    //  FSM 回调 —— RoundEnd
    // ═══════════════════════════════════════════

    /// <summary>回合结束：牌河洗回牌堆，敌人成长抽牌</summary>
    internal void OnRoundEnd()
    {
        _roundEndDelay = 0.5f;
        ShuffleRiverBackToDeck();
        EnemyGrowthDraw();
        NotifyHandUpdated();
        NotifyRiverUpdated();
    }

    /// <summary>回合结束延时，到时间后开始新回合</summary>
    internal void OnRoundEndUpdate(float delta)
    {
        _roundEndDelay -= delta;
        if (_roundEndDelay <= 0)
        {
            _fsm.TransitionTo<RoundStartState>();
        }
    }

    // ═══════════════════════════════════════════
    //  FSM 回调 —— BattleEnd
    // ═══════════════════════════════════════════

    /// <summary>战斗结束：通知 UI 胜负结果，禁用输入</summary>
    internal void OnBattleEnd()
    {
        bool playerWon = TotalEnemyHP <= 0;
        NotifyBattleEnded(playerWon);
        EnablePlayerInput(false);
    }

    // ═══════════════════════════════════════════
    //  Turn 回调 —— TurnJudge
    // ═══════════════════════════════════════════

    /// <summary>
    /// Turn Judge：检查 ActiveAgents，决定进 Act 还是结束回合。
    /// 特例：玩家是唯一活跃者 → 允许自压（继续 Act）。
    /// </summary>
    internal void OnTurnJudge()
    {
        if (IsRoundOver) return;

        if (ActiveAgents.Count == 1)
        {
            var sole = ActiveAgents[0];
            if (sole.IsPlayer)
            {
                // 玩家是唯一活跃者 → 允许自压
                _fsm.TurnTransitionTo<TurnActState>();
            }
            else
            {
                // 敌人是唯一活跃者 → 回合结束
                IsRoundOver = true;
                _fsm.RaiseTurnComplete();
            }
        }
        else if (ActiveAgents.Count > 1)
        {
            _fsm.TurnTransitionTo<TurnActState>();
        }
        else
        {
            IsRoundOver = true;
            _fsm.RaiseTurnComplete();
        }
    }

    // ═══════════════════════════════════════════
    //  Turn 回调 —— TurnAct
    // ═══════════════════════════════════════════

    /// <summary>
    /// Turn Act：根据当前活跃 Agent 类型开始行动。
    /// </summary>
    internal void OnTurnAct()
    {
        if (IsRoundOver) return;

        var agent = ActiveAgents.FirstOrDefault();
        if (agent == null)
        {
            _fsm.TurnTransitionTo<TurnAdvanceState>();
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

    /// <summary>
    /// Turn Act Update：敌人 AI 倒计时，到时间后自动决策。
    /// </summary>
    internal void OnTurnActUpdate(float delta)
    {
        if (IsRoundOver) return;

        var agent = ActiveAgents.FirstOrDefault();
        if (agent == null || agent.IsPlayer || _turnEnemyActed) return;

        _turnEnemyTimer -= delta;
        if (_turnEnemyTimer > 0) return;
        _turnEnemyActed = true;

        if (Chain.LastPlayed == null)
        {
            // 敌人不能先手（玩家总是先手）→ 淘汰
            ActiveAgents.Remove(agent);
            Chain.RecordPass(agent);
            NotifyAgentPassed(agent.Id);
            _fsm.TurnFSM.LastActionWasPass = true;
            _fsm.TurnFSM.LastActionPattern = null;
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
                _fsm.TurnFSM.LastActionWasPass = false;
                _fsm.TurnFSM.LastActionPattern = pattern;
            }
            else
            {
                ActiveAgents.Remove(agent);
                Chain.RecordPass(agent);
                NotifyAgentPassed(agent.Id);
                _fsm.TurnFSM.LastActionWasPass = true;
                _fsm.TurnFSM.LastActionPattern = null;
            }
        }

        _fsm.TurnTransitionTo<TurnResolveState>();
    }

    /// <summary>
    /// Turn 玩家出牌：验证牌型、扣除手牌、即时伤害、记录牌河。
    /// </summary>
    internal string? OnTurnPlayerPlay(List<Card> cards)
    {
        if (IsRoundOver) return "回合已结束";

        var player = PlayerAgent;
        if (player == null || !ActiveAgents.Contains(player)) return "现在不是你的回合";

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
        _lastPlayerPlayDamage = damage;

        NotifyAgentPlayed(player.Id, pattern.ToString(), pattern.CardCount);
        NotifyDamage(damage, TotalEnemyHP);
        NotifyHandUpdated();
        NotifyRiverUpdated();

        _fsm.TurnFSM.LastActionWasPass = false;
        _fsm.TurnFSM.LastActionIsClearHand = isClearHand;
        _fsm.TurnFSM.LastActionPattern = pattern;

        _fsm.TurnTransitionTo<TurnResolveState>();
        return null;
    }

    /// <summary>
    /// Turn 玩家 Pass：从 ActiveAgents 移除，判断是否触发回合结束。
    /// </summary>
    internal string? OnTurnPlayerPass()
    {
        if (IsRoundOver) return "回合已结束";

        var player = PlayerAgent;
        if (player == null || !ActiveAgents.Contains(player)) return "现在不是你的回合";

        ActiveAgents.Remove(player);
        Chain.RecordPass(player);
        NotifyAgentPassed(player.Id);

        if (Chain.LastPlayedBy?.IsPlayer == true)
        {
            ApplyWinningHandBonus();
        }

        IsRoundOver = true;
        _fsm.RaiseTurnComplete();
        return null;
    }

    /// <summary>
    /// Turn 玩家叫牌：从牌堆抽 6 张。
    /// </summary>
    internal string? OnTurnPlayerCall()
    {
        if (IsRoundOver) return "回合已结束";

        var player = PlayerAgent;
        if (player == null || !ActiveAgents.Contains(player)) return "现在不是你的回合";
        if (!player.CanCallCards) return "叫牌次数已用完";

        var drawn = player.Deck!.Draw(player.CardsPerCall);
        player.Hands[0].AddRange(drawn);
        player.RemainingCallCards--;
        NotifyHandUpdated();
        return null;
    }

    // ═══════════════════════════════════════════
    //  Turn 回调 —— TurnResolve
    // ═══════════════════════════════════════════

    /// <summary>
    /// Turn Resolve：根据 Act 阶段的结果决定继续轮还是结束回合。
    /// </summary>
    internal void OnTurnResolve()
    {
        if (IsRoundOver) return;

        var lastAgent = Chain.LastPlayedBy;
        bool wasPass = _fsm.TurnFSM.LastActionWasPass;
        bool wasClearHand = _fsm.TurnFSM.LastActionIsClearHand;

        if (wasPass)
        {
            _fsm.TurnTransitionTo<TurnAdvanceState>();
        }
        else if (wasClearHand && lastAgent?.IsPlayer == true)
        {
            var player = PlayerAgent!;
            Chain.ClearHandPlayed = true;
            var drawn = player.Deck!.Draw(8);
            player.Hands[0].AddRange(drawn);
            NotifyHandUpdated();
            IsRoundOver = true;
            _fsm.RaiseTurnComplete();
        }
        else
        {
            _fsm.TurnTransitionTo<TurnAdvanceState>();
        }
    }

    // ═══════════════════════════════════════════
    //  Turn 回调 —— TurnAdvance
    // ═══════════════════════════════════════════

    /// <summary>
    /// Turn Advance：将当前 Agent 移到队尾（循环），回到 Judge。
    /// </summary>
    internal void OnTurnAdvance()
    {
        if (IsRoundOver) return;

        if (ActiveAgents.Count > 0)
        {
            var first = ActiveAgents[0];
            ActiveAgents.RemoveAt(0);
            ActiveAgents.Add(first);
        }

        _fsm.TurnTransitionTo<TurnJudgeState>();
    }

    // ═══════════════════════════════════════════
    //  私有方法 —— Init
    // ═══════════════════════════════════════════

    /// <summary>创建玩家和测试敌人 Agent</summary>
    private void CreateAgents()
    {
        Agents.Clear();

        var player = new Agent
        {
            Id = "玩家",
            Type = AgentType.Player,
            Deck = _playerDeck.Clone(),
        };
        player.Hands.Add(new HandZone());
        Agents.Add(player);

        var enemy = Agent.SpawnAgentFromEnemy("训练假人",
            new List<Monster> { MonsterDatabase.CreateTestMonster() });
        Agents.Add(enemy);
    }

    /// <summary>重置所有 Agent 的叫牌次数</summary>
    private void ResetPlayerCallCounts()
    {
        var player = PlayerAgent;
        if (player != null)
        {
            player.RemainingCallCards = player.MaxCallCards;
        }
    }

    // ═══════════════════════════════════════════
    //  私有方法 —— BattleStart
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
    private void DrawStartHand()
    {
        //todo
    }

    /// <summary>判断玩家牌堆是否可抽牌</summary>
    private bool CanPlayerDraw()
    {
        var player = PlayerAgent;
        return player?.Deck != null && !player.Deck.IsEmpty;
    }

    // ═══════════════════════════════════════════
    //  私有方法 —— RoundStart
    // ═══════════════════════════════════════════

    /// <summary>重置回合相关状态：牌河、接龙、ActiveAgents</summary>
    private void ResetRoundState()
    {
        River.Clear();
        Chain.Reset();
        IsRoundOver = false;

        ActiveAgents.Clear();
        foreach (var agent in Agents)
        {
            ActiveAgents.Add(agent);
        }

        ResetPlayerCallCounts();
    }

    // ═══════════════════════════════════════════
    //  私有方法 —— RoundSettlement
    // ═══════════════════════════════════════════

    /// <summary>判定回合胜者：检查最后留在 ActiveAgents 中的 Agent</summary>
    private Agent DetermineRoundWinner()
    {
        if (ActiveAgents.Count == 1)
        {
            return ActiveAgents[0];
        }

        var lastPlayedBy = Chain.LastPlayedBy;
        if (lastPlayedBy != null)
        {
            return lastPlayedBy;
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
    //  私有方法 —— RoundEnd
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
        _ui.OnEnemyHpChanged(TotalEnemyHP);
    }

    /// <summary>启用/禁用玩家输入</summary>
    public void EnablePlayerInput(bool enabled)
    {
        IsPlayerInputEnabled = enabled;
        _ui.OnPlayerInputChanged(enabled);
    }

    // ═══════════════════════════════════════════
    //  UI 通知方法
    // ═══════════════════════════════════════════

    /// <summary>更新状态消息</summary>
    public void NotifyStatus(string msg) => _ui.OnStatusMessage(msg);

    /// <summary>通知伤害数值</summary>
    public void NotifyDamage(int dmg, int remaining) => _ui.OnDamageDealt(dmg, remaining);

    /// <summary>通知 Agent 出牌</summary>
    public void NotifyAgentPlayed(string id, string desc, int cardCount)
        => _ui.OnAgentPlayed(id, desc, cardCount);

    /// <summary>通知 Agent Pass</summary>
    public void NotifyAgentPassed(string id) => _ui.OnAgentPassed(id);

    /// <summary>通知回合结果</summary>
    public void NotifyRoundResult(bool playerWon, string msg) => _ui.OnRoundResult(playerWon, msg);

    /// <summary>通知战斗结束</summary>
    public void NotifyBattleEnded(bool playerWon) => _ui.OnBattleEnded(playerWon);

    /// <summary>通知手牌刷新</summary>
    public void NotifyHandUpdated()
    {
        var player = PlayerAgent;
        _ui.OnHandUpdated(
            player?.Deck?.Count ?? 0,
            player?.RemainingCallCards ?? 0,
            Chain.DepthMultiplier,
            Chain.PlayerHandCount);
    }

    /// <summary>通知牌河刷新</summary>
    public void NotifyRiverUpdated()
    {
        var entries = River.Entries;
        string riverText;
        if (entries.Count == 0)
        {
            riverText = "[牌河]";
        }
        else
        {
            riverText = string.Join("  →  ",
                entries.Select(e => $"[{(e.Agent.IsPlayer ? "你" : "敌")}] {e.Pattern}"));
        }

        var enemy = Agents.FirstOrDefault(a => a.IsEnemy);
        string enemyHandText;
        if (enemy == null)
        {
            enemyHandText = "[敌方手牌]";
        }
        else
        {
            enemyHandText = $"[敌方手牌] {string.Join(" ", enemy.Hands[0].Cards.Select(c => c))} (共{enemy.Hands[0].Count}张)";
        }

        _ui.OnRiverUpdated(riverText, enemyHandText);
    }
}

