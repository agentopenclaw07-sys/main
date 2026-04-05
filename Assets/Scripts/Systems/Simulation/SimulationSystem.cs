using System;
using System.Collections.Generic;
using Evolve.Core;
using UnityEngine;

namespace Evolve.Simulation
{
    // ═══════════════════════════════════════════════════════════════════
    // SIMULATION SYSTEM — World-scale economy with entities
    // ═══════════════════════════════════════════════════════════════════
    // Unlocks at: 1,000,000,000 energy (GamePhase.Simulation)
    //
    // Adds a macro layer: businesses, markets, populations.
    // The player's empire now operates within an economy.
    //
    // Architecture:
    //   - Entity-Component approach: entities are structs in arrays (SoA)
    //   - Tick-based with batch processing
    //   - Market prices fluctuate based on supply/demand
    //   - Businesses produce/consume resources
    //   - Population grows and provides workers
    //
    // Performance:
    //   - Entities stored in flat arrays (cache-friendly)
    //   - Batch updates: process all businesses, then all markets, etc.
    //   - LOD system: far-away entities update less frequently
    //   - Max 10,000 entities with culling at 5,000
    // ═══════════════════════════════════════════════════════════════════

    [Serializable]
    public struct BusinessEntity
    {
        public int Id;
        public string Type;            // "mine", "factory", "lab", "shop"
        public string InputResource;
        public string OutputResource;
        public double ProductionRate;   // units per second
        public double ConsumptionRate;
        public int WorkerSlots;
        public int AssignedWorkers;
        public double Efficiency;       // 0-1, affected by workers and upgrades
        public int Level;
        public bool Active;
    }

    [Serializable]
    public struct MarketData
    {
        public string ResourceId;
        public double BasePrice;
        public double CurrentPrice;
        public double Supply;
        public double Demand;
        public double Volatility;       // 0-1, how much price swings
        public double PriceHistory;     // rolling average for trend
    }

    [Serializable]
    public struct PopulationData
    {
        public double Current;
        public double Capacity;
        public double GrowthRate;
        public double Happiness;        // 0-1, affects growth
        public double AvailableWorkers;
        public double EmployedWorkers;
    }

    public class SimulationSystem : IGameSystem
    {
        public bool IsActive => GameManager.Instance != null
            && GameManager.Instance.CurrentPhase >= GamePhase.Simulation;

        private GameManager _gm;
        private ResourceSystem _resources;

        // Entity storage (SoA-style flat arrays for cache performance)
        private BusinessEntity[] _businesses;
        private int _businessCount;
        private const int MAX_BUSINESSES = 10_000;

        private readonly Dictionary<string, MarketData> _markets = new();
        private PopulationData _population;

        private double _simTickTimer;
        private const double SIM_TICK_INTERVAL = 1.0; // 1 Hz for simulation
        private int _simTick;

        public PopulationData Population => _population;
        public IReadOnlyDictionary<string, MarketData> Markets => _markets;
        public int BusinessCount => _businessCount;

        public event Action OnSimulationTick;
        public event Action<string, double> OnPriceChanged;

        public void Initialize(GameManager gm)
        {
            _gm = gm;
            _resources = gm.GetSystem<ResourceSystem>();

            _businesses = new BusinessEntity[MAX_BUSINESSES];
            _businessCount = 0;

            SetupMarkets();
            SetupPopulation();
        }

        private void SetupMarkets()
        {
            AddMarket("energy", 1.0, 0.2);
            AddMarket("data", 5.0, 0.3);
            AddMarket("credits", 1.0, 0.05); // stable
            AddMarket("research", 20.0, 0.4);
        }

        private void AddMarket(string resourceId, double basePrice, double volatility)
        {
            _markets[resourceId] = new MarketData
            {
                ResourceId = resourceId,
                BasePrice = basePrice,
                CurrentPrice = basePrice,
                Volatility = volatility,
            };
        }

        private void SetupPopulation()
        {
            _population = new PopulationData
            {
                Current = 100,
                Capacity = 1000,
                GrowthRate = 0.001,
                Happiness = 0.8,
                AvailableWorkers = 50,
            };
        }

        public void Tick(double delta)
        {
            _simTickTimer += delta;
            if (_simTickTimer < SIM_TICK_INTERVAL) return;
            _simTickTimer -= SIM_TICK_INTERVAL;
            _simTick++;

            UpdateMarkets();
            UpdateBusinesses(SIM_TICK_INTERVAL);
            UpdatePopulation(SIM_TICK_INTERVAL);

            OnSimulationTick?.Invoke();
        }

        // ─── MARKETS ────────────────────────────────────────────────

        private void UpdateMarkets()
        {
            var keys = new List<string>(_markets.Keys);
            foreach (var key in keys)
            {
                var market = _markets[key];

                // Supply/demand from businesses
                double supplyPressure = market.Supply - market.Demand;
                double priceDelta = -supplyPressure * 0.001 * market.Volatility;

                // Add sine-wave fluctuation for organic feel
                double wave = Math.Sin(_simTick * 0.1 + key.GetHashCode()) * market.Volatility * 0.1;

                market.CurrentPrice = market.BasePrice + priceDelta + wave;
                market.CurrentPrice = Math.Max(market.BasePrice * 0.1, market.CurrentPrice); // floor

                // Decay supply/demand toward 0
                market.Supply *= 0.95;
                market.Demand *= 0.95;

                // Rolling average
                market.PriceHistory = market.PriceHistory * 0.9 + market.CurrentPrice * 0.1;

                _markets[key] = market;
                OnPriceChanged?.Invoke(key, market.CurrentPrice);
            }
        }

        // ─── BUSINESSES ─────────────────────────────────────────────

        private void UpdateBusinesses(double delta)
        {
            for (int i = 0; i < _businessCount; i++)
            {
                ref var biz = ref _businesses[i];
                if (!biz.Active) continue;

                // LOD: lower-tier businesses update less often at scale
                if (_businessCount > 5000 && biz.Level < 3 && _simTick % 5 != 0)
                    continue;

                // Calculate efficiency based on worker fill
                double workerFill = biz.WorkerSlots > 0
                    ? (double)biz.AssignedWorkers / biz.WorkerSlots
                    : 1.0;
                biz.Efficiency = Math.Max(0.1, workerFill);

                // Consume input
                if (!string.IsNullOrEmpty(biz.InputResource))
                {
                    BigNumber consumed = new(biz.ConsumptionRate * biz.Efficiency * delta);
                    if (!_resources.SpendResource(biz.InputResource, consumed))
                    {
                        biz.Efficiency *= 0.5; // starved
                    }

                    // Register demand on market
                    if (_markets.TryGetValue(biz.InputResource, out var inMarket))
                    {
                        inMarket.Demand += biz.ConsumptionRate * biz.Efficiency;
                        _markets[biz.InputResource] = inMarket;
                    }
                }

                // Produce output
                BigNumber produced = new(biz.ProductionRate * biz.Efficiency * biz.Level * delta);
                _resources.AddResource(biz.OutputResource, produced);

                // Register supply on market
                if (_markets.TryGetValue(biz.OutputResource, out var outMarket))
                {
                    outMarket.Supply += biz.ProductionRate * biz.Efficiency * biz.Level;
                    _markets[biz.OutputResource] = outMarket;
                }
            }
        }

        // ─── POPULATION ─────────────────────────────────────────────

        private void UpdatePopulation(double delta)
        {
            // Logistic growth with happiness modifier
            double effectiveGrowth = _population.GrowthRate * _population.Happiness;
            double newPop = GameFormulas.PopulationGrowth(
                _population.Current, _population.Capacity, effectiveGrowth);

            _population.Current = Math.Min(newPop, _population.Capacity);
            _population.AvailableWorkers = _population.Current * 0.5 - _population.EmployedWorkers;
            _population.AvailableWorkers = Math.Max(0, _population.AvailableWorkers);
        }

        // ─── PUBLIC API ─────────────────────────────────────────────

        public int CreateBusiness(string type, string input, string output,
            double prodRate, double consRate, int workerSlots, int level = 1)
        {
            if (_businessCount >= MAX_BUSINESSES) return -1;

            int id = _businessCount;
            _businesses[_businessCount++] = new BusinessEntity
            {
                Id = id,
                Type = type,
                InputResource = input,
                OutputResource = output,
                ProductionRate = prodRate,
                ConsumptionRate = consRate,
                WorkerSlots = workerSlots,
                Level = level,
                Active = true,
            };

            return id;
        }

        public bool AssignWorkerToBusiness(int businessId)
        {
            if (businessId < 0 || businessId >= _businessCount) return false;
            ref var biz = ref _businesses[businessId];

            if (biz.AssignedWorkers >= biz.WorkerSlots) return false;
            if (_population.AvailableWorkers < 1) return false;

            biz.AssignedWorkers++;
            _population.AvailableWorkers--;
            _population.EmployedWorkers++;
            return true;
        }

        public void UpgradeBusiness(int businessId)
        {
            if (businessId < 0 || businessId >= _businessCount) return;
            _businesses[businessId].Level++;
        }

        public double GetMarketPrice(string resourceId)
        {
            return _markets.TryGetValue(resourceId, out var m) ? m.CurrentPrice : 0;
        }

        /// <summary>
        /// Sell resource on the market at current price.
        /// </summary>
        public bool MarketSell(string resourceId, double amount)
        {
            if (!_markets.TryGetValue(resourceId, out var market)) return false;
            if (!_resources.SpendResource(resourceId, new BigNumber(amount))) return false;

            double revenue = amount * market.CurrentPrice;
            _resources.AddResource("credits", new BigNumber(revenue));

            // Increase supply (depresses price)
            var m = _markets[resourceId];
            m.Supply += amount;
            _markets[resourceId] = m;

            return true;
        }

        /// <summary>
        /// Buy resource from market at current price.
        /// </summary>
        public bool MarketBuy(string resourceId, double amount)
        {
            if (!_markets.TryGetValue(resourceId, out var market)) return false;

            double cost = amount * market.CurrentPrice;
            if (!_resources.SpendResource("credits", new BigNumber(cost))) return false;

            _resources.AddResource(resourceId, new BigNumber(amount));

            // Increase demand (raises price)
            var m = _markets[resourceId];
            m.Demand += amount;
            _markets[resourceId] = m;

            return true;
        }

        public BusinessEntity GetBusiness(int id)
        {
            if (id < 0 || id >= _businessCount) return default;
            return _businesses[id];
        }

        public void ExpandPopulationCapacity(double amount)
        {
            _population.Capacity += amount;
        }
    }
}
