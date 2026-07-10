using Godot;

namespace GuandanKitty;

/// <summary>
/// UI 根节点 —— 管理所有界面的加载与切换。
/// 在当前节点下创建或销毁子场景（MainScene、BattleScene 等）。
/// </summary>
public partial class UIRoot : Control
{
    /// <summary>单例引用</summary>
    public static UIRoot Instance { get; private set; } = null!;

    public override void _Ready()
    {
        Instance = this;
        LoadMainScene();
    }

    /// <summary>
    /// 加载主菜单场景。
    /// </summary>
    public void LoadMainScene()
    {
        ClearChildren();
        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/MainScene.tscn");
        AddChild(scene.Instantiate());
    }

    /// <summary>
    /// 加载战斗场景，返回 BattleUI 引用。
    /// </summary>
    public BattleUI LoadBattleScene()
    {
        ClearChildren();
        var scene = ResourceLoader.Load<PackedScene>("res://Scenes/BattleScene.tscn");
        var battleUI = scene.Instantiate<BattleUI>();
        AddChild(battleUI);
        return battleUI;
    }

    /// <summary>
    /// 清除所有子节点。
    /// </summary>
    private void ClearChildren()
    {
        foreach (var child in GetChildren())
            child.QueueFree();
    }

    public override void _Process(double delta)
    {
        Run.Instance.Update((float)delta);
    }
}
