using System;
using System.Collections.Generic;
using Evolve.Core;
using UnityEngine;

namespace Evolve.Meta
{
    // ═══════════════════════════════════════════════════════════════════
    // META / PRESTIGE SYSTEM — Reset for permanent bonuses
    // ═══════════════════════════════════════════════════════════════════
    // Unlocks at: 1,000,000,000,000 energy (GamePhase.Meta)
    //
    // Prestige resets most progress but grants Prestige Points (PP) that
    // provide permanent multipliers. Each prestige run is faster due to
    // accumulated PP. Later, multi-layer prestige (Ascension) adds depth.
    //
    // Layer 1: Prestige (resets generators/resources, keeps PP + upgrades)
    // Layer 2: Ascension (resets PP, grants Ascension Tokens for mega bonuses)
    //
    // No system becomes obsolete: all systems remain, just amplified.
    // ═══════════════════════════════════════════════════════════════════

    [Serializable]
    public class PrestigeUpgrade
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public double Cost;            // PP cost
        public int Level;
        public int MaxLevel;
        public double EffectPerLevel;  // varies by type
        public PrestigeEffectType Effect;
    }

    public enum PrestigeEffectType
    {
        GlobalProductionMultiplier,   // +X% to all production
        ClickMultiplier,              // +X% to tap
        StartingEnergy,               // start with X energy after prestige
        MaxWorkers,                   // +X max workers
        MaxRules,                     // +X max logic rules
        MaxAIAgents,                  // +X max AI agents
        OfflineMultiplier,            // +X% offline earnings
        AutoBuySpeed,                 // auto-buy checks more often
    }

    [Serializable]
    public class PrestigeState
    {
        public int PrestigeCount;
        public BigNumber PrestigePoints;
        public BigNumber LifetimePrestigePoints;
        public List<PrestigeUpgrade> Upgrades = new();

        // Ascension (layer 2)
        public int AscensionCount;
        public BigNumber AscensionTokens;
        public double AscensionMultiplier; // compounds with prestige
    }

    public class MetaSystem : IGameSystem
    {
        public bool IsActive => GameManager.Instance != null
            && GameManager.Instance.CurrentPhase >= GamePhase.Meta;

        private GameManager _gm;
        private ResourceSystem _resources;
        private PrestigeState _state = new();

        public PrestigeState State => _state;

        public event Action<int> OnPrestige; // prestige count
        public event Action<int> OnAscension;
        public event Action<PrestigeUpgrade> OnPrestigeUpgradeBought;

        public void Initialize(GameManager gm)
        {
            _gm = gm;
            _resources = gm.GetSystem<ResourceSystem>();

            SetupPrestigeUpgrades();
        }

        private void SetupPrestigeUpgrades()
        {
            _state.Upgrades = new List<PrestigeUpgrade>
            {
                new()
                {
                    Id = "global_prod", DisplayName = "Empire Output",
                    Description = "+25% all production per level",
                    Cost = 5, MaxLevel = 100, EffectPerLevel = 0.25,
                    Effect = PrestigeEffectType.GlobalProductionMultiplier
                },
                new()
                {
                    Id = "click_mult", DisplayName = "Neural Tap",
                    Description = "+50% tap power per level",
                    Cost = 3, MaxLevel = 50, EffectPerLevel = 0.5,
                    Effect = PrestigeEffectType.ClickMultiplier
                },
                new()
                {
                    Id = "starting_energy", DisplayName = "Seed Capital",
                    Description = "Start with 10^(level+2) energy after prestige",
                    Cost = 10, MaxLevel = 20, EffectPerLevel = 1,
                    Effect = PrestigeEffectType.StartingEnergy
                },
                new()
                {
                    Id = "extra_workers", DisplayName = "Workforce Expansion",
                    Description = "+2 max workers per level",
                    Cost = 8, MaxLevel = 25, EffectPerLevel = 2,
                    Effect = PrestigeEffectType.MaxWorkers
                },
                new()
                {
                    Id = "extra_rules", DisplayName = "Logic Expansion",
                    Description = "+5 max logic rules per level",
                    Cost = 8, MaxLevel = 20, EffectPerLevel = 5,
                    Effect = PrestigeEffectType.MaxRules
                },
                new()
                {
                    Id = "extra_agents", DisplayName = "AI Expansion",
                    Description = "+1 max AI agent per level",
                    Cost = 15, MaxLevel = 10, EffectPerLevel = 1,
                    Effect = PrestigeEffectType.MaxAIAgents
                },
                new()
                {
                    Id = "offline_mult", DisplayName = "Dream Factory",
                    Description = "+20% offline earnings per level",
                    Cost = 5, MaxLevel = 50, EffectPerLevel = 0.2,
                    Effect = PrestigeEffectType.OfflineMultiplier
                },
            };
        }

        public void Tick(double delta)
        {
            // Meta system is passive — only acts on player input
        }

        // ─── PRESTIGE ───────────────────────────────────────────────

        /// <summary>
        /// Calculate how many prestige points would be earned right now.
        /// </summary>
        public BigNumber CalculatePrestigeReward()
        {
            BigNumber totalEarned = _resources.Resources.ContainsKey("energy")
                ? _resources.Resources["energy"].TotalEarned
                : BigNumber.Zero;

            return GameFormulas.PrestigeCurrency(totalEarned);
        }

        /// <summary>
        /// Execute prestige reset. Returns PP earned.
        /// </summary>
        public BigNumber ExecutePrestige()
        {
            BigNumber reward = CalculatePrestigeReward();
            if (reward <= BigNumber.Zero) return BigNumber.Zero;

            _state.PrestigePoints = _state.PrestigePoints + reward;
            _state.LifetimePrestigePoints = _state.LifetimePrestigePoints + reward;
            _state.PrestigeCount++;

            // Reset game state (resources, generators) but keep meta
            ResetGameState();

            // Apply prestige bonuses
            ApplyPrestigeBonuses();

            OnPrestige?.Invoke(_state.PrestigeCount);
            return reward;
        }

        private void ResetGameState()
        {
            // Reset resources to 0 (except prestige_points)
            foreach (var res in _resources.Resources.Values)
            {
                if (res.Id == "prestige_points") continue;
                res.Amount = BigNumber.Zero;
                res.TotalEarned = BigNumber.Zero;
            }

            // Reset generators to 0 owned
            foreach (var gen in _resources.Generators)
            {
                gen.Owned = gen.Tier == 0 ? 1 : 0; // keep clicker
                gen.Multiplier = 1.0;
            }

            // Reset upgrades
            foreach (var upg in _resources.Upgrades)
            {
                upg.Level = 0;
            }
        }

        private void ApplyPrestigeBonuses()
        {
            foreach (var upg in _state.Upgrades)
            {
                if (upg.Level <= 0) continue;

                switch (upg.Effect)
                {
                    case PrestigeEffectType.GlobalProductionMultiplier:
                        double mult = 1.0 + upg.EffectPerLevel * upg.Level;
                        _resources.ApplyGlobalMultiplier(mult);
                        break;

                    case PrestigeEffectType.ClickMultiplier:
                        var clicker = _resources.GetGenerator("clicker");
                        if (clicker != null)
                            clicker.Multiplier += upg.EffectPerLevel * upg.Level;
                        break;

                    case PrestigeEffectType.StartingEnergy:
                        double startEnergy = Math.Pow(10, upg.Level + 2);
                        _resources.AddResource("energy", new BigNumber(startEnergy));
                        break;

                    case PrestigeEffectType.MaxWorkers:
                        var auto = _gm.GetSystem<Automation.AutomationSystem>();
                        if (auto != null) auto.MaxWorkers += (int)(upg.EffectPerLevel * upg.Level);
                        break;

                    case PrestigeEffectType.MaxRules:
                        var logic = _gm.GetSystem<Logic.LogicSystem>();
                        if (logic != null) logic.UpgradeMaxRules((int)(upg.EffectPerLevel * upg.Level));
                        break;

                    case PrestigeEffectType.MaxAIAgents:
                        var ai = _gm.GetSystem<AI.AISystem>();
                        if (ai != null) ai.UpgradeMaxAgents((int)(upg.EffectPerLevel * upg.Level));
                        break;
                }
            }

            // Also apply base prestige multiplier
            double prestigeMult = GameFormulas.PrestigeMultiplier(_state.PrestigePoints);
            _resources.ApplyGlobalMultiplier(prestigeMult);
        }

        // ─── PRESTIGE UPGRADES ──────────────────────────────────────

        public bool BuyPrestigeUpgrade(string upgradeId)
        {
            var upg = _state.Upgrades.Find(u => u.Id == upgradeId);
            if (upg == null || upg.Level >= upg.MaxLevel) return false;

            double cost = upg.Cost * Math.Pow(1.5, upg.Level); // escalating cost
            if (_state.PrestigePoints.Value < cost) return false;

            _state.PrestigePoints = _state.PrestigePoints - new BigNumber(cost);
            upg.Level++;

            OnPrestigeUpgradeBought?.Invoke(upg);
            return true;
        }

        // ─── ASCENSION (LAYER 2) ────────────────────────────────────

        public BigNumber CalculateAscensionReward()
        {
            if (_state.PrestigeCount < 10) return BigNumber.Zero; // need 10+ prestiges
            return new BigNumber(Math.Floor(Math.Sqrt(_state.LifetimePrestigePoints.Value / 1000)));
        }

        public BigNumber ExecuteAscension()
        {
            BigNumber reward = CalculateAscensionReward();
            if (reward <= BigNumber.Zero) return BigNumber.Zero;

            _state.AscensionTokens = _state.AscensionTokens + reward;
            _state.AscensionCount++;

            // Full reset: prestige points, upgrades, everything
            _state.PrestigePoints = BigNumber.Zero;
            _state.LifetimePrestigePoints = BigNumber.Zero;
            _state.PrestigeCount = 0;
            foreach (var upg in _state.Upgrades) upg.Level = 0;

            ResetGameState();

            // Ascension multiplier applies on top of everything
            _state.AscensionMultiplier = 1.0 + _state.AscensionTokens.Value * 0.1;
            _resources.ApplyGlobalMultiplier(_state.AscensionMultiplier);

            OnAscension?.Invoke(_state.AscensionCount);
            return reward;
        }
    }
}
