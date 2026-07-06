namespace GuandanKitty.Core;

/// <summary>
/// 牌型类型
/// </summary>
public enum PatternType
{
    Single,     // 单张
    Pair,       // 一对
    Triple,     // 三张
    Straight,   // 顺子（≥5张连续）
    Tractor,    // 联队（≥3对连续）
    Bomb        // 炸弹（≥4张同数字）
}

/// <summary>
/// 牌型检测结果
/// </summary>
public class CardPattern
{
    public PatternType Type { get; }
    public List<Card> Cards { get; }
    public int CardCount => Cards.Count;
    public int CompareValue { get; }  // 同牌型比较值

    public CardPattern(PatternType type, List<Card> cards, int compareValue)
    {
        Type = type;
        Cards = cards;
        CompareValue = compareValue;
    }

    public override string ToString() =>
        $"{Type}({string.Join(" ", Cards)}) CV={CompareValue}";
}
