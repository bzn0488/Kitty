namespace GuandanKitty.Core;

/// <summary>
/// 敌方 AI——简单规则：用最小合法牌型压制
/// </summary>
public static class EnemyAI
{
    private static readonly Random _random = new();

    /// <summary>
    /// 在所有手牌区中寻找能压制 target 的牌型。
    /// 返回 (handIndex, pattern) 或 null（Pass）。
    /// 策略：选 CompareValue 最小的合法牌型（若同 CompareValue 则选 CardCount 小的）。
    /// </summary>
    public static (int handIndex, CardPattern pattern)? FindBestPlay(
        List<HandZone> hands, CardPattern target)
    {
        CardPattern? bestPattern = null;
        int bestHandIdx = 0;

        for (int h = 0; h < hands.Count; h++)
        {
            var candidates = FindAllValidPlays(hands[h], target);
            foreach (var p in candidates)
            {
                if (bestPattern == null ||
                    p.CompareValue < bestPattern.CompareValue ||
                    (p.CompareValue == bestPattern.CompareValue && p.CardCount < bestPattern.CardCount))
                {
                    bestPattern = p;
                    bestHandIdx = h;
                }
            }
        }

        if (bestPattern == null) return null;
        return (bestHandIdx, bestPattern);
    }

    /// <summary>
    /// 在手牌中暴力搜索所有能压制 target 的合法牌型
    /// </summary>
    private static List<CardPattern> FindAllValidPlays(HandZone hand, CardPattern target)
    {
        var results = new List<CardPattern>();
        var cards = hand.Cards;

        if (target.Type == PatternType.Bomb)
        {
            // 目标已是炸弹 → 必须找更大的炸弹
            FindBombs(cards, target.CompareValue, results);
        }
        else
        {
            // 同牌型搜索
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
            // 另外炸弹总是可以跨牌型压制
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
