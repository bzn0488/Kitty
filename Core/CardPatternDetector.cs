namespace GuandanKitty.Core;

/// <summary>
/// 牌型检测器——判断一组牌是否构成合法牌型
/// </summary>
public static class CardPatternDetector
{
    /// <summary>
    /// 检测牌型。不合法返回 null。
    /// </summary>
    public static CardPattern? Detect(List<Card> cards)
    {
        if (cards == null || cards.Count == 0) return null;

        // 按牌面值排序
        var sorted = cards.OrderBy(c => c.FaceValue).ToList();

        // 检测优先级：炸弹 → 联队 → 顺子 → 三张 → 一对 → 单张
        if (TryDetectBomb(sorted, out var bomb)) return bomb;
        if (TryDetectTractor(sorted, out var tractor)) return tractor;
        if (TryDetectStraight(sorted, out var straight)) return straight;
        if (TryDetectTriple(sorted, out var triple)) return triple;
        if (TryDetectPair(sorted, out var pair)) return pair;
        if (TryDetectSingle(sorted, out var single)) return single;

        return null;
    }

    private static bool TryDetectSingle(List<Card> cards, out CardPattern? pattern)
    {
        if (cards.Count == 1)
        {
            pattern = new CardPattern(PatternType.Single, cards, cards[0].FaceValue);
            return true;
        }
        pattern = null;
        return false;
    }

    private static bool TryDetectPair(List<Card> cards, out CardPattern? pattern)
    {
        if (cards.Count == 2 && cards[0].FaceValue == cards[1].FaceValue)
        {
            pattern = new CardPattern(PatternType.Pair, cards, cards[0].FaceValue);
            return true;
        }
        pattern = null;
        return false;
    }

    private static bool TryDetectTriple(List<Card> cards, out CardPattern? pattern)
    {
        if (cards.Count == 3 &&
            cards[0].FaceValue == cards[1].FaceValue &&
            cards[1].FaceValue == cards[2].FaceValue)
        {
            pattern = new CardPattern(PatternType.Triple, cards, cards[0].FaceValue);
            return true;
        }
        pattern = null;
        return false;
    }

    private static bool TryDetectStraight(List<Card> cards, out CardPattern? pattern)
    {
        if (cards.Count < 5)
        {
            pattern = null;
            return false;
        }

        // 检查是否连续
        for (int i = 1; i < cards.Count; i++)
        {
            if (cards[i].FaceValue != cards[i - 1].FaceValue + 1)
            {
                pattern = null;
                return false;
            }
        }

        pattern = new CardPattern(PatternType.Straight, cards, cards[^1].FaceValue);
        return true;
    }

    private static bool TryDetectTractor(List<Card> cards, out CardPattern? pattern)
    {
        // 联队：≥3对连续，且每对花色不能完全相同
        if (cards.Count < 6 || cards.Count % 2 != 0)
        {
            pattern = null;
            return false;
        }

        int pairCount = cards.Count / 2;
        if (pairCount < 3)
        {
            pattern = null;
            return false;
        }

        // 按牌面值分组
        var groups = cards.GroupBy(c => c.FaceValue).OrderBy(g => g.Key).ToList();

        // 每组必须恰好2张
        if (groups.Any(g => g.Count() != 2))
        {
            pattern = null;
            return false;
        }

        // 组间必须连续
        for (int i = 1; i < groups.Count; i++)
        {
            if (groups[i].Key != groups[i - 1].Key + 1)
            {
                pattern = null;
                return false;
            }
        }

        pattern = new CardPattern(PatternType.Tractor, cards, groups[^1].Key);
        return true;
    }

    private static bool TryDetectBomb(List<Card> cards, out CardPattern? pattern)
    {
        if (cards.Count < 4)
        {
            pattern = null;
            return false;
        }

        // 所有牌必须同数字
        int value = cards[0].FaceValue;
        if (cards.All(c => c.FaceValue == value))
        {
            pattern = new CardPattern(PatternType.Bomb, cards, value);
            return true;
        }

        pattern = null;
        return false;
    }
}
