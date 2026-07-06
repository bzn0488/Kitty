using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using GuandanKitty.Core;

namespace GuandanKitty;

/// <summary>
/// 战斗管理器——淘汰制多 Agent 回合状态机
/// </summary>
public partial class BattleManager : Node
{
    [Signal] public delegate void BattleStartedEventHandler();
    [Signal] public delegate void StateChangedEventHandler(string state, string message);
    [Signal] public delegate void PlayerTurnEventHandler();
    [Signal] public delegate void EnemyTurnEventHandler(string agentId);
    [Signal] public delegate void AgentPlayedEventHandler(string agentId, string patternDesc, int cardCount);
    [Signal] public delegate void AgentPassedEventHandler(string agentId);
    [Signal] public delegate void DamageDealtEventHandler(int damage, int remainingHP);
    [Signal] public delegate void RoundResultEventHandler(bool playerWon, string message);
    [Signal] public delegate void BattleEndedEventHandler(bool playerWon);
    [Signal] public delegate void HandUpdatedEventHandler();

    public enum State { Init, RoundStart, AgentTurn, RoundSettlement, RoundEnd, Victory, Defeat }
    public State CurrentState { get; private set; } = State.Init;

    public List<Agent> Agents { get; } = new();
    public CardRiver River { get; } = new();
    public ChainTracker Chain { get; } = new();
    public int TotalEnemyHP => Agents.Where(a => a.IsEnemy)
        .Sum(a => a.Monsters.Sum(m => m.CurrentHP));

    private int _currentAgentIndex;
    private readonly Random _random = new();

    public Agent? PlayerAgent => Agents.FirstOrDefault(a => a.IsPlayer);
    public Agent? CurrentAgent =>
        _currentAgentIndex >= 0 && _currentAgentIndex < Agents.Count
            ? Agents[_currentAgentIndex] : null;

    // ============ 生命周期 ============

    public void StartBattle(List<Agent> agents)
    {
        Agents.Clear();
        Agents.AddRange(agents);
        CurrentState = State.Init;

        // 初始化所有 Agent
        foreach (var agent in Agents)
        {
            agent.Deck?.Initialize();
            if (agent.IsPlayer && agent.Deck != null)
            {
                // 玩家初始抽8张
                agent.Hands[0].AddRange(agent.Deck.Draw(8));
            }
            agent.HasPassed = false;
        }

        EmitSignal(SignalName.BattleStarted);
        BeginRound();
    }

    private void BeginRound()
    {
        CurrentState = State.RoundStart;
        River.Clear();
        Chain.Reset();

        foreach (var agent in Agents)
            agent.HasPassed = false;

        // 从第一个 Agent 开始（总是玩家）
        _currentAgentIndex = 0;
        AdvanceToNextAgent();
    }

    // ============ 轮询核心 ============

    private void AdvanceToNextAgent()
    {
        // 跳过已淘汰的 Agent
        int loopCount = 0;
        while (loopCount < Agents.Count)
        {
            if (_currentAgentIndex < Agents.Count && Agents[_currentAgentIndex].HasPassed)
            {
                _currentAgentIndex++;
                if (_currentAgentIndex >= Agents.Count)
                    _currentAgentIndex = 0;
                loopCount++;
                continue;
            }
            break;
        }

        // 检查活跃 Agent 数量
        var activeAgents = Agents.Where(a => a.IsActive).ToList();

        if (activeAgents.Count == 1)
        {
            var lastAgent = activeAgents[0];
            if (lastAgent.IsPlayer)
            {
                // 只剩玩家 → 等待玩家自压或 Pass
                CurrentState = State.AgentTurn;
                EmitSignal(SignalName.StateChanged, "PlayerOnly", "其他人已全部Pass，你可以自压或Pass");
                EmitSignal(SignalName.PlayerTurn);
            }
            else
            {
                // 玩家已 Pass，敌方是最后一个 → 敌人赢
                SettleRound(winner: lastAgent);
            }
            return;
        }

        if (activeAgents.Count == 0) return;

        _currentAgentIndex %= Agents.Count;
        var currentAgent = Agents[_currentAgentIndex];
        CurrentState = State.AgentTurn;

        if (currentAgent.IsPlayer)
        {
            EmitSignal(SignalName.StateChanged, "PlayerTurn", "请出牌或跳过");
            EmitSignal(SignalName.PlayerTurn);
        }
        else
        {
            EmitSignal(SignalName.StateChanged, "EnemyTurn", $"{currentAgent.Id} 回合");
            EmitSignal(SignalName.EnemyTurn, currentAgent.Id);
            ProcessEnemyTurn(currentAgent);
        }
    }

    // ============ 玩家行动（UI 调用） ============

    public string? PlayerPlay(List<Card> selectedCards)
    {
        var player = PlayerAgent;
        if (player == null || CurrentState != State.AgentTurn || !player.IsActive)
            return "现在不是你的回合";

        var pattern = CardPatternDetector.Detect(selectedCards);
        if (pattern == null)
            return "不是合法牌型";

        // 压制判定
        if (Chain.LastPlayed != null)
        {
            if (!SuppressionJudge.CanSuppress(pattern, Chain.LastPlayed))
                return "无法压制上一手牌";
        }

        // 从手牌移除
        player.Hands[0].Remove(selectedCards);

        // 即时伤害（只有玩家出牌造成伤害）
        bool isClearHand = player.Hands[0].IsEmpty;
        int damage = DamageCalculator.Calculate(pattern, Chain.DepthMultiplier,
            isWinningHand: false, isClearHand: isClearHand);
        ApplyDamageToEnemies(damage);
        EmitSignal(SignalName.DamageDealt, damage, TotalEnemyHP);

        // 记录
        River.Add(pattern, player);
        Chain.RecordPlay(pattern, player);
        EmitSignal(SignalName.AgentPlayed, player.Id, pattern.ToString(), pattern.CardCount);

        if (isClearHand)
        {
            // 清空手牌 → 立即获胜 → 补抽8张
            var drawn = player.Deck!.Draw(8);
            player.Hands[0].AddRange(drawn);
            EmitSignal(SignalName.HandUpdated);
            SettleRound(winner: player);
            return null;
        }

        EmitSignal(SignalName.HandUpdated);

        _currentAgentIndex++;
        AdvanceToNextAgent();
        return null;
    }

    public string? PlayerPass()
    {
        var player = PlayerAgent;
        if (player == null || CurrentState != State.AgentTurn || !player.IsActive)
            return "现在不是你的回合";

        player.HasPassed = true;
        Chain.RecordPass(player);
        EmitSignal(SignalName.AgentPassed, player.Id);

        _currentAgentIndex++;
        AdvanceToNextAgent();
        return null;
    }

    public string? PlayerCallCards()
    {
        var player = PlayerAgent;
        if (player == null || CurrentState != State.AgentTurn || !player.IsActive)
            return "现在不是你的回合";
        if (!player.CanCallCards)
            return "叫牌次数已用完";

        var drawn = player.Deck!.Draw(player.CardsPerCall);
        player.Hands[0].AddRange(drawn);
        player.RemainingCallCards--;
        EmitSignal(SignalName.HandUpdated);
        return null;
    }

    // ============ 敌方行动 ============

    private async void ProcessEnemyTurn(Agent enemy)
    {
        // 短暂延迟模拟思考
        await ToSignal(GetTree().CreateTimer(0.6f), Godot.Timer.SignalName.Timeout);

        if (Chain.LastPlayed == null)
        {
            // 回合首手敌人不能出（玩家总是先手）
            enemy.HasPassed = true;
            Chain.RecordPass(enemy);
            EmitSignal(SignalName.AgentPassed, enemy.Id);
        }
        else
        {
            var result = EnemyAI.FindBestPlay(enemy.Hands, Chain.LastPlayed);
            if (result != null)
            {
                var (handIdx, pattern) = result.Value;
                enemy.Hands[handIdx].Remove(pattern.Cards);
                River.Add(pattern, enemy);
                Chain.RecordPlay(pattern, enemy);
                EmitSignal(SignalName.AgentPlayed, enemy.Id, pattern.ToString(), pattern.CardCount);
                GD.Print($"[Enemy {enemy.Id}] 出牌: {pattern}");
            }
            else
            {
                enemy.HasPassed = true;
                Chain.RecordPass(enemy);
                EmitSignal(SignalName.AgentPassed, enemy.Id);
                GD.Print($"[Enemy {enemy.Id}] Pass");
            }
        }

        _currentAgentIndex++;
        AdvanceToNextAgent();
    }

    // ============ 回合结算 ============

    /// <summary>
    /// 对敌方 HP 造成伤害（从第一个有 HP 的怪物开始扣）
    /// </summary>
    private void ApplyDamageToEnemies(int damage)
    {
        int remaining = damage;
        foreach (var enemy in Agents.Where(a => a.IsEnemy))
        {
            foreach (var monster in enemy.Monsters)
            {
                if (remaining <= 0) return;
                // 怪物 HP 通过动态属性实现：在 CurrentHP getter 中使用公式
                // 此处用简化方式：需要给 Monster 添加可变的 CurrentHP 字段
                // 暂时直接修改 BaseHP 以降低 CurrentHP（原型阶段）
                int currentHP = monster.CurrentHP;
                int deducted = Math.Min(remaining, currentHP);
                remaining -= deducted;
                // 调整为新的"等效BaseHP"
                monster.AdjustHP(-deducted);
            }
        }
    }

    private void SettleRound(Agent winner)
    {
        CurrentState = State.RoundSettlement;

        if (winner.IsPlayer)
        {
            // 如果最后一手是玩家出的，修正为赢回合 ×2
            // （注意：清空手牌 ×10 已在出牌时计算，此处不重复）
            EmitSignal(SignalName.RoundResult, true,
                $"你赢得了本回合！剩余敌人HP: {TotalEnemyHP}");

            if (TotalEnemyHP <= 0)
            {
                CurrentState = State.Victory;
                EmitSignal(SignalName.BattleEnded, true);
                return;
            }
        }
        else
        {
            // 敌方赢 → 战败效果
            foreach (var enemy in Agents.Where(a => a.IsEnemy))
            {
                if (enemy.Monsters.Count > 0)
                {
                    DefeatEffectExecutor.Execute(enemy.Monsters,
                        PlayerAgent!.Hands[0], PlayerAgent.Deck!);
                }
            }

            EmitSignal(SignalName.RoundResult, false,
                $"敌人赢得了本回合！牌堆剩余: {PlayerAgent!.Deck!.Count}");

            if (PlayerAgent.IsDefeated)
            {
                CurrentState = State.Defeat;
                EmitSignal(SignalName.BattleEnded, false);
                return;
            }
        }

        EmitSignal(SignalName.HandUpdated);
        EndRound();
    }

    private async void EndRound()
    {
        CurrentState = State.RoundEnd;

        // 牌河洗回
        var allRiverCards = River.GetAllCards();
        PlayerAgent!.Deck!.ShuffleBack(allRiverCards);
        River.Clear();

        // 敌方成长
        foreach (var enemy in Agents.Where(a => a.IsEnemy))
        {
            foreach (var monster in enemy.Monsters)
            {
                for (int i = 0; i < monster.DrawsPerRound; i++)
                {
                    var card = monster.DrawFromPool(_random);
                    enemy.Hands[0].Add(card);
                }
            }
        }

        await ToSignal(GetTree().CreateTimer(0.5f), Godot.Timer.SignalName.Timeout);
        BeginRound();
    }
}
