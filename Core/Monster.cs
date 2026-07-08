using System;
using System.Collections.Generic;

namespace GuandanKitty.Core;

/// <summary>
/// 怪物数据定义 —— 敌方的被动挂载，类似玩家的遗物。
/// </summary>
public class Monster
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int Level { get; set; } = 1;

    /// <summary>Lv1 基础血量</summary>
    public int BaseHP { get; set; }

    private int _currentHP = -1;

    /// <summary>当前血量 = BaseHP × 1.1^(Level-1)</summary>
    public int CurrentHP
    {
        get
        {
            if (_currentHP < 0)
                _currentHP = (int)(BaseHP * Math.Pow(1.1, Level - 1));
            return _currentHP;
        }
    }

    /// <summary>最大血量</summary>
    public int MaxHP => (int)(BaseHP * Math.Pow(1.1, Level - 1));

    /// <summary>调整血量（正数为治疗，负数为伤害）</summary>
    public void AdjustHP(int delta)
    {
        if (_currentHP < 0) _ = CurrentHP;
        _currentHP = Math.Clamp(_currentHP + delta, 0, MaxHP);
    }

    /// <summary>牌池（抽复制品，不消耗）</summary>
    public List<Card> CardPool { get; set; } = new();

    /// <summary>每回合抽牌数（默认 1）</summary>
    public int DrawsPerRound { get; set; } = 1;

    /// <summary>战败效果（敌方赢回合时触发）</summary>
    public DefeatEffect? DefeatEffect { get; set; }

    /// <summary>
    /// 从牌池中随机抽 1 张（复制品，牌池不消耗）。
    /// </summary>
    public Card DrawFromPool(Random random)
    {
        if (CardPool.Count == 0)
            throw new InvalidOperationException($"Monster {Name} has empty card pool");
        return CardPool[random.Next(CardPool.Count)];
    }
}
