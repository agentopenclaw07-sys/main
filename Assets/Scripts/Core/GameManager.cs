using UnityEngine;
using System;
using System.Collections.Generic;

namespace Evolve.Core
{
    /// <summary>
    /// Central game manager. Owns the tick loop, system registry, and game state lifecycle.
    /// All systems register here and are updated in dependency order.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Tick Settings")]
        [SerializeField] private float _tickInterval = 0.1f; // 10 ticks/sec
        [SerializeField] private float _offlineTickCap = 28800f; // 8 hours max offline

        public float TickInterval => _tickInterval;
        public double GameTime { get; private set; }
        public long TickCount { get; private set; }
        public GamePhase CurrentPhase { get; private set; } = GamePhase.Incremental;

        private float _tickAccumulator;
        private readonly List<IGameSystem> _systems = new();
        private readonly Dictionary<Type, IGameSystem> _systemLookup = new();

        public event Action<double> OnTick; // delta
        public event Action<GamePhase, GamePhase> OnPhaseChanged;

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            RegisterCoreSystems();
        }

        private void RegisterCoreSystems()
        {
            RegisterSystem(new ResourceSystem());
            RegisterSystem(new Automation.AutomationSystem());
            RegisterSystem(new Logic.LogicSystem());
            RegisterSystem(new AI.AISystem());
            RegisterSystem(new Simulation.SimulationSystem());
            RegisterSystem(new Meta.MetaSystem());
            RegisterSystem(new Monetization.MonetizationSystem());
            RegisterSystem(new LiveOps.LiveOpsSystem());
        }

        public void RegisterSystem(IGameSystem system)
        {
            _systems.Add(system);
            _systemLookup[system.GetType()] = system;
            system.Initialize(this);
        }

        public T GetSystem<T>() where T : class, IGameSystem
        {
            return _systemLookup.TryGetValue(typeof(T), out var sys) ? (T)sys : null;
        }

        private void Start()
        {
            SaveSystem.SaveManager.Instance.Load();
            ProcessOfflineProgress();
        }

        private void Update()
        {
            _tickAccumulator += Time.deltaTime;

            // Cap to prevent spiral of death
            if (_tickAccumulator > 1f) _tickAccumulator = 1f;

            while (_tickAccumulator >= _tickInterval)
            {
                _tickAccumulator -= _tickInterval;
                ExecuteTick(_tickInterval);
            }
        }

        private void ExecuteTick(double delta)
        {
            GameTime += delta;
            TickCount++;

            for (int i = 0; i < _systems.Count; i++)
            {
                if (_systems[i].IsActive)
                    _systems[i].Tick(delta);
            }

            OnTick?.Invoke(delta);
        }

        public void ProcessOfflineProgress()
        {
            var save = SaveSystem.SaveManager.Instance;
            if (save.LastSaveTime <= 0) return;

            double elapsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - save.LastSaveTime;
            elapsed = Math.Min(elapsed, _offlineTickCap);

            if (elapsed < 1) return;

            // Offline runs at reduced tick rate for performance
            double offlineTickSize = 1.0; // 1-second granularity
            int offlineTicks = (int)(elapsed / offlineTickSize);

            for (int i = 0; i < offlineTicks; i++)
            {
                ExecuteTick(offlineTickSize);
            }

            Debug.Log($"[GameManager] Processed {offlineTicks} offline ticks ({elapsed:F0}s)");
        }

        public void AdvancePhase(GamePhase newPhase)
        {
            if (newPhase <= CurrentPhase) return;
            var old = CurrentPhase;
            CurrentPhase = newPhase;
            OnPhaseChanged?.Invoke(old, newPhase);
            Debug.Log($"[GameManager] Phase advanced: {old} → {newPhase}");
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SaveSystem.SaveManager.Instance.Save();
            else ProcessOfflineProgress();
        }

        private void OnApplicationQuit()
        {
            SaveSystem.SaveManager.Instance.Save();
        }
    }

    public enum GamePhase
    {
        Incremental = 0,  // Click & basic upgrades
        Automation = 1,   // Auto-generators, workers
        Logic = 2,        // IF/THEN rules, routing
        AI = 3,           // Self-optimizing agents
        Simulation = 4,   // World-scale systems
        Meta = 5          // Prestige, multiverse
    }

    public interface IGameSystem
    {
        bool IsActive { get; }
        void Initialize(GameManager gm);
        void Tick(double delta);
    }
}
