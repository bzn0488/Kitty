using System;
using System.Collections.Generic;
using System.Linq;

namespace GuandanKitty.Core;

/// <summary>
/// 敌方 Agent —— 挂载怪物列表，拥有 AI 决策、伤害承受、战败效果执行能力。
/// </summary>
public class EnemyAgent : Agent
{
    /// <summary>挂载的怪物列表</summary>
    public List<Monster> Monsters { get; } = new();

    /// <summary>怪物总血量</summary>
    public int TotalHP => Monsters.Sum(m => m.CurrentHP);

    /// <summary>敌人不会因牌堆耗尽而战败（由 Battle 判定 HP）</summary>
    public override bool IsDefeated => false;

    /// <summary>
    /// 创建一个敌方 Agent，挂载指定怪物列表。
    /// </summary>
    public EnemyAgent(Battle battle, string id, List<Monster> monsters)
        : base(battle, id)
    {
        Monsters.AddRange(monsters);
    }

    // ═══════════════════════════════════════════
    //  抽牌
    // ═══════════════════════════════════════════

    /// <summary>
    /// 战斗开始：从所有怪物牌池各抽 3 张初始手牌。
    /// </summary>
    public void DrawInitialHand(Random rng)
    {
        foreach (var monster in Monsters)
        {
            for (int i = 0; i < 3; i++)
            {
                Hand.Add(monster.DrawFromPool(rng));
            }
        }
    }

    /// <summary>
    /// 回合结束：从所有怪物牌池按各自的 DrawsPerRound 抽牌。
    /// </summary>
    public void DrawGrowthCards(Random rng)
    {
        foreach (var monster in Monsters)
        {
            for (int i = 0; i < monster.DrawsPerRound; i++)
            {
                Hand.Add(monster.DrawFromPool(rng));
            }
        }
    }

    // ═══════════════════════════════════════════
    //  伤害
    // ═══════════════════════════════════════════

    /// <summary>
    /// 承受伤害，从第一个怪物开始扣血。
    /// </summary>
    public void ApplyDamage(int damage)
    {
        int remaining = damage;
        foreach (var monster in Monsters)
        {
            if (remaining <= 0) break;
            int deducted = Math.Min(remaining, monster.CurrentHP);
            remaining -= deducted;
            monster.AdjustHP(-deducted);
        }
    }

    // ═══════════════════════════════════════════
    //  战败效果
    // ═══════════════════════════════════════════

    /// <summary>
    /// 执行所有怪物的战败效果：从玩家手牌移出牌 → 永久删除 → 补抽等量。
    /// </summary>
    public void ExecuteDefeatEffects(PlayerAgent player)
    {
        int totalRemoved = 0;

        foreach (var monster in Monsters)
        {
            if (monster.DefeatEffect == null) continue;

            var toRemove = monster.DefeatEffect.SelectCardsToRemove(player.Hand);
            if (toRemove.Count == 0) continue;

            player.Hand.Remove(toRemove);
            player.Deck.RemovePermanently(toRemove);
            totalRemoved += toRemove.Count;
        }

        if (totalRemoved > 0)
        {
            var drawn = player.Deck.Draw(totalRemoved);
            player.Hand.AddRange(drawn);
        }
    }

    // ═══════════════════════════════════════════
    //  AI 决策
    // ═══════════════════════════════════════════

    /// <summary>
    /// AI 自动决策：搜索手牌 → 出牌或 Pass。返回是否出了牌。
    /// </summary>
    public bool DecideAndPlay()
    {
        if (Battle.Chain.LastPlayed == null)
        {
            // 敌人不能先手，直接 Pass
            TryPass();
            return false;
        }

        var pattern = FindBestPlay(Battle.Chain.LastPlayed);
        if (pattern != null)
        {
            var cards = pattern.Cards;
            TryPlayCards(cards);
            return true;
        }

        TryPass();
        return false;
    }

    /// <summary>
    /// 在手牌中寻找能压制 target 的牌型。
    /// 返回 pattern 或 null（Pass）。
    /// 策略：选 CompareValue 最小的合法牌型。
    /// </summary>
    public CardPattern? FindBestPlay(CardPattern target)
    {
        var candidates = FindAllValidPlays(Hand, target);
        CardPattern? bestPattern = null;

        foreach (var p in candidates)
        {
            if (bestPattern == null ||
                p.CompareValue < bestPattern.CompareValue ||
                (p.CompareValue == bestPattern.CompareValue && p.CardCount < bestPattern.CardCount))
            {
                bestPattern = p;
            }
        }

        return bestPattern;
    }

    /// <summary>
    /// 在手牌中暴力搜索所有能压制 target 的合法牌型。
    /// </summary>
    private List<CardPattern> FindAllValidPlays(HandZone hand, CardPattern target)
    {
        var results = new List<CardPattern>();
        var cards = hand.Cards;

        if (target.Type == PatternType.Bomb)
        {
            FindBombs(cards, target.CompareValue, results);
        }
        else
        {
            switch (target.Type)
            {
                case PatternType.Single:
                    FindSingles(cards, target.CompareValue, results);
                    break;
                case PatternType.Pair:
                    FindPairs(cards, target.CompareValue, results);
                    break;
                case PatternType.Triple:
                    FindTriples(cards, target.CompareValue, results);
                    break;
                case PatternType.Straight:
                    FindStraights(cards, target.CardCount, target.CompareValue, results);
                    break;
                case PatternType.Tractor:
                    FindTractors(cards, target.CardCount / 2, target.CompareValue, results);
                    break;
            }
            FindBombs(cards, 0, results);
        }

        return results;
    }

    private static void FindSingles(IReadOnlyList<Card> cards, int minValue, List<CardPattern> results)
    {
        foreach (var card in cards)
        {
            if (card.FaceValue > minValue)
                results.Add(new CardPattern(PatternType.Single, new List<Card> { card }, card.FaceValue));
        }
    }

    private static void FindPairs(IReadOnlyList<Card> cards, int minValue, List<CardPattern> results)
    {
        var groups = cards.GroupBy(c => c.FaceValue)
            .Where(g => g.Count() >= 2 && g.Key > minValue);
        foreach (var g in groups)
        {
            var pair = g.Take(2).ToList();
            results.Add(new CardPattern(PatternType.Pair, pair, g.Key));
        }
    }

    private static void FindTriples(IReadOnlyList<Card> cards, int minValue, List<CardPattern> results)
    {
        var groups = cards.GroupBy(c => c.FaceValue)
            .Where(g => g.Count() >= 3 && g.Key > minValue);
        foreach (var g in groups)
        {
            var triple = g.Take(3).ToList();
            results.Add(new CardPattern(PatternType.Triple, triple, g.Key));
        }
    }

    private static void FindStraights(IReadOnlyList<Card> cards, int length, int minMaxValue, List<CardPattern> results)
    {
        var sorted = cards.DistinctBy(c => c.FaceValue).OrderBy(c => c.FaceValue).ToList();
        if (sorted.Count < length) return;

        for (int i = 0; i <= sorted.Count - length; i++)
        {
            bool isConsecutive = true;
            for (int j = 1; j < length; j++)
            {
                if (sorted[i + j].FaceValue != sorted[i + j - 1].FaceValue + 1)
                {
                    isConsecutive = false;
                    break;
                }
            }
            if (isConsecutive && sorted[i + length - 1].FaceValue > minMaxValue)
            {
                var straight = sorted.Skip(i).Take(length).ToList();
                results.Add(new CardPattern(PatternType.Straight, straight, straight[^1].FaceValue));
            }
        }
    }

    private static void FindTractors(IReadOnlyList<Card> cards, int pairCount, int minMaxValue, List<CardPattern> results)
    {
        var groups = cards.GroupBy(c => c.FaceValue)
            .Where(g => g.Count() >= 2)
            .OrderBy(g => g.Key)
            .ToList();

        for (int i = 0; i <= groups.Count - pairCount; i++)
        {
            bool isConsecutive = true;
            for (int j = 1; j < pairCount; j++)
            {
                if (groups[i + j].Key != groups[i + j - 1].Key + 1)
                {
                    isConsecutive = false;
                    break;
                }
            }
            if (isConsecutive && groups[i + pairCount - 1].Key > minMaxValue)
            {
                var tractorCards = new List<Card>();
                for (int j = 0; j < pairCount; j++)
                    tractorCards.AddRange(groups[i + j].Take(2));
                results.Add(new CardPattern(PatternType.Tractor, tractorCards, groups[i + pairCount - 1].Key));
            }
        }
    }

    private static void FindBombs(IReadOnlyList<Card> cards, int minValue, List<CardPattern> results)
    {
        var groups = cards.GroupBy(c => c.FaceValue)
            .Where(g => g.Count() >= 4 && g.Key > minValue);
        foreach (var g in groups)
        {
            var bomb = g.Take(4).ToList();
            results.Add(new CardPattern(PatternType.Bomb, bomb, g.Key));
        }
    }
}
