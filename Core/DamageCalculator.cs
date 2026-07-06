namespace GuandanKitty.Core;

/// <summary>
/// 伤害计算上下文
/// </summary>
public class DamageContext
{
    public CardPattern Pattern { get; init; } = null!;
    public int DepthMultiplier { get; init; }
    public bool IsWinningHand { get; init; }
    public bool IsClearHand { get; init; }
}

/// <summary>
/// 伤害计算器
/// 公式：牌面值之和 × 出牌张数 × 接龙深度 × (赢回合?2:1) × (清空?10:1)
/// </summary>
public static class DamageCalculator
{
    public static int Calculate(CardPattern pattern, int depthMultiplier,
        bool isWinningHand, bool isClearHand)
    {
        int faceSum = pattern.Cards.Sum(c => c.FaceValue);
        int cardCount = pattern.CardCount;

        int damage = faceSum * cardCount * depthMultiplier;

        if (isClearHand)
            damage *= 10;
        else if (isWinningHand)
            damage *= 2;

        return damage;
    }
}
