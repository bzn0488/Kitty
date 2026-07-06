namespace GuandanKitty.Core;

/// <summary>
/// 怪物数据库（占位）
/// </summary>
public static class MonsterDatabase
{
    public static Monster CreateTestMonster()
    {
        return new Monster
        {
            Id = "test_dummy",
            Name = "训练假人",
            Level = 1,
            BaseHP = 100,
            DrawsPerRound = 1,
            CardPool = new List<Card>
            {
                new(Suit.Spade, Rank.Three),
                new(Suit.Heart, Rank.Four),
                new(Suit.Club, Rank.Five),
                new(Suit.Diamond, Rank.Six),
                new(Suit.Spade, Rank.Seven),
            },
            DefeatEffect = new RandomDiscardEffect { Count = 2 }
        };
    }
}
