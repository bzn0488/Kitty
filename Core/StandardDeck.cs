namespace GuandanKitty.Core;

/// <summary>
/// 52张标准扑克牌堆（无大小王）
/// </summary>
public class StandardDeck
{
    private readonly List<Card> _cards = new();
    private readonly Random _random = new();

    public int Count => _cards.Count;
    public bool IsEmpty => _cards.Count == 0;

    /// <summary>
    /// 创建52张牌并洗牌
    /// </summary>
    public void Initialize()
    {
        _cards.Clear();
        foreach (Suit suit in Enum.GetValues<Suit>())
        foreach (Rank rank in Enum.GetValues<Rank>())
            _cards.Add(new Card(suit, rank));
        Shuffle();
    }

    public void Shuffle()
    {
        for (int i = _cards.Count - 1; i > 0; i--)
        {
            int j = _random.Next(i + 1);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }
    }

    /// <summary>
    /// 从顶部抽1张牌。牌堆为空时返回 null。
    /// </summary>
    public Card? Draw()
    {
        if (_cards.Count == 0) return null;
        var card = _cards[0];
        _cards.RemoveAt(0);
        return card;
    }

    /// <summary>
    /// 从顶部抽n张牌。不足时返回实际能抽到的数量。
    /// </summary>
    public List<Card> Draw(int n)
    {
        var drawn = new List<Card>();
        for (int i = 0; i < n; i++)
        {
            var card = Draw();
            if (card != null) drawn.Add(card);
            else break;
        }
        return drawn;
    }

    /// <summary>
    /// 将牌洗回牌堆（不改变已有顺序）
    /// </summary>
    public void ShuffleBack(List<Card> cards)
    {
        _cards.AddRange(cards);
        Shuffle();
    }

    /// <summary>
    /// 永久移出牌（战败效果用）
    /// </summary>
    public void RemovePermanently(List<Card> cards)
    {
        foreach (var card in cards)
            _cards.Remove(card);
    }
}
