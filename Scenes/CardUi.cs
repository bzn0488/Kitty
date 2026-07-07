using Godot;
using GuandanKitty.Core;

namespace GuandanKitty;

/// <summary>
/// 单张卡牌的 UI 控件。支持选中状态、数据绑定。
/// </summary>
public partial class CardUi : Control
{
    // 卡牌数据
    private Card? _card;
    public Card? Card => _card;

    // 选中状态
    private bool _isSelected;
    public bool IsSelected => _isSelected;

    // 子节点引用
    private TextureButton? _textureButton;
    private Label? _label;

    // 选中状态变更信号
    [Signal]
    public delegate void CardToggledEventHandler(CardUi cardUi, bool selected);

    public override void _Ready()
    {
        _textureButton = GetNode<TextureButton>("TextureButton");

        // 创建文字标签覆盖在卡面上
        _label = new Label();
        _label.HorizontalAlignment = HorizontalAlignment.Center;
        _label.VerticalAlignment = VerticalAlignment.Center;
        _label.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _label.AddThemeFontSizeOverride("font_size", 28);
        _textureButton.AddChild(_label);

        _textureButton.Pressed += OnPressed;
    }

    /// <summary>
    /// 绑定卡牌数据
    /// </summary>
    public void SetCard(Card card)
    {
        _card = card;
        if (_label != null)
        {
            _label.Text = $"{card.SuitSymbol}{card.RankSymbol}";
            _label.AddThemeColorOverride("font_color",
                card.IsRed ? new Color(0.8f, 0.2f, 0.2f) : new Color(0.0f, 0.0f, 0.0f));
        }
    }

    /// <summary>
    /// 设置选中状态
    /// </summary>
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        if (_textureButton != null)
        {
            _textureButton.Modulate = selected
                ? new Color(1.0f, 0.9f, 0.3f)  // 黄色高亮
                : Colors.White;
        }
    }

    private void OnPressed()
    {
        SetSelected(!_isSelected);
        EmitSignal(SignalName.CardToggled, this, _isSelected);
    }
}
