using System;
using System.Collections.Generic;
using Evolve.Core;
using UnityEngine;

namespace Evolve
{
    // ═══════════════════════════════════════════════════════════════════
    // RESOURCE SYSTEM — The foundation of everything
    // ═══════════════════════════════════════════════════════════════════
    // Responsibilities:
    //   - Track all currencies/resources
    //   - Process generators (things that produce resources per tick)
    //   - Handle upgrades that modify production
    //   - Expose resource change events for UI binding
    //
    // Inputs:  Player taps, generator ownership, upgrade levels, multipliers
    // Outputs: Resource balances, production rates, unlock triggers
    //
    // Scaling: All values use BigNumber (double-based). Generator array is
    //          fixed-size (8 tiers). Upgrades are data-driven from JSON configs.
    // ═══════════════════════════════════════════════════════════════════

    [Serializable]
    public class ResourceData
    {
        public string Id;
        public string DisplayName;
        public BigNumber Amount;
        public BigNumber TotalEarned; // lifetime, for prestige calc
        public BigNumber PerSecond;   // computed each tick
        public BigNumber Capacity;    // 0 = unlimited
    }

    [Serializable]
    public class GeneratorData
    {
        public string Id;
        public string DisplayName;
        public int Tier;              // 0 = base (manual click equivalent)
        public string ProducesResource; // resource ID it feeds
        public double BaseProduction; // per unit per second
        public double BaseCost;
        public double CostGrowth;     // exponential factor (typically 1.15)
        public int Owned;
        public double Multiplier;     // from upgrades, prestige, etc.

        public BigNumber CurrentCost => GameFormulas.UpgradeCost(BaseCost, CostGrowth, Owned);
        public BigNumber CurrentProduction => GameFormulas.GeneratorProduction(BaseProduction, Owned, Multiplier);
    }

    [Serializable]
    public class UpgradeData
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string TargetGenerator;  // generator ID it boosts
        public string CostResource;     // resource ID to spend
        public double BaseCost;
        public double CostGrowth;
        public int Level;
        public int MaxLevel;
        public double MultiplierPerLevel; // e.g., 0.5 = +50% per level

        public BigNumber CurrentCost => GameFormulas.UpgradeCost(BaseCost, CostGrowth, Level);
        public bool IsMaxed => MaxLevel > 0 && Level >= MaxLevel;
    }

    public class ResourceSystem : IGameSystem
    {
        public bool IsActive => true; // Always active

        private GameManager _gm;
        private readonly Dictionary<string, ResourceData> _resources = new();
        private readonly List<GeneratorData> _generators = new();
        private readonly List<UpgradeData> _upgrades = new();

        // Events for UI binding
        public event Action<string, BigNumber> OnResourceChanged;
        public event Action<string, BigNumber> OnProductionChanged;
        public event Action<GeneratorData> OnGeneratorPurchased;
        public event Action<UpgradeData> OnUpgradePurchased;

        // Read-only access
        public IReadOnlyDictionary<string, ResourceData> Resources => _resources;
        public IReadOnlyList<GeneratorData> Generators => _generators;
        public IReadOnlyList<UpgradeData> Upgrades => _upgrades;

        public void Initialize(GameManager gm)
        {
            _gm = gm;
            SetupDefaultResources();
            SetupDefaultGenerators();
            SetupDefaultUpgrades();
        }

        private void SetupDefaultResources()
        {
            AddResource(new ResourceData
            {
                Id = "energy",
                DisplayName = "Energy",
                Amount = new BigNumber(0),
                Capacity = BigNumber.Zero // unlimited
            });

            AddResource(new ResourceData
            {
                Id = "data",
                DisplayName = "Data",
                Amount = BigNumber.Zero,
                Capacity = new BigNumber(1000) // starts capped, upgradeable
            });

            AddResource(new ResourceData
            {
                Id = "credits",
                DisplayName = "Credits",
                Amount = BigNumber.Zero,
            });

            AddResource(new ResourceData
            {
                Id = "research",
                DisplayName = "Research",
                Amount = BigNumber.Zero,
            });

            AddResource(new ResourceData
            {
                Id = "prestige_points",
                DisplayName = "Prestige Points",
                Amount = BigNumber.Zero,
            });
        }

        private void SetupDefaultGenerators()
        {
            // Tier 0: Manual click (handled separately, but tracked here)
            _generators.Add(new GeneratorData
            {
                Id = "clicker",
                DisplayName = "Manual Tap",
                Tier = 0,
                ProducesResource = "energy",
                BaseProduction = 1,
                BaseCost = 0,
                CostGrowth = 1,
                Owned = 1,
                Multiplier = 1
            });

            // Tier 1: Micro Generator — first auto-producer
            _generators.Add(new GeneratorData
            {
                Id = "micro_gen",
                DisplayName = "Micro Generator",
                Tier = 1,
                ProducesResource = "energy",
                BaseProduction = 1,
                BaseCost = 10,
                CostGrowth = 1.15,
                Owned = 0,
                Multiplier = 1
            });

            // Tier 2: Power Cell
            _generators.Add(new GeneratorData
            {
                Id = "power_cell",
                DisplayName = "Power Cell",
                Tier = 2,
                ProducesResource = "energy",
                BaseProduction = 8,
                BaseCost = 100,
                CostGrowth = 1.15,
                Owned = 0,
                Multiplier = 1
            });

            // Tier 3: Reactor
            _generators.Add(new GeneratorData
            {
                Id = "reactor",
                DisplayName = "Reactor",
                Tier = 3,
                ProducesResource = "energy",
                BaseProduction = 47,
                BaseCost = 1_000,
                CostGrowth = 1.15,
                Owned = 0,
                Multiplier = 1
            });

            // Tier 4: Fusion Core
            _generators.Add(new GeneratorData
            {
                Id = "fusion_core",
                DisplayName = "Fusion Core",
                Tier = 4,
                ProducesResource = "energy",
                BaseProduction = 260,
                BaseCost = 12_000,
                CostGrowth = 1.15,
                Owned = 0,
                Multiplier = 1
            });

            // Tier 5: Quantum Array
            _generators.Add(new GeneratorData
            {
                Id = "quantum_array",
                DisplayName = "Quantum Array",
                Tier = 5,
                ProducesResource = "energy",
                BaseProduction = 1_400,
                BaseCost = 130_000,
                CostGrowth = 1.15,
                Owned = 0,
                Multiplier = 1
            });

            // Tier 6: Dyson Collector
            _generators.Add(new GeneratorData
            {
                Id = "dyson_collector",
                DisplayName = "Dyson Collector",
                Tier = 6,
                ProducesResource = "energy",
                BaseProduction = 7_800,
                BaseCost = 1_400_000,
                CostGrowth = 1.15,
                Owned = 0,
                Multiplier = 1
            });

            // Tier 7: Singularity Engine
            _generators.Add(new GeneratorData
            {
                Id = "singularity_engine",
                DisplayName = "Singularity Engine",
                Tier = 7,
                ProducesResource = "energy",
                BaseProduction = 44_000,
                BaseCost = 20_000_000,
                CostGrowth = 1.15,
                Owned = 0,
                Multiplier = 1
            });
        }

        private void SetupDefaultUpgrades()
        {
            _upgrades.Add(new UpgradeData
            {
                Id = "click_power",
                DisplayName = "Click Power",
                Description = "+100% tap production per level",
                TargetGenerator = "clicker",
                CostResource = "energy",
                BaseCost = 50,
                CostGrowth = 2.0,
                MaxLevel = 50,
                MultiplierPerLevel = 1.0
            });

            _upgrades.Add(new UpgradeData
            {
                Id = "micro_boost",
                DisplayName = "Micro Boost",
                Description = "+50% Micro Generator output per level",
                TargetGenerator = "micro_gen",
                CostResource = "energy",
                BaseCost = 100,
                CostGrowth = 1.8,
                MaxLevel = 100,
                MultiplierPerLevel = 0.5
            });

            _upgrades.Add(new UpgradeData
            {
                Id = "cell_boost",
                DisplayName = "Cell Boost",
                Description = "+50% Power Cell output per level",
                TargetGenerator = "power_cell",
                CostResource = "energy",
                BaseCost = 1_000,
                CostGrowth = 1.8,
                MaxLevel = 100,
                MultiplierPerLevel = 0.5
            });

            _upgrades.Add(new UpgradeData
            {
                Id = "data_storage",
                DisplayName = "Data Storage",
                Description = "+500 Data capacity per level",
                TargetGenerator = "",
                CostResource = "energy",
                BaseCost = 500,
                CostGrowth = 1.5,
                MaxLevel = 200,
                MultiplierPerLevel = 0
            });
        }

        public void Tick(double delta)
        {
            // Compute and apply production for all generators (skip tier 0 = manual)
            for (int i = 1; i < _generators.Count; i++)
            {
                var gen = _generators[i];
                if (gen.Owned <= 0) continue;

                BigNumber produced = gen.CurrentProduction * delta;
                AddResource(gen.ProducesResource, produced);
            }

            // Recompute per-second rates for UI
            RecomputeRates();

            // Check phase unlocks
            CheckUnlocks();
        }

        private void RecomputeRates()
        {
            // Reset
            foreach (var r in _resources.Values) r.PerSecond = BigNumber.Zero;

            for (int i = 1; i < _generators.Count; i++)
            {
                var gen = _generators[i];
                if (gen.Owned <= 0) continue;

                if (_resources.TryGetValue(gen.ProducesResource, out var res))
                {
                    res.PerSecond = res.PerSecond + gen.CurrentProduction;
                }
            }
        }

        private void CheckUnlocks()
        {
            var energy = GetAmount("energy").Value;

            if (energy >= GameFormulas.UnlockAutomation && _gm.CurrentPhase < GamePhase.Automation)
                _gm.AdvancePhase(GamePhase.Automation);
            if (energy >= GameFormulas.UnlockLogic && _gm.CurrentPhase < GamePhase.Logic)
                _gm.AdvancePhase(GamePhase.Logic);
            if (energy >= GameFormulas.UnlockAI && _gm.CurrentPhase < GamePhase.AI)
                _gm.AdvancePhase(GamePhase.AI);
            if (energy >= GameFormulas.UnlockSimulation && _gm.CurrentPhase < GamePhase.Simulation)
                _gm.AdvancePhase(GamePhase.Simulation);
            if (energy >= GameFormulas.UnlockMeta && _gm.CurrentPhase < GamePhase.Meta)
                _gm.AdvancePhase(GamePhase.Meta);
        }

        // ─── PUBLIC API ─────────────────────────────────────────────

        public void AddResource(ResourceData data)
        {
            _resources[data.Id] = data;
        }

        public BigNumber GetAmount(string resourceId)
        {
            return _resources.TryGetValue(resourceId, out var r) ? r.Amount : BigNumber.Zero;
        }

        public BigNumber GetPerSecond(string resourceId)
        {
            return _resources.TryGetValue(resourceId, out var r) ? r.PerSecond : BigNumber.Zero;
        }

        public void AddResource(string resourceId, BigNumber amount)
        {
            if (!_resources.TryGetValue(resourceId, out var res)) return;

            res.Amount = res.Amount + amount;
            res.TotalEarned = res.TotalEarned + amount;

            // Enforce capacity
            if (res.Capacity > BigNumber.Zero && res.Amount > res.Capacity)
                res.Amount = res.Capacity;

            OnResourceChanged?.Invoke(resourceId, res.Amount);
        }

        public bool SpendResource(string resourceId, BigNumber amount)
        {
            if (!_resources.TryGetValue(resourceId, out var res)) return false;
            if (res.Amount < amount) return false;

            res.Amount = res.Amount - amount;
            OnResourceChanged?.Invoke(resourceId, res.Amount);
            return true;
        }

        /// <summary>
        /// Player taps the screen. Produces energy based on clicker generator.
        /// </summary>
        public BigNumber ProcessTap()
        {
            var clicker = _generators[0];
            BigNumber amount = GameFormulas.GeneratorProduction(
                clicker.BaseProduction, clicker.Owned, clicker.Multiplier);

            AddResource("energy", amount);
            return amount;
        }

        /// <summary>
        /// Buy one unit of a generator.
        /// </summary>
        public bool BuyGenerator(string generatorId)
        {
            var gen = _generators.Find(g => g.Id == generatorId);
            if (gen == null) return false;

            BigNumber cost = gen.CurrentCost;
            if (!SpendResource("energy", cost)) return false;

            gen.Owned++;
            OnGeneratorPurchased?.Invoke(gen);
            return true;
        }

        /// <summary>
        /// Buy N generators at once (uses geometric sum for cost).
        /// </summary>
        public bool BuyGeneratorBulk(string generatorId, int count)
        {
            var gen = _generators.Find(g => g.Id == generatorId);
            if (gen == null || count <= 0) return false;

            // Geometric sum: baseCost * r^owned * (r^count - 1) / (r - 1)
            double r = gen.CostGrowth;
            double totalCost = gen.BaseCost * Math.Pow(r, gen.Owned) * (Math.Pow(r, count) - 1) / (r - 1);

            if (!SpendResource("energy", new BigNumber(totalCost))) return false;

            gen.Owned += count;
            OnGeneratorPurchased?.Invoke(gen);
            return true;
        }

        /// <summary>
        /// Buy one level of an upgrade.
        /// </summary>
        public bool BuyUpgrade(string upgradeId)
        {
            var upg = _upgrades.Find(u => u.Id == upgradeId);
            if (upg == null || upg.IsMaxed) return false;

            BigNumber cost = upg.CurrentCost;
            if (!SpendResource(upg.CostResource, cost)) return false;

            upg.Level++;

            // Apply effect
            if (!string.IsNullOrEmpty(upg.TargetGenerator))
            {
                var gen = _generators.Find(g => g.Id == upg.TargetGenerator);
                if (gen != null)
                    gen.Multiplier += upg.MultiplierPerLevel;
            }

            // Special: data_storage increases Data capacity
            if (upg.Id == "data_storage")
            {
                if (_resources.TryGetValue("data", out var dataRes))
                    dataRes.Capacity = dataRes.Capacity + new BigNumber(500);
            }

            OnUpgradePurchased?.Invoke(upg);
            return true;
        }

        /// <summary>
        /// Apply a global multiplier to all generators (e.g., from prestige or boosts).
        /// </summary>
        public void ApplyGlobalMultiplier(double multiplier)
        {
            for (int i = 0; i < _generators.Count; i++)
                _generators[i].Multiplier *= multiplier;
        }

        public GeneratorData GetGenerator(string id) => _generators.Find(g => g.Id == id);
    }
}
