using Godot;
using GuandanKitty.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GuandanKitty;

/// <summary>
/// 战斗 UI —— 纯显示层，发射 C# event 通知 Battle，接收 Battle 调用来更新显示。
/// 不持有 Battle 引用。
/// </summary>
public partial class BattleUI : Control
{
    // ═══════════════════════════════════════════
    //  事件 —— Battle 订阅以接收玩家输入
    // ═══════════════════════════════════════════

    /// <summary>玩家请求出牌</summary>
    public event Action<List<Card>>? PlayRequested;

    /// <summary>玩家请求 Pass</summary>
    public event Action? PassRequested;

    /// <summary>玩家请求叫牌</summary>
    public event Action? CallRequested;

    // ═══════════════════════════════════════════
    //  私有字段
    // ═══════════════════════════════════════════

    private PlayerAgent? _playerAgent;
    private EnemyAgent? _enemyAgent;
    private int _deckCount;
    private int _remainingCallCards;
    private int _chainDepthMultiplier;
    private int _chainPlayerHandCount;
    private string _riverText = "[牌河]";
    private string _enemyHandText = "[敌方手牌]";

    private Label? _statusLabel;
    private Label? _enemyHP;
    private Label? _playerDeckCount;
    private Label? _callCardCount;
    private Label? _chainDepth;
    private Label? _enemyHandLabel;
    private Label? _riverLabel;

    [Export] private HBoxContainer? _playerHand;
    [Export] private HBoxContainer? _enemyHand;
    private Button? _playBtn;
    private Button? _callBtn;
    private Button? _passBtn;

    private readonly HashSet<Card> _selectedCards = new();
    private PackedScene? _cardUiScene;

    public override void _Ready()
    {
        _cardUiScene = ResourceLoader.Load<PackedScene>("res://Scenes/CardUI.tscn");
        _playerHand?.GetChildren().ToList().ForEach(c => c.QueueFree());
        _enemyHand?.GetChildren().ToList().ForEach(c => c.QueueFree());
        BuildUI();
        Run.Instance.StartBattle(this);
    }

    /// <summary>
    /// 设置玩家 Agent 引用（由 Battle 在初始化时调用）。
    /// </summary>
    public void SetPlayerAgent(PlayerAgent agent)
    {
        _playerAgent = agent;
    }

    /// <summary>
    /// 设置敌方 Agent 引用（由 Battle 在初始化时调用）。
    /// </summary>
    public void SetEnemyAgent(EnemyAgent agent)
    {
        _enemyAgent = agent;
    }

    /// <summary>
    /// 显示战斗 UI。
    /// </summary>
    public void ShowBattle()
    {
        Show();
    }

    /// <summary>
    /// 隐藏战斗 UI。
    /// </summary>
    public void HideBattle()
    {
        Hide();
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

    // ═══════════════════════════════════════════
    //  按钮回调 —— 发射事件
    // ═══════════════════════════════════════════

    private void OnPlayPressed()
    {
        var cards = new List<Card>(_selectedCards);
        if (cards.Count == 0) return;
        PlayRequested?.Invoke(cards);
    }

    private void OnCallPressed()
    {
        CallRequested?.Invoke();
    }

    private void OnPassPressed()
    {
        PassRequested?.Invoke();
    }

    private void OnCardToggled(CardUi cardUi, bool selected)
    {
        if (cardUi.Card == null) return;
        if (selected) _selectedCards.Add(cardUi.Card);
        else _selectedCards.Remove(cardUi.Card);
    }

    // ═══════════════════════════════════════════
    //  以下方法由 Battle 直接调用（显示更新）
    // ═══════════════════════════════════════════

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
        backBtn.Pressed += () => UIRoot.Instance.LoadMainScene();
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

    /// <summary>
    /// 更新手牌显示数据（由 Battle 调用）。
    /// </summary>
    public void OnHandUpdated(int deckCount, int remainingCallCards,
        int chainDepthMultiplier, int chainPlayerHandCount)
    {
        _deckCount = deckCount;
        _remainingCallCards = remainingCallCards;
        _chainDepthMultiplier = chainDepthMultiplier;
        _chainPlayerHandCount = chainPlayerHandCount;
        RefreshHandDisplay();
    }

    /// <summary>
    /// 更新牌河显示（由 Battle 调用）。
    /// </summary>
    public void OnRiverUpdated(string riverText, string enemyHandText)
    {
        _riverText = riverText;
        _enemyHandText = enemyHandText;
        UpdateRiverDisplay();
        UpdateEnemyHandDisplay();
    }

    /// <summary>
    /// 更新敌方手牌显示（由 Battle 调用）。
    /// </summary>
    public void OnEnemyHandUpdated()
    {
        RefreshEnemyHandDisplay();
    }

    // ═══════════════════════════════════════════
    //  UI 刷新
    // ═══════════════════════════════════════════

    private void RefreshHandDisplay()
    {
        if (_playerAgent == null || _playerHand == null) return;

        foreach (var child in _playerHand.GetChildren())
            child.QueueFree();
        _selectedCards.Clear();

        if (_cardUiScene != null)
        {
            var cardUis = new List<CardUi>();
            foreach (var card in _playerAgent.Hand.Cards)
            {
                var cardUi = _cardUiScene.Instantiate<CardUi>();
                cardUi.SetCard(card);
                cardUi.CardToggled += OnCardToggled;
                cardUis.Add(cardUi);
            }

            SortHand(cardUis);

            foreach (var cardUi in cardUis)
            {
                _playerHand.AddChild(cardUi);
            }
        }

        if (_playerDeckCount != null)
            _playerDeckCount.Text = $"📇 牌堆: {_deckCount}";
        if (_callCardCount != null)
            _callCardCount.Text = $"📞 叫牌剩余: {_remainingCallCards}";
        if (_chainDepth != null)
            _chainDepth.Text = $"⛓️ 接龙深度: ×{_chainDepthMultiplier} (第{_chainPlayerHandCount + 1}手)";
    }

    /// <summary>
    /// 刷新敌方手牌区：清空、重新生成 CardUi、排序。
    /// </summary>
    private void RefreshEnemyHandDisplay()
    {
        if (_enemyAgent == null || _enemyHand == null) return;

        foreach (var child in _enemyHand.GetChildren())
            child.QueueFree();

        if (_cardUiScene == null) return;

        var cardUis = new List<CardUi>();
        foreach (var card in _enemyAgent.Hand.Cards)
        {
            var cardUi = _cardUiScene.Instantiate<CardUi>();
            cardUi.SetCard(card);
            cardUis.Add(cardUi);
        }

        SortHand(cardUis);

        foreach (var cardUi in cardUis)
        {
            _enemyHand.AddChild(cardUi);
        }
    }

    /// <summary>
    /// 整理手牌：按点数从大到小，同点数按 ♥♠♦♣ 排列。
    /// </summary>
    private static void SortHand(List<CardUi> cardUis)
    {
        cardUis.Sort((a, b) =>
        {
            var ca = a.Card;
            var cb = b.Card;
            if (ca == null || cb == null) return 0;

            // 点数降序
            int valueCmp = cb.FaceValue.CompareTo(ca.FaceValue);
            if (valueCmp != 0) return valueCmp;

            // 同点数按花色：♥ > ♠ > ♦ > ♣
            return GetSuitOrder(ca.Suit).CompareTo(GetSuitOrder(cb.Suit));
        });
    }

    private static int GetSuitOrder(Suit suit) => suit switch
    {
        Suit.Heart   => 0,
        Suit.Spade   => 1,
        Suit.Diamond => 2,
        Suit.Club    => 3,
        _ => 4
    };

    private void UpdateRiverDisplay()
    {
        if (_riverLabel != null)
            _riverLabel.Text = _riverText;
    }

    private void UpdateEnemyHandDisplay()
    {
        if (_enemyHandLabel != null)
            _enemyHandLabel.Text = _enemyHandText;
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
