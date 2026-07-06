namespace GuandanKitty.Core;

/// <summary>
/// 接龙追踪器——记录本回合出牌序列和深度
/// </summary>
public class ChainTracker
{
    public int PlayerHandCount { get; private set; }     // 本回合玩家第几次出牌
    public CardPattern? LastPlayed { get; private set; } // 上一手出的牌型
    public Agent? LastPlayedBy { get; private set; }     // 上一手是哪个 Agent
    public List<Agent> PassedAgents { get; } = new();    // 本回合已 Pass 的 Agent

    /// <summary>
    /// 接龙深度倍率 = 2^(玩家出牌次数-1)
    /// </summary>
    public int DepthMultiplier => PlayerHandCount > 0 ? 1 << (PlayerHandCount - 1) : 1;

    public void RecordPlay(CardPattern pattern, Agent agent)
    {
        LastPlayed = pattern;
        LastPlayedBy = agent;
        if (agent.Type == AgentType.Player)
            PlayerHandCount++;
    }

    public void RecordPass(Agent agent)
    {
        if (!PassedAgents.Contains(agent))
            PassedAgents.Add(agent);
    }

    public void Reset()
    {
        PlayerHandCount = 0;
        LastPlayed = null;
        LastPlayedBy = null;
        PassedAgents.Clear();
    }
}
