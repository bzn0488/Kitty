using Godot;
using GuandanKitty.Core;
using System.Collections.Generic;

namespace GuandanKitty;

/// <summary>
/// 玩家 —— 贯穿一次 Run 的实体。
/// 管理卡组、金币等 Run 级数据，不属于战斗系统。
/// 战斗时由 Battle 根据 Player 的数据创建对应的 Player Agent。
/// </summary>
public class Player
{
    /// <summary>玩家牌组（52 张标准扑克）</summary>
    public StandardDeck Deck { get; } = new();

    /// <summary>当前金币</summary>
    public int Gold { get; set; }

    /// <summary>已收集的遗物列表</summary>
    public List<Relic> Relics { get; } = new();

    /// <summary>已获得的贴纸列表</summary>
    public List<Sticker> Stickers { get; } = new();

    /// <summary>
    /// 初始化玩家：创建并洗牌牌组。
    /// </summary>
    public void Initialize()
    {
        Deck.Initialize();
        Gold = 0;
        Relics.Clear();
        Stickers.Clear();
    }
}

/// <summary>
/// 遗物（占位数据结构，待展开）。
/// </summary>
public class Relic
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}

/// <summary>
/// 贴纸（占位数据结构，待展开）。
/// </summary>
public class Sticker
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
}
