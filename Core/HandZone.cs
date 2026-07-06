namespace GuandanKitty.Core;

/// <summary>
/// 手牌区——管理手牌的添加、移除、查询
/// </summary>
public class HandZone
{
    private readonly List<Card> _cards = new();

    public int Count => _cards.Count;
    public bool IsEmpty => _cards.Count == 0;
    public IReadOnlyList<Card> Cards => _cards.AsReadOnly();

    public void Add(Card card) => _cards.Add(card);

    public void AddRange(IEnumerable<Card> cards) => _cards.AddRange(cards);

    /// <summary>
    /// 移除指定牌，返回成功移除的数量
    /// </summary>
    public int Remove(List<Card> cards)
    {
        int removed = 0;
        foreach (var card in cards)
        {
            if (_cards.Remove(card))
                removed++;
        }
        return removed;
    }

    /// <summary>
    /// 清空手牌，返回所有牌
    /// </summary>
    public List<Card> RemoveAll()
    {
        var all = new List<Card>(_cards);
        _cards.Clear();
        return all;
    }

    public bool Contains(Card card) => _cards.Contains(card);

    public List<Card> FindAll(Func<Card, bool> predicate) =>
        _cards.Where(predicate).ToList();

    public void SortByRank()
    {
        _cards.Sort((a, b) => a.FaceValue.CompareTo(b.FaceValue));
    }
}
