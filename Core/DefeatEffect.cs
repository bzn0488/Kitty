namespace GuandanKitty.Core;

/// <summary>
/// 战败效果基类
/// </summary>
public abstract class DefeatEffect
{
    public abstract string Description { get; }

    /// <summary>
    /// 从手牌中选择要移出的牌
    /// </summary>
    public abstract List<Card> SelectCardsToRemove(HandZone playerHand);
}

/// <summary>
/// 随机弃N张
/// </summary>
public class RandomDiscardEffect : DefeatEffect
{
    public int Count { get; set; } = 3;
    public override string Description => $"随机弃 {Count} 张手牌";

    public override List<Card> SelectCardsToRemove(HandZone playerHand)
    {
        var rng = new Random();
        return playerHand.Cards.OrderBy(_ => rng.Next()).Take(Count).ToList();
    }
}

/// <summary>
/// 弃掉所有 ≤N 的牌
/// </summary>
public class DiscardBelowEffect : DefeatEffect
{
    public int Threshold { get; set; } = 7;
    public override string Description => $"弃掉所有 ≤{Threshold} 的手牌";

    public override List<Card> SelectCardsToRemove(HandZone playerHand)
        => playerHand.Cards.Where(c => c.FaceValue <= Threshold).ToList();
}

/// <summary>
/// 弃掉所有 ≥N 的牌
/// </summary>
public class DiscardAboveEffect : DefeatEffect
{
    public int Threshold { get; set; } = 10;
    public override string Description => $"弃掉所有 ≥{Threshold} 的手牌";

    public override List<Card> SelectCardsToRemove(HandZone playerHand)
        => playerHand.Cards.Where(c => c.FaceValue >= Threshold).ToList();
}
