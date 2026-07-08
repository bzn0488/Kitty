using Godot;
using GuandanKitty.Core;

namespace GuandanKitty;

/// <summary>
/// Run 管理器 —— 外线系统的根实体，Battle 的上级。
/// 全局单例（Autoload），管理 Player 数据、创建/驱动/销毁 Battle。
/// </summary>
public partial class Run : Node
{
    /// <summary>静态实例引用（Autoload 自动赋值）</summary>
    public static Run Instance { get; private set; } = null!;

    /// <summary>玩家数据（贯穿一次 Run）</summary>
    public Player PlayerData { get; private set; } = null!;

    /// <summary>当前战斗实例</summary>
    public Battle? CurrentBattle { get; private set; }

    /// <summary>BattleUI 引用（由主场景在启动战斗前设置）</summary>
    private BattleUI? _battleUI;

    public override void _Ready()
    {
        Instance = this;
    }

    /// <summary>
    /// 初始化 Run：创建玩家、重置 Run 级数据。
    /// </summary>
    public void Initialize()
    {
        PlayerData = new Player();
        PlayerData.Initialize();
    }

    /// <summary>
    /// 设置 BattleUI 引用（由主场景在 _Ready 时调用）。
    /// </summary>
    public void SetBattleUI(BattleUI ui)
    {
        _battleUI = ui;
    }

    /// <summary>
    /// 开始战斗：创建 Battle、初始化、开始驱动。
    /// </summary>
    public void StartBattle()
    {
        if (PlayerData == null || _battleUI == null) return;

        CurrentBattle = new Battle(PlayerData.Deck, _battleUI);
        CurrentBattle.Initialize();
    }

    /// <summary>
    /// 每帧驱动当前 Battle。
    /// </summary>
    public override void _Process(double delta)
    {
        CurrentBattle?.Update((float)delta);
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
