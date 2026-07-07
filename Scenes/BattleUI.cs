using Godot;
using GuandanKitty.Core;

namespace GuandanKitty;

public partial class BattleUI : Control
{
    private BattleManager? _battle;
    private Agent? _playerAgent;

    // UI 元素
    private Label? _statusLabel;
    private Label? _enemyHP;
    private Label? _playerDeckCount;
    private Label? _callCardCount;
    private Label? _chainDepth;
    private Label? _enemyHandLabel;
    private Label? _riverLabel;

    private HBoxContainer? _playerHand;
    private HBoxContainer? _actionButtons;
    private Button? _playBtn;
    private Button? _callBtn;
    private Button? _passBtn;

    private readonly List<CardUi> _cardUiList = new();
    private readonly HashSet<Card> _selectedCards = new();
    private PackedScene? _cardUiScene;

    public override void _Ready()
    {
        // 窗口大小由 project.godot 中的 1920×1080 控制
        _cardUiScene = ResourceLoader.Load<PackedScene>("res://Scenes/CardUI.tscn");
        _playerHand = GetNode<HBoxContainer>("PlayerHand");
        BuildUI();
        StartBattle();
    }

    private void BuildUI()
    {
        // === 状态信息栏（顶部） ===
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

        // === 敌人手牌区（顶部偏下） ===
        _enemyHandLabel = MakeLabel("[敌方手牌]", 14);
        _enemyHandLabel.SetPosition(new Vector2(20, 120));
        AddChild(_enemyHandLabel);

        // === 牌河区（中央） ===
        _riverLabel = MakeLabel("[牌河]", 14);
        _riverLabel.SetPosition(new Vector2(20, 300));
        _riverLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _riverLabel.SetSize(new Vector2(1240, 30));
        AddChild(_riverLabel);

        // === 玩家状态栏 ===
        var playerStatus = new HBoxContainer();
        playerStatus.SetPosition(new Vector2(20, 440));
        AddChild(playerStatus);

        _playerDeckCount = MakeLabel("", 14);
        playerStatus.AddChild(_playerDeckCount);
        playerStatus.AddChild(MakeSpacer(20));

        _callCardCount = MakeLabel("", 14);
        playerStatus.AddChild(_callCardCount);

        // === 操作按钮 ===
        _actionButtons = new HBoxContainer();
        _actionButtons.SetPosition(new Vector2(20, 740));
        AddChild(_actionButtons);

        _playBtn = new Button { Text = "▶ 出牌", Disabled = true };
        _playBtn.SetSize(new Vector2(160, 50));
        _playBtn.Pressed += OnPlayPressed;
        _actionButtons.AddChild(_playBtn);

        _callBtn = new Button { Text = "📞 叫牌 (+6)", Disabled = true };
        _callBtn.SetSize(new Vector2(160, 50));
        _callBtn.Pressed += OnCallPressed;
        _actionButtons.AddChild(_callBtn);

        _passBtn = new Button { Text = "跳过", Disabled = true };
        _passBtn.SetSize(new Vector2(120, 50));
        _passBtn.Pressed += OnPassPressed;
        _actionButtons.AddChild(_passBtn);
    }

    private void StartBattle()
    {
        _battle = new BattleManager();
        _battle.StateChanged += OnStateChanged;
        _battle.PlayerTurn += OnPlayerTurn;
        _battle.EnemyTurn += OnEnemyTurn;
        _battle.AgentPlayed += OnAgentPlayed;
        _battle.AgentPassed += OnAgentPassed;
        _battle.DamageDealt += OnDamageDealt;
        _battle.RoundResult += OnRoundResult;
        _battle.BattleEnded += OnBattleEnded;
        _battle.HandUpdated += RefreshHandDisplay;
        AddChild(_battle);

        // 创建玩家
        var player = new Agent
        {
            Id = "玩家",
            Type = AgentType.Player,
            Deck = new StandardDeck(),
        };
        player.Hands.Add(new HandZone());

        // 创建敌人
        var enemy = new Agent
        {
            Id = "训练假人",
            Type = AgentType.Enemy,
        };
        enemy.Hands.Add(new HandZone());
        enemy.Monsters.Add(MonsterDatabase.CreateTestMonster());
        _playerAgent = player;

        _battle.StartBattle(new List<Agent> { player, enemy });
    }

    // ============ 信号处理 ============

    private void OnStateChanged(string state, string message)
    {
        if (_statusLabel != null)
            _statusLabel.Text = message;
    }

    private void OnPlayerTurn()
    {
        if (_playBtn != null) _playBtn.Disabled = false;
        if (_callBtn != null) _callBtn.Disabled = !_playerAgent!.CanCallCards;
        if (_passBtn != null) _passBtn.Disabled = false;
        // 清除所有卡牌选中状态
        foreach (var cui in _cardUiList)
            cui.SetSelected(false);
        _selectedCards.Clear();
    }

    private void OnEnemyTurn(string agentId)
    {
        if (_playBtn != null) _playBtn.Disabled = true;
        if (_callBtn != null) _callBtn.Disabled = true;
        if (_passBtn != null) _passBtn.Disabled = true;
        if (_statusLabel != null)
            _statusLabel.Text = $"{agentId} 思考中...";
    }

    private void OnAgentPlayed(string agentId, string patternDesc, int cardCount)
    {
        GD.Print($"[UI] {agentId} 出牌: {patternDesc}");
        UpdateRiverDisplay();
        UpdateEnemyHandDisplay();
    }

    private void OnAgentPassed(string agentId)
    {
        GD.Print($"[UI] {agentId} Pass");
    }

    private void OnDamageDealt(int damage, int remainingHP)
    {
        if (_enemyHP != null)
            _enemyHP.Text = $"❤️ 敌人 HP: {remainingHP}  (-{damage})";
    }

    private void OnRoundResult(bool playerWon, string message)
    {
        GD.Print($"[UI] 回合结果: {(playerWon ? "玩家赢" : "敌人赢")} - {message}");
        if (_statusLabel != null)
            _statusLabel.Text = message;
    }

    private void OnBattleEnded(bool playerWon)
    {
        if (_playBtn != null) _playBtn.Disabled = true;
        if (_callBtn != null) _callBtn.Disabled = true;
        if (_passBtn != null) _passBtn.Disabled = true;

        var resultLabel = new Label
        {
            Text = playerWon ? "🎉 胜利！" : "💀 失败...",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        resultLabel.AddThemeFontSizeOverride("font_size", 48);
        resultLabel.SetPosition(new Vector2(640, 350));
        resultLabel.SetSize(new Vector2(300, 80));
        AddChild(resultLabel);

        // 返回按钮
        var backBtn = new Button { Text = "返回主菜单" };
        backBtn.SetPosition(new Vector2(640, 450));
        backBtn.SetSize(new Vector2(200, 50));
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainScene.tscn");
        AddChild(backBtn);
    }

    // ============ 按钮回调 ============

    private void OnPlayPressed()
    {
        if (_battle == null) return;

        var cards = new List<Card>(_selectedCards);
        if (cards.Count == 0) return;

        var error = _battle.PlayerPlay(cards);
        if (error != null)
        {
            if (_statusLabel != null) _statusLabel.Text = error;
        }
    }

    private void OnCallPressed()
    {
        if (_battle == null) return;
        var error = _battle.PlayerCallCards();
        if (error != null && _statusLabel != null)
            _statusLabel.Text = error;
    }

    private void OnPassPressed()
    {
        if (_battle == null) return;
        var error = _battle.PlayerPass();
        if (error != null && _statusLabel != null)
            _statusLabel.Text = error;
    }

    private void OnCardToggled(CardUi cardUi, bool selected)
    {
        if (cardUi.Card == null) return;
        if (selected)
            _selectedCards.Add(cardUi.Card);
        else
            _selectedCards.Remove(cardUi.Card);
    }

    // ============ UI 刷新 ============

    private void RefreshHandDisplay()
    {
        if (_playerAgent == null || _playerHand == null) return;

        // 清空旧卡牌
        foreach (var cui in _cardUiList)
            cui.QueueFree();
        _cardUiList.Clear();
        _selectedCards.Clear();

        // 创建新的 CardUI 实例
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

        // 更新状态信息
        if (_playerDeckCount != null)
            _playerDeckCount.Text = $"📇 牌堆: {_playerAgent.Deck?.Count ?? 0}";
        if (_callCardCount != null)
            _callCardCount.Text = $"📞 叫牌剩余: {_playerAgent.RemainingCallCards}";
        if (_chainDepth != null && _battle != null)
            _chainDepth.Text = $"⛓️ 接龙深度: ×{_battle.Chain.DepthMultiplier} (第{_battle.Chain.PlayerHandCount + 1}手)";

        if (_enemyHP != null && _battle != null)
            _enemyHP.Text = $"❤️ 敌人 HP: {_battle.TotalEnemyHP}";
    }

    private void UpdateRiverDisplay()
    {
        if (_battle == null || _riverLabel == null) return;
        var entries = _battle.River.Entries;
        if (entries.Count == 0)
        {
            _riverLabel.Text = "[牌河]";
            return;
        }
        var parts = entries.Select(e =>
            $"[{(e.Agent.IsPlayer ? "你" : "敌")}] {e.Pattern}");
        _riverLabel.Text = string.Join("  →  ", parts);
    }

    private void UpdateEnemyHandDisplay()
    {
        if (_battle == null || _enemyHandLabel == null) return;
        var enemy = _battle.Agents.FirstOrDefault(a => a.IsEnemy);
        if (enemy == null)
        {
            _enemyHandLabel.Text = "[敌方手牌]";
            return;
        }
        var cards = string.Join(" ", enemy.Hands[0].Cards.Select(c => c.ToString()));
        _enemyHandLabel.Text = $"[敌方手牌] {cards} (共{enemy.Hands[0].Count}张)";
    }

    // ============ 工具方法 ============

    private static Label MakeLabel(string text, int fontSize)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        return label;
    }

    private static Control MakeSpacer(int width)
    {
        return new Control { Size = new Vector2(width, 1) };
    }
}
