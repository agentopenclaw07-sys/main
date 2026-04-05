using System;

namespace Evolve.Core
{
    /// <summary>
    /// Central formula library. All game math lives here for easy tuning.
    /// Every formula is deterministic and uses only primitive inputs.
    /// </summary>
    public static class GameFormulas
    {
        // ─── UPGRADE COSTS ───────────────────────────────────────────
        // Standard exponential: baseCost * growthRate^level
        public static BigNumber UpgradeCost(double baseCost, double growthRate, int level)
        {
            return new BigNumber(baseCost * Math.Pow(growthRate, level));
        }

        // Polynomial softcap: baseCost * (level+1)^exponent (slower growth)
        public static BigNumber SoftCost(double baseCost, double exponent, int level)
        {
            return new BigNumber(baseCost * Math.Pow(level + 1, exponent));
        }

        // ─── PRODUCTION RATES ────────────────────────────────────────
        // Base production: baseRate * level * (1 + bonusMultiplier)
        public static BigNumber ProductionRate(double baseRate, int level, double bonusMultiplier)
        {
            return new BigNumber(baseRate * level * (1.0 + bonusMultiplier));
        }

        // Diminishing returns: baseRate * level^0.8
        public static BigNumber DiminishingProduction(double baseRate, int level)
        {
            return new BigNumber(baseRate * Math.Pow(level, 0.8));
        }

        // ─── PRESTIGE ───────────────────────────────────────────────
        // Prestige currency earned: floor(150 * sqrt(lifetimeEarnings / 1e10))
        public static BigNumber PrestigeCurrency(BigNumber lifetimeEarnings)
        {
            if (lifetimeEarnings.Value < 1e10) return BigNumber.Zero;
            return new BigNumber(Math.Floor(150.0 * Math.Sqrt(lifetimeEarnings.Value / 1e10)));
        }

        // Prestige multiplier: 1 + (prestigePoints * 0.01) ^ 0.5
        public static double PrestigeMultiplier(BigNumber prestigePoints)
        {
            return 1.0 + Math.Pow(prestigePoints.Value * 0.01, 0.5);
        }

        // ─── UNLOCK THRESHOLDS ──────────────────────────────────────
        public static readonly double UnlockAutomation = 1_000;       // 1K energy
        public static readonly double UnlockLogic = 100_000;          // 100K energy
        public static readonly double UnlockAI = 10_000_000;          // 10M energy
        public static readonly double UnlockSimulation = 1_000_000_000; // 1B energy
        public static readonly double UnlockMeta = 1e12;              // 1T energy

        // ─── GENERATOR SCALING ──────────────────────────────────────
        // Generator N unlocks at: 10^(N+1) energy
        public static double GeneratorUnlockCost(int generatorIndex)
        {
            return Math.Pow(10, generatorIndex + 1);
        }

        // Generator base cost: 10^(N+1) * 1.15^owned
        public static BigNumber GeneratorCost(int genIndex, int owned)
        {
            double baseCost = Math.Pow(10, genIndex + 1);
            return new BigNumber(baseCost * Math.Pow(1.15, owned));
        }

        // Generator production: baseRate * owned * multiplier
        // Each generator produces the currency below it
        public static BigNumber GeneratorProduction(double baseRate, int owned, double multiplier)
        {
            return new BigNumber(baseRate * owned * multiplier);
        }

        // ─── WORKER EFFICIENCY ──────────────────────────────────────
        // Workers get tired: efficiency = 1.0 - (0.01 * hoursWorked), min 0.3
        public static double WorkerEfficiency(double hoursWorked)
        {
            return Math.Max(0.3, 1.0 - 0.01 * hoursWorked);
        }

        // ─── AI UTILITY SCORING ─────────────────────────────────────
        // Normalized utility: weight * (currentValue / targetValue)
        public static double UtilityScore(double weight, double current, double target)
        {
            if (target <= 0) return 0;
            return weight * Math.Min(current / target, 2.0); // cap at 2x
        }

        // ─── SIMULATION ─────────────────────────────────────────────
        // Market price fluctuation: basePrice * (1 + sin(time * frequency) * amplitude)
        public static double MarketPrice(double basePrice, double time, double frequency, double amplitude)
        {
            return basePrice * (1.0 + Math.Sin(time * frequency) * amplitude);
        }

        // Population growth: current * (1 + growthRate * (1 - current/capacity))
        public static double PopulationGrowth(double current, double capacity, double growthRate)
        {
            if (capacity <= 0) return current;
            return current * (1.0 + growthRate * (1.0 - current / capacity));
        }
    }
}
