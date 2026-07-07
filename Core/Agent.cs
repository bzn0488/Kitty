using GuandanKitty;

namespace GuandanKitty.Core;

/// <summary>
/// Agent 类型
/// </summary>
public enum AgentType { Player, Enemy }

/// <summary>
/// 参战方——统一抽象。支持多手牌、怪物挂载。
/// </summary>
public class Agent
{
    public string Id { get; set; } = "";
    public AgentType Type { get; set; }
    public List<HandZone> Hands { get; } = new();
    public StandardDeck? Deck { get; set; }

    // 仅 Player
    public int MaxCallCards { get; set; } = 3;
    public int RemainingCallCards { get; set; } = 3;
    public int CardsPerCall { get; set; } = 6;

    // 仅 Enemy
    public List<Monster> Monsters { get; } = new();

    // 回合状态
    public bool HasPassed { get; set; }
    public bool IsActive => !HasPassed;

    // 便捷属性
    public bool IsPlayer => Type == AgentType.Player;
    public bool IsEnemy => Type == AgentType.Enemy;
    public bool CanCallCards => IsPlayer && RemainingCallCards > 0;
    public bool IsDefeated =>
        (Deck?.IsEmpty ?? true) && Hands.All(h => h.IsEmpty);

    // ═══════════════════════════════════════════
    //  工厂方法
    // ═══════════════════════════════════════════

    /// <summary>
    /// 从外线 Player 衍生一个玩家 Agent（内线用，深拷贝牌组）。
    /// </summary>
    public static Agent SpawnAgentFromPlayer(Player player, string id = "玩家")
    {
        var agent = new Agent
        {
            Id = id,
            Type = AgentType.Player,
            Deck = player.Deck.Clone(),
        };
        agent.Hands.Add(new HandZone());
        return agent;
    }

    /// <summary>
    /// 创建一个敌方 Agent，挂载指定怪物列表。
    /// </summary>
    public static Agent SpawnAgentFromEnemy(string id, List<Monster> monsters)
    {
        var agent = new Agent
        {
            Id = id,
            Type = AgentType.Enemy,
        };
        agent.Hands.Add(new HandZone());
        agent.Monsters.AddRange(monsters);
        return agent;
    }
}

/// <summary>
/// 怪物数据定义
/// </summary>
public class Monster
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; } = 1;

    // HP：baseHP × 1.1^(level-1)，_currentHP 跟踪战斗中变化
    public int BaseHP { get; set; }
    private int _currentHP = -1; // -1 表示未初始化
    
    public int CurrentHP
    {
        get
        {
            if (_currentHP < 0)
                _currentHP = (int)(BaseHP * Math.Pow(1.1, Level - 1));
            return _currentHP;
        }
    }
    
    public int MaxHP => (int)(BaseHP * Math.Pow(1.1, Level - 1));

    public void AdjustHP(int delta)
    {
        if (_currentHP < 0) _ = CurrentHP; // 初始化
        _currentHP = Math.Clamp(_currentHP + delta, 0, MaxHP);
    }

    // 牌池（骰子）
    public List<Card> CardPool { get; set; } = new();
    public int DrawsPerRound { get; set; } = 1;

    // 战败效果
    public DefeatEffect? DefeatEffect { get; set; }

    /// <summary>
    /// 从牌池中随机抽1张（复制品）
    /// </summary>
    public Card DrawFromPool(Random random)
    {
        if (CardPool.Count == 0)
            throw new InvalidOperationException($"Monster {Name} has empty card pool");
        return CardPool[random.Next(CardPool.Count)];
    }
}
