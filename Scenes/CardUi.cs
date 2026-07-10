using Godot;
using GuandanKitty.Core;

namespace GuandanKitty;

/// <summary>
/// 单张卡牌的 UI 控件。由 BattleUI 创建、设置数据、注册事件。
/// 不含 _Ready，所有初始化由 SetCard 完成。
/// </summary>
public partial class CardUi : Control
{
    private const string CardTexturePath = "res://Resources/Card/card{0}{1}.png";

    private Card? _card;
    private TextureButton? _textureButton;
    private Label? _fallbackLabel;
    private bool _isSelected;

    public Card? Card => _card;
    public bool IsSelected => _isSelected;

    [Signal]
    public delegate void CardToggledEventHandler(CardUi cardUi, bool selected);

    /// <summary>
    /// 绑定卡牌数据并初始化子控件。由 BattleUI 在 AddChild 之后调用。
    /// </summary>
    public void SetCard(Card card)
    {
        _card = card;

        _textureButton = GetNode<TextureButton>("TextureButton");

        _fallbackLabel = new Label();
        _fallbackLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _fallbackLabel.VerticalAlignment = VerticalAlignment.Center;
        _fallbackLabel.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        _fallbackLabel.AddThemeFontSizeOverride("font_size", 28);
        _fallbackLabel.Hide();
        _textureButton.AddChild(_fallbackLabel);

        _textureButton.Pressed += OnPressed;

        ApplyCardTexture(card);
    }

    /// <summary>
    /// 加载并应用卡面纹理，失败则显示文字。
    /// </summary>
    private void ApplyCardTexture(Card card)
    {
        var texture = LoadCardTexture(card);
        if (texture != null)
        {
            _textureButton!.TextureNormal = texture;
            _fallbackLabel?.Hide();
        }
        else
        {
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
    /// 设置选中状态。
    /// </summary>
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        if (_textureButton != null)
        {
            _textureButton.Modulate = selected
                ? new Color(1.0f, 0.9f, 0.3f)
                : Colors.White;
        }
    }

    private void OnPressed()
    {
        SetSelected(!_isSelected);
        EmitSignal(SignalName.CardToggled, this, _isSelected);
    }

    /// <summary>
    /// 根据花色和牌面加载对应纹理。
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
}

