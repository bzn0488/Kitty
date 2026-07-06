namespace GuandanKitty.Core;

/// <summary>
/// 扑克牌花色
/// </summary>
public enum Suit { Spade, Club, Heart, Diamond }

/// <summary>
/// 扑克牌面（2=2, ..., A=14）
/// </summary>
public enum Rank
{
    Two = 2, Three, Four, Five, Six, Seven, Eight, Nine, Ten,
    Jack = 11, Queen = 12, King = 13, Ace = 14
}

/// <summary>
/// 不可变扑克牌数据
/// </summary>
public class Card
{
    public Suit Suit { get; }
    public Rank Rank { get; }
    public int FaceValue => (int)Rank;

    public Card(Suit suit, Rank rank)
    {
        Suit = suit;
        Rank = rank;
    }

    public string SuitSymbol => Suit switch
    {
        Suit.Spade => "♠",
        Suit.Club => "♣",
        Suit.Heart => "♥",
        Suit.Diamond => "♦",
        _ => "?"
    };

    public string RankSymbol => Rank switch
    {
        Rank.Two => "2", Rank.Three => "3", Rank.Four => "4", Rank.Five => "5",
        Rank.Six => "6", Rank.Seven => "7", Rank.Eight => "8", Rank.Nine => "9",
        Rank.Ten => "10", Rank.Jack => "J", Rank.Queen => "Q", Rank.King => "K",
        Rank.Ace => "A", _ => "?"
    };

    public bool IsRed => Suit == Suit.Heart || Suit == Suit.Diamond;

    public override string ToString() => $"{SuitSymbol}{RankSymbol}";

    public override bool Equals(object? obj) =>
        obj is Card c && c.Suit == Suit && c.Rank == Rank;

    public override int GetHashCode() => HashCode.Combine(Suit, Rank);
}
