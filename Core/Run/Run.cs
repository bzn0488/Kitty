using Godot;
using GuandanKitty.Core;

namespace GuandanKitty;

/// <summary>
/// Run 管理器 —— 外线系统的根实体，Battle 的上级。
/// 全局单例（Autoload），管理 Player 数据、启动 Battle。
/// </summary>
public partial class Run : Node
{
    /// <summary>静态实例引用（Autoload 自动赋值）</summary>
    public static Run Instance { get; private set; } = null!;

    /// <summary>玩家数据（贯穿一次 Run）</summary>
    public Player PlayerData { get; private set; } = null!;

    /// <summary>当前战斗实例</summary>
    public Battle? CurrentBattle { get; private set; }

    public override void _Ready()
    {
        Instance = this;
    }

    /// <summary>
    /// 初始化 Run：创建玩家、重置 Run 级数据。
    /// 在每次开始新 Run 时调用。
    /// </summary>
    public void Initialize()
    {
        PlayerData = new Player();
        PlayerData.Initialize();
    }

    /// <summary>
    /// 开始战斗：加载战斗场景。
    /// </summary>
    public void StartBattle()
    {
        GetTree().ChangeSceneToFile("res://Scenes/BattleScene.tscn");
    }

    /// <summary>
    /// 结束战斗并清理引用。
    /// </summary>
    public void EndBattle()
    {
        CurrentBattle = null;
    }
}
