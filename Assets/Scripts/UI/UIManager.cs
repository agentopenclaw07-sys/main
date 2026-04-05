using System;
using System.Collections.Generic;
using Evolve.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Evolve.UI
{
    // ═══════════════════════════════════════════════════════════════════
    // UI SYSTEM — Screen management, data binding, dynamic layout
    // ═══════════════════════════════════════════════════════════════════
    // Architecture:
    //   - Screen-based navigation (stack)
    //   - MVVM-style data binding via UIBinding components
    //   - Dynamic UI: screens show/hide based on GamePhase
    //   - Object pooling for list items (generators, workers, etc.)
    //
    // Mobile optimization:
    //   - UI updates throttled to 10 Hz (not every frame)
    //   - Off-screen elements disabled
    //   - Atlas-based sprites, minimal overdraw
    // ═══════════════════════════════════════════════════════════════════

    public enum ScreenId
    {
        Main,           // tap button, energy counter, generators
        Generators,     // buy/upgrade generators
        Automation,     // workers, auto-buy settings
        Logic,          // rule editor
        AI,             // AI agent management
        Simulation,     // businesses, market, population
        Prestige,       // prestige/ascension
        Shop,           // IAP, boosts
        Settings,       // sound, save, cloud
        Events,         // live events, daily rewards
        Achievements,   // achievement list
    }

    /// <summary>
    /// Base class for all game screens. Each screen is a panel/canvas group.
    /// </summary>
    public abstract class UIScreen : MonoBehaviour
    {
        public ScreenId Id;
        [SerializeField] protected CanvasGroup _canvasGroup;

        public virtual GamePhase RequiredPhase => GamePhase.Incremental;

        public virtual void Show()
        {
            gameObject.SetActive(true);
            if (_canvasGroup)
            {
                _canvasGroup.alpha = 1;
                _canvasGroup.interactable = true;
                _canvasGroup.blocksRaycasts = true;
            }
            OnShow();
        }

        public virtual void Hide()
        {
            if (_canvasGroup)
            {
                _canvasGroup.alpha = 0;
                _canvasGroup.interactable = false;
                _canvasGroup.blocksRaycasts = false;
            }
            gameObject.SetActive(false);
            OnHide();
        }

        protected virtual void OnShow() { }
        protected virtual void OnHide() { }
        public virtual void Refresh() { }
    }

    /// <summary>
    /// Main screen: tap area, energy display, quick generator list.
    /// Visible from game start.
    /// </summary>
    public class MainScreen : UIScreen
    {
        [SerializeField] private Text _energyText;
        [SerializeField] private Text _perSecondText;
        [SerializeField] private Button _tapButton;
        [SerializeField] private Text _tapFeedbackText;
        [SerializeField] private RectTransform _generatorListParent;

        private ResourceSystem _resources;
        private double _tapFeedbackTimer;

        protected override void OnShow()
        {
            Id = ScreenId.Main;
            _resources = GameManager.Instance.GetSystem<ResourceSystem>();
            _tapButton.onClick.AddListener(OnTap);
        }

        private void OnTap()
        {
            BigNumber amount = _resources.ProcessTap();
            _tapFeedbackText.text = $"+{amount}";
            _tapFeedbackTimer = 1.0;
        }

        public override void Refresh()
        {
            _energyText.text = _resources.GetAmount("energy").ToString();
            _perSecondText.text = $"{_resources.GetPerSecond("energy")}/s";

            // Fade tap feedback
            if (_tapFeedbackTimer > 0)
            {
                _tapFeedbackTimer -= Time.deltaTime;
                _tapFeedbackText.color = new Color(1, 1, 1, (float)_tapFeedbackTimer);
            }
        }
    }

    /// <summary>
    /// Generators screen: list of buyable generators with costs and production.
    /// </summary>
    public class GeneratorScreen : UIScreen
    {
        [SerializeField] private RectTransform _listParent;
        [SerializeField] private GameObject _itemPrefab;

        private ResourceSystem _resources;
        private readonly List<GeneratorListItem> _items = new();

        protected override void OnShow()
        {
            Id = ScreenId.Generators;
            _resources = GameManager.Instance.GetSystem<ResourceSystem>();
            RebuildList();
        }

        private void RebuildList()
        {
            // Pool existing items
            foreach (var item in _items) item.gameObject.SetActive(false);
            _items.Clear();

            for (int i = 1; i < _resources.Generators.Count; i++) // skip clicker
            {
                var gen = _resources.Generators[i];
                var go = Instantiate(_itemPrefab, _listParent);
                var item = go.GetComponent<GeneratorListItem>();
                item.Bind(gen, _resources);
                _items.Add(item);
            }
        }

        public override void Refresh()
        {
            foreach (var item in _items) item.Refresh();
        }
    }

    /// <summary>
    /// Individual generator row in the list.
    /// </summary>
    public class GeneratorListItem : MonoBehaviour
    {
        [SerializeField] private Text _nameText;
        [SerializeField] private Text _ownedText;
        [SerializeField] private Text _productionText;
        [SerializeField] private Text _costText;
        [SerializeField] private Button _buyButton;
        [SerializeField] private Button _buy10Button;

        private GeneratorData _data;
        private ResourceSystem _resources;

        public void Bind(GeneratorData data, ResourceSystem resources)
        {
            _data = data;
            _resources = resources;
            _nameText.text = data.DisplayName;

            _buyButton.onClick.AddListener(() => _resources.BuyGenerator(_data.Id));
            _buy10Button.onClick.AddListener(() => _resources.BuyGeneratorBulk(_data.Id, 10));

            Refresh();
        }

        public void Refresh()
        {
            _ownedText.text = $"x{_data.Owned}";
            _productionText.text = $"{_data.CurrentProduction}/s";
            _costText.text = _data.CurrentCost.ToString();

            bool canAfford = _resources.GetAmount("energy") >= _data.CurrentCost;
            _buyButton.interactable = canAfford;
        }
    }

    /// <summary>
    /// Manages all screens, navigation, and UI update throttling.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [SerializeField] private List<UIScreen> _screens = new();
        [SerializeField] private RectTransform _tabBar;

        private readonly Stack<ScreenId> _navStack = new();
        private UIScreen _currentScreen;
        private float _refreshTimer;
        private const float REFRESH_INTERVAL = 0.1f; // 10 Hz UI updates

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void Start()
        {
            // Start on main screen
            NavigateTo(ScreenId.Main);

            // Listen for phase changes to show/hide tabs
            GameManager.Instance.OnPhaseChanged += OnPhaseChanged;
            UpdateTabVisibility();
        }

        private void Update()
        {
            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= REFRESH_INTERVAL)
            {
                _refreshTimer -= REFRESH_INTERVAL;
                _currentScreen?.Refresh();
            }
        }

        public void NavigateTo(ScreenId screenId)
        {
            var screen = _screens.Find(s => s.Id == screenId);
            if (screen == null) return;

            // Check if phase requirement is met
            if (GameManager.Instance.CurrentPhase < screen.RequiredPhase) return;

            _currentScreen?.Hide();
            _navStack.Push(screenId);
            _currentScreen = screen;
            _currentScreen.Show();
        }

        public void NavigateBack()
        {
            if (_navStack.Count <= 1) return;

            _currentScreen?.Hide();
            _navStack.Pop();

            var prevId = _navStack.Peek();
            var screen = _screens.Find(s => s.Id == prevId);
            _currentScreen = screen;
            _currentScreen?.Show();
        }

        private void OnPhaseChanged(GamePhase oldPhase, GamePhase newPhase)
        {
            UpdateTabVisibility();
        }

        private void UpdateTabVisibility()
        {
            // Show/hide tab buttons based on current phase
            // Each tab maps to a screen which has a RequiredPhase
            var phase = GameManager.Instance.CurrentPhase;

            foreach (var screen in _screens)
            {
                // Tab buttons have the same index as screen order
                // In a real implementation, tabs would be a separate list
                bool visible = phase >= screen.RequiredPhase;
                // Tab bar children would be enabled/disabled here
            }
        }

        /// <summary>
        /// Screen layout definition for UI building:
        ///
        /// MAIN SCREEN (Phase: Incremental)
        /// ┌─────────────────────────────┐
        /// │ [Energy: 1.5K]  [+42.3/s]   │  ← top bar, always visible
        /// │─────────────────────────────│
        /// │                             │
        /// │        [ TAP HERE ]         │  ← big tap button, center
        /// │        +1 Energy            │
        /// │                             │
        /// │─────────────────────────────│
        /// │ Micro Gen  x5    10/s  [Buy]│  ← quick generator list
        /// │ Power Cell x2    16/s  [Buy]│
        /// │ Reactor    x0     0/s  [Buy]│
        /// │─────────────────────────────│
        /// │ [Main][Gen][Auto][Logic][AI]│  ← tab bar, grows with phases
        /// └─────────────────────────────┘
        ///
        /// AUTOMATION SCREEN (Phase: Automation)
        /// ┌─────────────────────────────┐
        /// │ Workers (2/3)    [Hire $200] │
        /// │─────────────────────────────│
        /// │ Worker 1 → Energy   [Rest]  │
        /// │   Eff: 95%  Lvl: 1          │
        /// │ Worker 2 → Data     [Rest]  │
        /// │   Eff: 82%  Lvl: 1          │
        /// │─────────────────────────────│
        /// │ AUTO-BUY                    │
        /// │ ☑ Micro Gen   Priority: 1   │
        /// │ ☐ Power Cell  Priority: 2   │
        /// └─────────────────────────────┘
        ///
        /// LOGIC SCREEN (Phase: Logic)
        /// ┌─────────────────────────────┐
        /// │ Rules (3/20)    [+ New Rule] │
        /// │─────────────────────────────│
        /// │ ▶ Auto Sell Energy     [ON] │
        /// │   IF energy > 50K           │
        /// │   THEN sell 10K energy      │
        /// │ ▶ Buy Reactors         [ON] │
        /// │   IF reactors < 10          │
        /// │   THEN buy reactor          │
        /// │─────────────────────────────│
        /// │ [Edit Rule]  [Templates]    │
        /// └─────────────────────────────┘
        ///
        /// AI SCREEN (Phase: AI)
        /// ┌─────────────────────────────┐
        /// │ AI Agents (1/3)  [+ Deploy] │
        /// │─────────────────────────────│
        /// │ "Optimizer-1"        [ON]   │
        /// │  Last action: Buy Best Gen  │
        /// │  Personality:               │
        /// │   Aggr: ■■■□□ Eff: ■■■■□   │
        /// │   Grow: ■■■■■ Stab: ■■□□□  │
        /// │─────────────────────────────│
        /// │ Performance: +15% /hr       │
        /// └─────────────────────────────┘
        ///
        /// SIMULATION SCREEN (Phase: Simulation)
        /// ┌─────────────────────────────┐
        /// │ Population: 523/1000        │
        /// │ Available Workers: 211      │
        /// │─────────────────────────────│
        /// │ BUSINESSES                  │
        /// │ Mine Lv3     → Energy  [+W] │
        /// │ Factory Lv2  → Data    [+W] │
        /// │ Lab Lv1      → Research[+W] │
        /// │─────────────────────────────│
        /// │ MARKET          Price  Trend│
        /// │ Energy          1.2    ↑    │
        /// │ Data            4.8    ↓    │
        /// │ [Buy]  [Sell]               │
        /// └─────────────────────────────┘
        ///
        /// PRESTIGE SCREEN (Phase: Meta)
        /// ┌─────────────────────────────┐
        /// │ Prestige Points: 142        │
        /// │ Available: +38 PP           │
        /// │ [PRESTIGE NOW]              │
        /// │─────────────────────────────│
        /// │ UPGRADES                    │
        /// │ Empire Output Lv5    [Buy 8]│
        /// │ Neural Tap    Lv3    [Buy 5]│
        /// │ Seed Capital  Lv1   [Buy 15]│
        /// │─────────────────────────────│
        /// │ Ascension Tokens: 0         │
        /// │ Requires: 10 Prestiges      │
        /// └─────────────────────────────┘
        /// </summary>
        public void BuildLayoutDocumentation() { } // see ASCII art above
    }
}
