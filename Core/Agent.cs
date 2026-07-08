using System.Collections.Generic;

namespace GuandanKitty.Core;

/// <summary>
/// 参战方抽象基类。PlayerAgent 和 EnemyAgent 的公共父类。
/// 持有 Battle 引用，支持多手牌。
/// </summary>
public abstract class Agent
{
    /// <summary>标识符</summary>
    public string Id { get; set; } = "";

    /// <summary>手牌区</summary>
    public HandZone Hand { get; } = new();

    /// <summary>所属 Battle</summary>
    public Battle Battle { get; }

    /// <summary>本回合是否已 Pass</summary>
    public bool HasPassed { get; set; }

    /// <summary>本回合是否仍在活跃</summary>
    public bool IsActive => !HasPassed;

    /// <summary>是否已战败</summary>
    public abstract bool IsDefeated { get; }

    /// <summary>
    /// 创建 Agent 实例。
    /// </summary>
    protected Agent(Battle battle, string id)
    {
        Battle = battle;
        Id = id;
    }
}

