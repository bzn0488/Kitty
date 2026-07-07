using Godot;
using GuandanKitty.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GuandanKitty;

/// <summary>
/// 战斗 UI —— 纯显示层，方法由 Battle 直接调用。
/// </summary>
public partial class BattleUI : Control
{
    private Battle? _battle;
    private Agent? _playerAgent;

    private Label? _statusLabel;
    private Label? _enemyHP;
    private Label? _playerDeckCount;
    private Label? _callCardCount;
    private Label? _chainDepth;
    private Label? _enemyHandLabel;
    private Label? _riverLabel;

    private HBoxContainer? _playerHand;
    private Button? _playBtn;
    private Button? _callBtn;
    private Button? _passBtn;

    private readonly List<CardUi> _cardUiList = new();
    private readonly HashSet<Card> _selectedCards = new();
    private PackedScene? _cardUiScene;

    public override void _Ready()
    {
        _cardUiScene = ResourceLoader.Load<PackedScene>("res://Scenes/CardUI.tscn");
        _playerHand = GetNode<HBoxContainer>("PlayerHand");
        BuildUI();
        InitBattle();
    }

    private void BuildUI()
    {
        var topBar = new HBoxContainer();
        topBar.SetPosition(new Vector2(20, 10));
        AddChild(topBar);

        _statusLabel = MakeLabel("准备中...", 18);
        topBar.AddChild(_statusLabel);
        topBar.AddChild(MakeSpacer(40));
        _enemyHP = MakeLabel("", 16);
        topBar.AddChild(_enemyHP);
        topBar.AddChild(MakeSpacer(40));
        _chainDepth = MakeLabel("", 14);
        topBar.AddChild(_chainDepth);

        _enemyHandLabel = MakeLabel("[敌方手牌]", 14);
        _enemyHandLabel.SetPosition(new Vector2(20, 120));
        AddChild(_enemyHandLabel);

        _riverLabel = MakeLabel("[牌河]", 14);
        _riverLabel.SetPosition(new Vector2(20, 300));
        _riverLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _riverLabel.SetSize(new Vector2(1240, 30));
        AddChild(_riverLabel);

        var playerStatus = new HBoxContainer();
        playerStatus.SetPosition(new Vector2(20, 440));
        AddChild(playerStatus);
        _playerDeckCount = MakeLabel("", 14);
        playerStatus.AddChild(_playerDeckCount);
        playerStatus.AddChild(MakeSpacer(20));
        _callCardCount = MakeLabel("", 14);
        playerStatus.AddChild(_callCardCount);

        var actionButtons = new HBoxContainer();
        actionButtons.SetPosition(new Vector2(20, 740));
        AddChild(actionButtons);

        _playBtn = new Button { Text = "▶ 出牌", Disabled = true };
        _playBtn.SetSize(new Vector2(160, 50));
        _playBtn.Pressed += OnPlayPressed;
        actionButtons.AddChild(_playBtn);

        _callBtn = new Button { Text = "📞 叫牌 (+6)", Disabled = true };
        _callBtn.SetSize(new Vector2(160, 50));
        _callBtn.Pressed += OnCallPressed;
        actionButtons.AddChild(_callBtn);

        _passBtn = new Button { Text = "跳过", Disabled = true };
        _passBtn.SetSize(new Vector2(120, 50));
        _passBtn.Pressed += OnPassPressed;
        actionButtons.AddChild(_passBtn);
    }

    private void InitBattle()
    {
        // Battle 节点已在场景中预置，由它自己启动并读取 Run 的数据
        _battle = GetNode<Battle>("Battle");
        _playerAgent = _battle?.PlayerAgent;
    }

    // ═══════════════════════════════════════════
    //  以下方法由 Battle 直接调用
    // ═══════════════════════════════════════════

    /// <summary>摸牌动画：飞牌，播完后回调</summary>
    public void OnCardDrawRequested(Card card, Action onComplete)
    {
        if (_playerHand == null || _cardUiScene == null)
        {
            onComplete();
            return;
        }

        var cardUi = _cardUiScene.Instantiate<CardUi>();
        cardUi.SetCard(card);
        cardUi.CardToggled += OnCardToggled;

        _playerHand.AddChild(cardUi);
        _cardUiList.Add(cardUi);

        // Tween 飞牌
        var endOffset = new Vector2((_cardUiList.Count - 1) * 110, 0);
        var tween = CreateTween();
        tween.TweenProperty(cardUi, "position",
            _playerHand.GlobalPosition + endOffset, 0.25f)
             .SetTrans(Tween.TransitionType.Quad)
             .SetEase(Tween.EaseType.Out);
        tween.Finished += onComplete;
    }

    public void OnStatusMessage(string msg)
    {
        if (_statusLabel != null) _statusLabel.Text = msg;
    }

    public void OnDamageDealt(int damage, int remainingHP)
    {
        GD.Print($"[UI] 伤害: {damage}, 剩余 HP: {remainingHP}");
    }

    public void OnAgentPlayed(string agentId, string patternDesc, int cardCount)
    {
        GD.Print($"[UI] {agentId} 出牌: {patternDesc}");
        UpdateEnemyHandDisplay();
    }

    public void OnAgentPassed(string agentId)
    {
        GD.Print($"[UI] {agentId} Pass");
    }

    public void OnRoundResult(bool playerWon, string message)
    {
        GD.Print($"[UI] 回合结果: {(playerWon ? "玩家赢" : "敌人赢")} - {message}");
        if (_statusLabel != null) _statusLabel.Text = message;
    }

    public void OnBattleEnded(bool playerWon)
    {
        EnableButtons(false);

        var resultLabel = new Label
        {
            Text = playerWon ? "🎉 胜利！" : "💀 失败...",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        resultLabel.AddThemeFontSizeOverride("font_size", 48);
        resultLabel.SetPosition(new Vector2(640, 350));
        resultLabel.SetSize(new Vector2(300, 80));
        AddChild(resultLabel);

        var backBtn = new Button { Text = "返回主菜单" };
        backBtn.SetPosition(new Vector2(640, 450));
        backBtn.SetSize(new Vector2(200, 50));
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainScene.tscn");
        AddChild(backBtn);
    }

    public void OnEnemyHpChanged(int totalHP)
    {
        if (_enemyHP != null)
            _enemyHP.Text = $"❤️ 敌人 HP: {totalHP}";
    }

    public void OnPlayerInputChanged(bool enabled)
    {
        EnableButtons(enabled);
    }

    public void OnHandUpdated()
    {
        RefreshHandDisplay();
    }

    public void OnRiverUpdated()
    {
        UpdateRiverDisplay();
    }

    // ═══════════════════════════════════════════
    //  按钮回调
    // ═══════════════════════════════════════════

    private void OnPlayPressed()
    {
        if (_battle == null) return;
        var cards = new List<Card>(_selectedCards);
        if (cards.Count == 0) return;
        var err = _battle.PlayerPlay(cards);
        if (err != null && _statusLabel != null) _statusLabel.Text = err;
    }

    private void OnCallPressed()
    {
        if (_battle == null) return;
        var err = _battle.PlayerCallCards();
        if (err != null && _statusLabel != null) _statusLabel.Text = err;
    }

    private void OnPassPressed()
    {
        if (_battle == null) return;
        var err = _battle.PlayerPass();
        if (err != null && _statusLabel != null) _statusLabel.Text = err;
    }

    private void OnCardToggled(CardUi cardUi, bool selected)
    {
        if (cardUi.Card == null) return;
        if (selected) _selectedCards.Add(cardUi.Card);
        else _selectedCards.Remove(cardUi.Card);
    }

    // ═══════════════════════════════════════════
    //  UI 刷新
    // ═══════════════════════════════════════════

    private void RefreshHandDisplay()
    {
        if (_playerAgent == null || _playerHand == null) return;

        foreach (var cui in _cardUiList) cui.QueueFree();
        _cardUiList.Clear();
        _selectedCards.Clear();

        if (_cardUiScene != null)
        {
            foreach (var card in _playerAgent.Hands[0].Cards)
            {
                var cardUi = _cardUiScene.Instantiate<CardUi>();
                cardUi.SetCard(card);
                cardUi.CardToggled += OnCardToggled;
                _playerHand.AddChild(cardUi);
                _cardUiList.Add(cardUi);
            }
        }

        if (_playerDeckCount != null)
            _playerDeckCount.Text = $"📇 牌堆: {_playerAgent.Deck?.Count ?? 0}";
        if (_callCardCount != null)
            _callCardCount.Text = $"📞 叫牌剩余: {_playerAgent.RemainingCallCards}";
        if (_chainDepth != null && _battle != null)
            _chainDepth.Text = $"⛓️ 接龙深度: ×{_battle.Chain.DepthMultiplier} (第{_battle.Chain.PlayerHandCount + 1}手)";
    }

    private void UpdateRiverDisplay()
    {
        if (_battle == null || _riverLabel == null) return;
        var entries = _battle.River.Entries;
        if (entries.Count == 0) { _riverLabel.Text = "[牌河]"; return; }
        _riverLabel.Text = string.Join("  →  ",
            entries.Select(e => $"[{(e.Agent.IsPlayer ? "你" : "敌")}] {e.Pattern}"));
    }

    private void UpdateEnemyHandDisplay()
    {
        if (_battle == null || _enemyHandLabel == null) return;
        var enemy = _battle.Agents.FirstOrDefault(a => a.IsEnemy);
        if (enemy == null) { _enemyHandLabel.Text = "[敌方手牌]"; return; }
        _enemyHandLabel.Text = $"[敌方手牌] {string.Join(" ", enemy.Hands[0].Cards.Select(c => c))} (共{enemy.Hands[0].Count}张)";
    }

    private void EnableButtons(bool enabled)
    {
        if (_playBtn != null) _playBtn.Disabled = !enabled;
        if (_callBtn != null)
            _callBtn.Disabled = !enabled || (_playerAgent != null && !_playerAgent.CanCallCards);
        if (_passBtn != null) _passBtn.Disabled = !enabled;
    }

    private static Label MakeLabel(string text, int fontSize)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static Control MakeSpacer(int width) => new Control { Size = new Vector2(width, 1) };
}
