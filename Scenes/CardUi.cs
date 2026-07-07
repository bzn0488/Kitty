using Godot;
using GuandanKitty.Core;

namespace GuandanKitty;

/// <summary>
/// 单张卡牌的 UI 控件。支持选中状态、数据绑定、动态卡面纹理。
/// </summary>
public partial class CardUi : Control
{
    private const string CardTexturePath = "res://Resources/Card/card{0}{1}.png";

    // 卡牌数据
    private Card? _card;
    public Card? Card => _card;

    // 选中状态
    private bool _isSelected;
    public bool IsSelected => _isSelected;

    // 子节点引用
    private TextureButton? _textureButton;
    private Label? _fallbackLabel;

    // 选中状态变更信号
    [Signal]
    public delegate void CardToggledEventHandler(CardUi cardUi, bool selected);

    public override void _Ready()
    {
        _textureButton = GetNode<TextureButton>("TextureButton");

        // 备用文字标签（纹理加载失败时显示）
        _fallbackLabel = new Label();
        _fallbackLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _fallbackLabel.VerticalAlignment = VerticalAlignment.Center;
        _fallbackLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _fallbackLabel.AddThemeFontSizeOverride("font_size", 28);
        _fallbackLabel.Hide();
        _textureButton.AddChild(_fallbackLabel);

        _textureButton.Pressed += OnPressed;
    }

    /// <summary>
    /// 绑定卡牌数据，动态加载对应纹理
    /// </summary>
    public void SetCard(Card card)
    {
        _card = card;

        // 加载卡面纹理
        var texture = LoadCardTexture(card);
        if (texture != null)
        {
            _textureButton!.TextureNormal = texture;
            _fallbackLabel?.Hide();
        }
        else
        {
            // 纹理加载失败 → 显示备用文字
            if (_fallbackLabel != null)
            {
                _fallbackLabel.Text = $"{card.SuitSymbol}{card.RankSymbol}";
                _fallbackLabel.AddThemeColorOverride("font_color",
                    card.IsRed ? new Color(0.8f, 0.2f, 0.2f) : new Color(0.0f, 0.0f, 0.0f));
                _fallbackLabel.Show();
            }
        }
    }

    /// <summary>
    /// 根据花色和牌面解析文件名，加载对应纹理
    /// </summary>
    private static Texture2D? LoadCardTexture(Card card)
    {
        var suitName = card.Suit switch
        {
            Suit.Spade   => "Spades",
            Suit.Club    => "Clubs",
            Suit.Heart   => "Hearts",
            Suit.Diamond => "Diamonds",
            _ => null
        };

        var rankName = card.Rank switch
        {
            Rank.Two   => "2",  Rank.Three => "3",  Rank.Four  => "4",
            Rank.Five  => "5",  Rank.Six   => "6",  Rank.Seven => "7",
            Rank.Eight => "8",  Rank.Nine  => "9",  Rank.Ten   => "10",
            Rank.Jack  => "J",  Rank.Queen => "Q",  Rank.King  => "K",
            Rank.Ace   => "A",
            _ => null
        };

        if (suitName == null || rankName == null) return null;

        var path = string.Format(CardTexturePath, suitName, rankName);
        return ResourceLoader.Load<Texture2D>(path);
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
