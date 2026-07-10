using Godot;

namespace GuandanKitty;

/// <summary>
/// 主菜单场景 —— UI 在 .tscn 中设计，脚本只负责按钮事件。
/// </summary>
public partial class MainScene : Control
{
    public override void _Ready()
    {
        GetNode<Button>("Button").Pressed += OnStartPressed;
    }

    private void OnStartPressed()
    {
        Run.Instance.Initialize();
    }
}

