using System.Collections.Generic;

namespace GuandanKitty.Core;

/// <summary>
/// 参战方抽象基类。PlayerAgent 和 EnemyAgent 的公共父类。
/// 持有 Battle 引用，支持多手牌。
/// </summary>
public abstract class Agent
{
    public string Id { get; set; } = "";
    public HandZone Hand { get; } = new();
    public Battle Battle { get; }
    public bool HasPassed { get; set; }
    public bool IsActive => !HasPassed;
    public abstract bool IsDefeated { get; }

    protected Agent(Battle battle, string id)
    {
        Battle = battle;
        Id = id;
    }

    // ═══════════════════════════════════════════
    //  行动方法
    // ═══════════════════════════════════════════

    /// <summary>
    /// 尝试出牌。验证牌型+压制 → 成功则扣手牌、写入牌河/Chain → null。
    /// 失败返回错误信息。
    /// </summary>
    public string? TryPlayCards(List<Card> cards)
    {
        var pattern = CardPatternDetector.Detect(cards);
        if (pattern == null) return "不是合法牌型";

        if (!CanSuppressCurrent(pattern))
            return "无法压制上一手牌";

        Hand.Remove(cards);
        Battle.River.Add(pattern, this);
        Battle.Chain.RecordPlay(pattern, this);
        return null;
    }

    /// <summary>
    /// 检查能否压制当前 Chain.LastPlayed。
    /// </summary>
    public bool CanSuppressCurrent(CardPattern pattern)
    {
        if (Battle.Chain.LastPlayed == null) return true;
        return SuppressionJudge.CanSuppress(pattern, Battle.Chain.LastPlayed);
    }

    /// <summary>
    /// Pass：从活跃集合移除。
    /// </summary>
    public void TryPass()
    {
        HasPassed = true;
        Battle.ActiveAgents.Remove(this);
        Battle.Chain.RecordPass(this);
    }
}

