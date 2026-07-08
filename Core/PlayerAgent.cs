using System.Collections.Generic;
using System.Linq;

namespace GuandanKitty.Core;

/// <summary>
/// 玩家 Agent —— 持有牌堆，可叫牌，从牌堆抽牌。
/// </summary>
public class PlayerAgent : Agent
{
    /// <summary>玩家牌堆</summary>
    public StandardDeck Deck { get; }

    /// <summary>每场战斗最大叫牌次数</summary>
    public int MaxCallCards { get; set; } = 3;

    /// <summary>剩余叫牌次数</summary>
    public int RemainingCallCards { get; set; } = 3;

    /// <summary>每次叫牌抽几张</summary>
    public int CardsPerCall { get; set; } = 6;

    /// <summary>是否还能叫牌</summary>
    public bool CanCallCards => RemainingCallCards > 0;

    /// <summary>
    /// 牌堆为空且所有手牌区为空时战败。
    /// </summary>
    public override bool IsDefeated =>
        Deck.IsEmpty && Hands.All(h => h.IsEmpty);

    /// <summary>
    /// 从 Run 的 Player 数据创建玩家 Agent（深拷贝牌组）。
    /// </summary>
    public PlayerAgent(Battle battle, StandardDeck deck, string id = "玩家")
        : base(battle, id)
    {
        Deck = deck.Clone();
        Hands.Add(new HandZone());
    }

    /// <summary>
    /// 从牌堆抽 n 张牌。
    /// </summary>
    public List<Card> DrawFromDeck(int n)
    {
        return Deck.Draw(n);
    }

    /// <summary>
    /// 叫牌：抽 CardsPerCall 张，扣除一次叫牌次数。
    /// 不可叫牌时返回空列表。
    /// </summary>
    public List<Card> CallCards()
    {
        if (!CanCallCards) return new List<Card>();
        RemainingCallCards--;
        return Deck.Draw(CardsPerCall);
    }

    /// <summary>
    /// 重置叫牌次数到最大值。
    /// </summary>
    public void ResetCallCounts()
    {
        RemainingCallCards = MaxCallCards;
    }
}
