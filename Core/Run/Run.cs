using GuandanKitty.Core;

namespace GuandanKitty;

/// <summary>
/// Run 管理器 —— 外线系统的根实体，纯 C# 类，不继承 Node。
/// 管理 Player 数据，创建/驱动/销毁 Battle。
/// </summary>
public class Run
{
    /// <summary>全局单例</summary>
    public static Run Instance { get; } = new Run();

    /// <summary>玩家数据（贯穿一次 Run）</summary>
    public Player PlayerData { get; private set; } = null!;

    /// <summary>当前战斗实例</summary>
    public Battle? CurrentBattle { get; private set; }

    private Run() { }

    /// <summary>
    /// 初始化 Run：创建玩家、重置 Run 级数据。
    /// </summary>
    public void Initialize()
    {
        PlayerData = new Player();
        PlayerData.Initialize();
    }

    /// <summary>
    /// 启动 Battle：创建 Battle 实例，初始化 FSM 和 UI。
    /// </summary>
    public void StartBattle(BattleUI ui)
    {
        if (PlayerData == null) return;

        CurrentBattle = new Battle(PlayerData.Deck, ui);
        CurrentBattle.Initialize();
    }

    /// <summary>
    /// 每帧更新，由场景根节点的 _Process 驱动。
    /// </summary>
    public void Update(float delta)
    {
        CurrentBattle?.Update(delta);
    }

    /// <summary>
    /// 结束战斗：调用 Battle.End() 并清理引用。
    /// </summary>
    public void EndBattle()
    {
        CurrentBattle?.End();
        CurrentBattle = null;
    }
}

