namespace GuandanKitty.Core;

/// <summary>
/// 牌河条目
/// </summary>
public struct RiverEntry
{
    public List<Card> Cards;
    public Agent Agent;
    public CardPattern Pattern;
}

/// <summary>
/// 牌河——本回合双方打出的牌临时存放区
/// </summary>
public class CardRiver
{
    private readonly List<RiverEntry> _entries = new();

    public IReadOnlyList<RiverEntry> Entries => _entries.AsReadOnly();

    public void Add(CardPattern pattern, Agent agent)
    {
        _entries.Add(new RiverEntry
        {
            Cards = new List<Card>(pattern.Cards),
            Agent = agent,
            Pattern = pattern
        });
    }

    /// <summary>
    /// 牌河中所有牌
    /// </summary>
    public List<Card> GetAllCards()
    {
        var all = new List<Card>();
        foreach (var entry in _entries)
            all.AddRange(entry.Cards);
        return all;
    }

    public void Clear() => _entries.Clear();
}
