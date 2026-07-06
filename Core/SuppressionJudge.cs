namespace GuandanKitty.Core;

/// <summary>
/// 压制判定器——判断一手牌能否压制另一手牌
/// </summary>
public static class SuppressionJudge
{
    /// <summary>
    /// 判断 hand 是否能压制 target。
    /// 规则：炸弹可跨牌型压制任何牌；同牌型需 CompareValue 更大。
    /// </summary>
    public static bool CanSuppress(CardPattern? hand, CardPattern? target)
    {
        if (hand == null || target == null) return false;

        // 炸弹可压制任何牌型（含更大的炸弹）
        if (hand.Type == PatternType.Bomb)
        {
            if (target.Type == PatternType.Bomb)
                return hand.CompareValue > target.CompareValue;
            return true;
        }

        // 非炸弹：必须同牌型 + 更大数值
        if (hand.Type != target.Type) return false;
        return hand.CompareValue > target.CompareValue;
    }
}
