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
    public PlayerAgent? PlayerAgent => Agents.OfType<PlayerAgent>().FirstOrDefault();

    /// <summary>敌方总 HP</summary>
    public int TotalEnemyHP => Agents
        .OfType<EnemyAgent>()
        .Sum(a => a.TotalHP);

    /// <summary>玩家输入是否启用</summary>
    public bool IsPlayerInputEnabled { get; private set; }

    /// <summary>当前回合是否已结束（防止重复结算）</summary>
    public bool IsRoundOver { get; private set; }

    /// <summary>当前战斗中 Agent（供 TurnFSM 状态访问）</summary>
    public static Battle Current { get; private set; } = null!;

    // ═══════════════════════════════════════════
    //  私有字段
    // ═══════════════════════════════════════════

    private readonly StandardDeck _playerDeck;
    private BattleUI _ui = null!;
    private BattleFSM _fsm = null!;
    private int _lastPlayDamage;
    private float _roundEndDelay;

    // 敌人 AI 相关
    private float _turnEnemyTimer;
    private bool _turnEnemyActed;

    // ═══════════════════════════════════════════
    //  构造与生命周期
    // ═══════════════════════════════════════════

    /// <summary>
    /// 创建 Battle 实例。需调用 Initialize() 后才开始运行。
    /// </summary>
    public Battle(StandardDeck playerDeck)
    {
        _playerDeck = playerDeck;
    }

    /// <summary>
    /// 初始化战斗：加载 BattleUI、创建 Agent、注册 FSM、开始战斗。
    /// </summary>
    public void Initialize()
    {
        Current = this;

        // 加载 BattleUI
        _ui = UIRoot.Instance.LoadBattleScene();

        CreateAgents();

        _ui.SetPlayerAgent(PlayerAgent!);
        _ui.SetEnemyAgent(Agents.OfType<EnemyAgent>().First());

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
        var player = PlayerAgent;
        if (player == null) return;

        var err = player.TryPlayCards(cards);
        if (err != null)
        {
            NotifyStatus(err);
            return;
        }

        NotifyAgentPlayed(player.Id, Chain.LastPlayed?.ToString() ?? "", Chain.LastPlayed?.CardCount ?? 0);
        NotifyHandUpdated();
        NotifyRiverUpdated();
        _fsm.TurnFSM.TransitionTo<TurnAfterPlayState>();
    }

    private void OnPassRequested()
    {
        var player = PlayerAgent;
        if (player == null) return;

        player.TryPass();
        NotifyAgentPassed(player.Id);
        _fsm.TurnFSM.TransitionTo<TurnEndState>();
    }

    private void OnCallRequested()
    {
        var player = PlayerAgent;
        if (player == null) return;

        var err = player.TryCallCards();
        if (err != null)
        {
            NotifyStatus(err);
            return;
        }

        NotifyHandUpdated();
        _fsm.TurnFSM.TransitionTo<TurnEndState>();
    }

    // ═══════════════════════════════════════════
    //  FSM 回调 —— BattleStart
    // ═══════════════════════════════════════════

    /// <summary>战斗开始：布置敌人初始手牌、玩家起始手牌</summary>
    internal void OnBattleStart()
    {
        DrawEnemyInitialHands();
        DrawStartHand();
        NotifyHandUpdated();
        NotifyEnemyHandUpdated();
        _fsm.TransitionTo<RoundStartState>();
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

        if (winner is PlayerAgent)
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
        NotifyEnemyHandUpdated();
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
    //  Turn 方法
    // ═══════════════════════════════════════════

    /// <summary>
    /// Judge：当前轮是否继续。
    /// </summary>
    public bool JudgeTurn()
    {
        if (IsRoundOver) return false;
        return ActiveAgents.Count > 0;
    }

    /// <summary>
    /// Start：设置当前 Agent，玩家启输入，敌人启倒计时。
    /// </summary>
    public void OnTurnStart()
    {
        if (IsRoundOver) return;

        var agent = ActiveAgents.FirstOrDefault();
        if (agent == null) return;

        if (agent is PlayerAgent)
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
    /// Start Update：敌人倒计时，到时间自动决策。
    /// </summary>
    public void OnTurnStartUpdate(float delta)
    {
        if (IsRoundOver) return;

        var agent = ActiveAgents.FirstOrDefault();
        if (agent == null || agent is PlayerAgent || _turnEnemyActed) return;

        _turnEnemyTimer -= delta;
        if (_turnEnemyTimer > 0) return;
        _turnEnemyActed = true;

        if (agent is EnemyAgent enemy)
        {
            bool played = enemy.DecideAndPlay();
            if (played)
            {
                NotifyAgentPlayed(agent.Id, Chain.LastPlayed?.ToString() ?? "", Chain.LastPlayed?.CardCount ?? 0);
                NotifyRiverUpdated();
                NotifyEnemyHandUpdated();
                _fsm.TurnFSM.TransitionTo<TurnAfterPlayState>();
            }
            else
            {
                NotifyAgentPassed(agent.Id);
                _fsm.TurnFSM.TransitionTo<TurnEndState>();
            }
        }
    }

    /// <summary>
    /// AfterPlay：结算一手牌的伤害和效果。
    /// </summary>
    public void OnTurnAfterPlay()
    {
        if (IsRoundOver) return;

        var lastAgent = Chain.LastPlayedBy;
        var lastPattern = Chain.LastPlayed;
        if (lastAgent == null || lastPattern == null) return;

        // 只有玩家出牌造成伤害
        if (lastAgent is PlayerAgent player)
        {
            bool isClearHand = player.Hand.IsEmpty;
            int damage = DamageCalculator.Calculate(
                lastPattern, Chain.DepthMultiplier,
                isWinningHand: false, isClearHand: isClearHand);

            if (damage > 0)
            {
                ApplyDamageToEnemies(damage);
            }

            _lastPlayDamage = damage;
            NotifyDamage(damage, TotalEnemyHP);

            if (isClearHand)
            {
                Chain.ClearHandPlayed = true;
                var drawn = player.Deck!.Draw(8);
                player.Hand.AddRange(drawn);
            }
        }

        NotifyHandUpdated();
    }

    /// <summary>
    /// End：轮结束回调（预留效果触发）。
    /// </summary>
    public void OnTurnEnd()
    {
        // 预留：触发"轮结束"效果
    }

    /// <summary>
    /// 推进到下一 Agent，若只剩一个则结束回合。
    /// </summary>
    public void AdvanceToNext()
    {
        if (IsRoundOver) return;

        // 当前 Agent 移到队尾
        if (ActiveAgents.Count > 0)
        {
            var first = ActiveAgents[0];
            ActiveAgents.RemoveAt(0);
            ActiveAgents.Add(first);
        }

        // 只剩玩家一个 → 回合结束
        if (ActiveAgents.Count == 1 && ActiveAgents[0] is PlayerAgent)
        {
            if (Chain.LastPlayedBy is PlayerAgent)
            {
                ApplyWinningHandBonus(_lastPlayDamage);
            }
            IsRoundOver = true;
            _fsm.TurnFSM.CompleteTurn();
        }
    }

    // ═══════════════════════════════════════════
    //  私有方法 —— Init
    // ═══════════════════════════════════════════

    /// <summary>创建玩家和测试敌人 Agent</summary>
    private void CreateAgents()
    {
        Agents.Clear();
        Agents.Add(new PlayerAgent(this, _playerDeck, "玩家"));
        Agents.Add(new EnemyAgent(this, "训练假人",
            new List<Monster> { MonsterDatabase.CreateTestMonster() }));
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
        foreach (var enemy in Agents.OfType<EnemyAgent>())
            enemy.DrawInitialHand(rng);
    }

    /// <summary>玩家抽起始 8 张手牌</summary>
    private void DrawStartHand()
    {
        var player = PlayerAgent!;
        var drawn = player.DrawFromDeck(8);
        player.Hand.AddRange(drawn);
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
        var player = PlayerAgent!;
        foreach (var enemy in Agents.OfType<EnemyAgent>())
            enemy.ExecuteDefeatEffects(player);
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
        foreach (var enemy in Agents.OfType<EnemyAgent>())
            enemy.DrawGrowthCards(rng);
    }

    // ═══════════════════════════════════════════
    //  公共工具方法
    // ═══════════════════════════════════════════

    /// <summary>对最后一手补乘 ×2 伤害（清空手牌不重复叠加）。
    /// 取最后一手出牌的伤害值 ×1 补算。</summary>
    public void ApplyWinningHandBonus(int lastPlayDamage)
    {
        if (lastPlayDamage > 0 && !Chain.ClearHandPlayed)
        {
            ApplyDamageToEnemies(lastPlayDamage);
        }
    }

    /// <summary>对敌方全体造成伤害，从第一个怪物开始扣</summary>
    public void ApplyDamageToEnemies(int damage)
    {
        int remaining = damage;
        foreach (var enemy in Agents.OfType<EnemyAgent>())
        {
            if (remaining <= 0) break;
            foreach (var monster in enemy.Monsters)
            {
                if (remaining <= 0) break;
                int deducted = Math.Min(remaining, monster.CurrentHP);
                remaining -= deducted;
                monster.AdjustHP(-deducted);
            }
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

    /// <summary>通知敌方手牌刷新</summary>
    public void NotifyEnemyHandUpdated()
    {
        _ui.OnEnemyHandUpdated();
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
                entries.Select(e => $"[{(e.Agent is PlayerAgent ? "你" : "敌")}] {e.Pattern}"));
        }

        var enemy = Agents.OfType<EnemyAgent>().FirstOrDefault();
        string enemyHandText;
        if (enemy == null)
        {
            enemyHandText = "[敌方手牌]";
        }
        else
        {
            enemyHandText = $"[敌方手牌] {string.Join(" ", enemy.Hand.Cards.Select(c => c))} (共{enemy.Hand.Count}张)";
        }

        _ui.OnRiverUpdated(riverText, enemyHandText);
    }
}

