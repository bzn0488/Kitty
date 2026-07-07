using Godot;

namespace GuandanKitty;

public partial class MainScene : Control
{
    private Button? _startButton;
    private Label? _titleLabel;

    public override void _Ready()
    {
        // 标题
        _titleLabel = new Label
        {
            Text = "GuandanKitty",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _titleLabel.AddThemeFontSizeOverride("font_size", 48);
        _titleLabel.SetPosition(new Vector2(960, 300));
        _titleLabel.SetSize(new Vector2(400, 80));
        AddChild(_titleLabel);

        // 副标题
        var subtitle = new Label
        {
            Text = "类斗地主 Roguelike 纸牌对战",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        subtitle.AddThemeFontSizeOverride("font_size", 18);
        subtitle.SetPosition(new Vector2(960, 370));
        subtitle.SetSize(new Vector2(400, 40));
        AddChild(subtitle);

        // 开始按钮
        _startButton = new Button
        {
            Text = "开始游戏",
        };
        _startButton.SetPosition(new Vector2(960, 450));
        _startButton.SetSize(new Vector2(200, 60));
        _startButton.Pressed += OnStartPressed;
        AddChild(_startButton);

        // 居中
        CenterControls();
    }

    private void CenterControls()
    {
        // 将控件的 pivot 设为居中
        if (_titleLabel != null)
            _titleLabel.Position = new Vector2(
                (1920 - _titleLabel.Size.X) / 2,
                _titleLabel.Position.Y);
        if (_startButton != null)
            _startButton.Position = new Vector2(
                (1920 - _startButton.Size.X) / 2,
                _startButton.Position.Y);
    }

    private void OnStartPressed()
    {
        // 初始化 Run 并启动战斗
        Run.Instance.Initialize();
        Run.Instance.StartBattle();
    }
}
