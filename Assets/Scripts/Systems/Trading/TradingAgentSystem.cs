using System;
using System.Collections.Generic;
using System.Linq;
using Evolve.Core;

namespace Evolve.Trading
{
    // ═══════════════════════════════════════════════════════════════════
    // TRADING AGENT SYSTEM — Autonomous market traders with RL learning
    // ═══════════════════════════════════════════════════════════════════
    // Unlocks at: 1,000,000,000 energy (GamePhase.Simulation)
    //
    // Trading agents autonomously buy and sell resources on the market
    // using multiple strategies. Each strategy's weight is adjusted via
    // reinforcement learning based on realized profit/loss.
    //
    // Strategies:
    //   MeanReversion  — buy when price < long-MA, sell when price > long-MA
    //   Momentum       — buy on MA crossover up, sell on crossover down
    //   Arbitrage      — buy when current price < base price, sell when above
    //   ValueBased     — trade based on supply/demand imbalances
    //   VolatilityPlay — aggressive trades in high-volatility markets
    //
    // Learning:
    //   After every closed position, the realized P&L (as % return) is used
    //   to update the responsible strategy's LearnedBias via gradient signal.
    //   Strategies that generate positive returns gain weight; losers shrink.
    //
    // Risk management:
    //   • Max 25% of allocated credits per position
    //   • Stop-loss at -15%, take-profit at +20%
    //   • Total portfolio exposure capped at 70%
    // ═══════════════════════════════════════════════════════════════════

    public enum TradingStrategyType
    {
        MeanReversion,
        Momentum,
        Arbitrage,
        ValueBased,
        VolatilityPlay,
    }

    [Serializable]
    public class TradingPosition
    {
        public string ResourceId;
        public double Amount;
        public double AvgBuyPrice;
        public double OpenTime;
        public TradingStrategyType Strategy;
        public double StopLossPrice;
        public double TakeProfitPrice;
    }

    [Serializable]
    public class TradeRecord
    {
        public bool IsBuy;
        public TradingStrategyType Strategy;
        public string ResourceId;
        public double Price;
        public double Amount;
        public double PnL;           // 0 for buys; realized P&L for sells
        public double Timestamp;
    }

    [Serializable]
    public class StrategyProfile
    {
        public TradingStrategyType Type;
        public double Weight;        // base utility weight
        public double LearnedBias;   // adjusted by reinforcement
        public int TradesWon;
        public int TotalTrades;
        public double TotalPnL;

        public double WinRate       => TotalTrades > 0 ? (double)TradesWon / TotalTrades : 0.5;
        public double AvgPnL        => TotalTrades > 0 ? TotalPnL / TotalTrades : 0;
        public double EffectiveWeight => Math.Max(0.05, Weight + LearnedBias);
    }

    [Serializable]
    public class TradingAgentData
    {
        public string Id;
        public string Name;
        public bool Active;
        public double EvalInterval;      // seconds between decision cycles
        public double LastEvalTime;
        public double CreditAllocation;  // fraction of available credits managed (0-1)

        public List<StrategyProfile> Strategies = new();
        public List<TradingPosition> OpenPositions = new();
        public List<TradeRecord> RecentTrades = new();

        public double TotalPnL;
        public int TotalTrades;
        public int WinCount;

        public double WinRate => TotalTrades > 0 ? (double)WinCount / TotalTrades : 0;
    }

    // Internal signal from strategy evaluation
    internal enum TradeSignal { Hold, Buy, Sell }
    internal record SignalResult(TradeSignal Signal, double Score);

    public class TradingAgentSystem : IGameSystem
    {
        public bool IsActive => GameManager.Instance != null
            && GameManager.Instance.CurrentPhase >= GamePhase.Simulation;

        private GameManager _gm;
        private ResourceSystem _resources;
        private Simulation.SimulationSystem _simulation;

        private readonly List<TradingAgentData> _agents = new();
        private int _maxAgents = 2;

        // ── Price history ──────────────────────────────────────────────
        private const int HISTORY_LEN   = 30;
        private const int SHORT_MA_PERIOD = 5;
        private const int LONG_MA_PERIOD  = 20;

        // Per resource: circular price buffer
        private readonly Dictionary<string, Queue<double>> _priceHistory = new();
        // Per resource: [0] = short MA, [1] = long MA
        private readonly Dictionary<string, double[]> _movingAverages = new();

        // ── Risk parameters ───────────────────────────────────────────
        private const double MAX_POSITION_PCT  = 0.25;  // max 25% credits per trade
        private const double STOP_LOSS_PCT     = 0.15;  // exit if down 15%
        private const double TAKE_PROFIT_PCT   = 0.20;  // exit if up 20%
        private const double MAX_EXPOSURE_PCT  = 0.70;  // max 70% deployed
        private const double MIN_SIGNAL_SCORE  = 0.30;  // ignore weak signals
        private const double LEARNING_RATE     = 0.08;

        private static readonly string[] TRADEABLE = { "energy", "data", "research" };

        // ── Public surface ────────────────────────────────────────────
        public IReadOnlyList<TradingAgentData> Agents => _agents;
        public int MaxAgents => _maxAgents;

        public event Action<TradingAgentData, TradeRecord> OnTradeExecuted;

        // ─────────────────────────────────────────────────────────────
        // LIFECYCLE
        // ─────────────────────────────────────────────────────────────

        public void Initialize(GameManager gm)
        {
            _gm        = gm;
            _resources = gm.GetSystem<ResourceSystem>();
            _simulation = gm.GetSystem<Simulation.SimulationSystem>();

            foreach (var id in new[] { "energy", "data", "credits", "research" })
            {
                _priceHistory[id]    = new Queue<double>(HISTORY_LEN);
                _movingAverages[id]  = new double[2];
            }

            _simulation.OnPriceChanged += RecordPrice;
        }

        public void Tick(double delta)
        {
            if (!IsActive) return;

            // Auto-spawn first agent when simulation phase is reached
            if (_agents.Count == 0)
                CreateAgent("Quant-Alpha", creditAllocation: 0.30);

            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (!agent.Active) continue;

                if (_gm.GameTime - agent.LastEvalTime >= agent.EvalInterval)
                {
                    CheckExits(agent);
                    EvaluateEntries(agent);
                    agent.LastEvalTime = _gm.GameTime;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────
        // PRICE HISTORY & MOVING AVERAGES
        // ─────────────────────────────────────────────────────────────

        private void RecordPrice(string resourceId, double price)
        {
            if (!_priceHistory.TryGetValue(resourceId, out var q)) return;

            q.Enqueue(price);
            if (q.Count > HISTORY_LEN) q.Dequeue();

            UpdateMovingAverages(resourceId, q);
        }

        private void UpdateMovingAverages(string resourceId, Queue<double> q)
        {
            double[] prices = q.ToArray();
            int n = prices.Length;

            double[] ma = _movingAverages[resourceId];

            if (n >= SHORT_MA_PERIOD)
            {
                double s = 0;
                for (int i = n - SHORT_MA_PERIOD; i < n; i++) s += prices[i];
                ma[0] = s / SHORT_MA_PERIOD;
            }
            if (n >= LONG_MA_PERIOD)
            {
                double s = 0;
                for (int i = n - LONG_MA_PERIOD; i < n; i++) s += prices[i];
                ma[1] = s / LONG_MA_PERIOD;
            }
        }

        // ─────────────────────────────────────────────────────────────
        // EXIT LOGIC — stop-loss and take-profit checks
        // ─────────────────────────────────────────────────────────────

        private void CheckExits(TradingAgentData agent)
        {
            for (int i = agent.OpenPositions.Count - 1; i >= 0; i--)
            {
                var pos = agent.OpenPositions[i];
                double current = _simulation.GetMarketPrice(pos.ResourceId);

                if (current <= pos.StopLossPrice || current >= pos.TakeProfitPrice)
                    ClosePosition(agent, i, current);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // ENTRY LOGIC — evaluate strategies and open positions
        // ─────────────────────────────────────────────────────────────

        private void EvaluateEntries(TradingAgentData agent)
        {
            foreach (string resourceId in TRADEABLE)
            {
                // Skip if we already have a position in this market
                if (agent.OpenPositions.Any(p => p.ResourceId == resourceId)) continue;

                StrategyProfile winner = null;
                double          bestScore = 0;

                foreach (var strat in agent.Strategies)
                {
                    var result = ScoreStrategy(strat, resourceId);
                    if (result.Signal != TradeSignal.Buy) continue;

                    double weighted = result.Score * strat.EffectiveWeight;
                    if (weighted > bestScore)
                    {
                        bestScore = weighted;
                        winner    = strat;
                    }
                }

                if (winner != null && bestScore >= MIN_SIGNAL_SCORE)
                    TryOpenPosition(agent, resourceId, winner);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // STRATEGY SCORING
        // ─────────────────────────────────────────────────────────────

        private SignalResult ScoreStrategy(StrategyProfile strat, string resourceId)
        {
            if (!_simulation.Markets.TryGetValue(resourceId, out var market))
                return new SignalResult(TradeSignal.Hold, 0);

            double[] ma = _movingAverages[resourceId];
            double shortMA = ma[0];
            double longMA  = ma[1];

            // Need enough price history before trading
            if (shortMA <= 0 || longMA <= 0)
                return new SignalResult(TradeSignal.Hold, 0);

            double price     = market.CurrentPrice;
            double basePrice = market.BasePrice;

            switch (strat.Type)
            {
                case TradingStrategyType.MeanReversion:
                {
                    // Buy when price has fallen significantly below long-term average (mean-revert up)
                    double deviation = (price - longMA) / longMA;
                    if (deviation < -0.05)
                        return new SignalResult(TradeSignal.Buy, Math.Abs(deviation) * 3.0);
                    if (deviation > 0.05)
                        return new SignalResult(TradeSignal.Sell, deviation * 3.0);
                    return new SignalResult(TradeSignal.Hold, 0);
                }

                case TradingStrategyType.Momentum:
                {
                    // Buy on golden cross: short MA crosses above long MA
                    double crossover = (shortMA - longMA) / longMA;
                    if (crossover > 0.02)
                        return new SignalResult(TradeSignal.Buy, crossover * 5.0);
                    if (crossover < -0.02)
                        return new SignalResult(TradeSignal.Sell, Math.Abs(crossover) * 5.0);
                    return new SignalResult(TradeSignal.Hold, 0);
                }

                case TradingStrategyType.Arbitrage:
                {
                    // Buy when current price is far below base (fair value)
                    double deviation = (price - basePrice) / basePrice;
                    if (deviation < -0.10)
                        return new SignalResult(TradeSignal.Buy, Math.Abs(deviation) * 4.0);
                    if (deviation > 0.10)
                        return new SignalResult(TradeSignal.Sell, deviation * 4.0);
                    return new SignalResult(TradeSignal.Hold, 0);
                }

                case TradingStrategyType.ValueBased:
                {
                    // Buy when demand is outpacing supply (prices should rise)
                    double ratio = market.Supply > 0 ? market.Demand / market.Supply : 2.0;
                    if (ratio > 1.5)
                        return new SignalResult(TradeSignal.Buy, (ratio - 1.0) * 0.5);
                    if (ratio < 0.5)
                        return new SignalResult(TradeSignal.Sell, 1.0 - ratio);
                    return new SignalResult(TradeSignal.Hold, 0);
                }

                case TradingStrategyType.VolatilityPlay:
                {
                    // In high-volatility markets, buy deep dips and sell spikes
                    double vol = market.Volatility;
                    if (vol < 0.20) return new SignalResult(TradeSignal.Hold, 0);

                    double threshold = 0.08 * (vol / 0.30); // scale with volatility
                    double deviation = (price - longMA) / longMA;
                    if (deviation < -threshold)
                        return new SignalResult(TradeSignal.Buy, vol * Math.Abs(deviation) * 3.0);
                    if (deviation > threshold)
                        return new SignalResult(TradeSignal.Sell, vol * deviation * 3.0);
                    return new SignalResult(TradeSignal.Hold, 0);
                }

                default:
                    return new SignalResult(TradeSignal.Hold, 0);
            }
        }

        // ─────────────────────────────────────────────────────────────
        // EXECUTION
        // ─────────────────────────────────────────────────────────────

        private void TryOpenPosition(TradingAgentData agent, string resourceId, StrategyProfile strat)
        {
            double credits   = _resources.GetAmount("credits").Value;
            double maxSpend  = credits * agent.CreditAllocation * MAX_POSITION_PCT;
            if (maxSpend < 1.0) return;

            // Enforce total exposure limit
            double exposed  = TotalExposure(agent);
            double maxTotal = credits * agent.CreditAllocation * MAX_EXPOSURE_PCT;
            if (exposed >= maxTotal) return;

            double price  = _simulation.GetMarketPrice(resourceId);
            if (price <= 0) return;

            double amount = maxSpend / price;
            if (amount < 0.01) return;

            if (!_simulation.MarketBuy(resourceId, amount)) return;

            var position = new TradingPosition
            {
                ResourceId     = resourceId,
                Amount         = amount,
                AvgBuyPrice    = price,
                OpenTime       = _gm.GameTime,
                Strategy       = strat.Type,
                StopLossPrice  = price * (1.0 - STOP_LOSS_PCT),
                TakeProfitPrice = price * (1.0 + TAKE_PROFIT_PCT),
            };
            agent.OpenPositions.Add(position);

            var record = new TradeRecord
            {
                IsBuy      = true,
                Strategy   = strat.Type,
                ResourceId = resourceId,
                Price      = price,
                Amount     = amount,
                Timestamp  = _gm.GameTime,
            };
            AppendRecord(agent, record);
            OnTradeExecuted?.Invoke(agent, record);
        }

        private void ClosePosition(TradingAgentData agent, int posIdx, double closePrice)
        {
            var pos = agent.OpenPositions[posIdx];
            if (!_simulation.MarketSell(pos.ResourceId, pos.Amount)) return;

            double pnl = (closePrice - pos.AvgBuyPrice) * pos.Amount;

            // ── Reinforcement learning update ──────────────────────────
            var strat = agent.Strategies.Find(s => s.Type == pos.Strategy);
            if (strat != null)
            {
                strat.TotalTrades++;
                strat.TotalPnL += pnl;
                if (pnl > 0) strat.TradesWon++;

                double normalizedReturn = pnl / (pos.AvgBuyPrice * pos.Amount);
                double signal = pnl > 0 ? normalizedReturn : normalizedReturn * 0.5; // punish losses less
                strat.LearnedBias = Math.Clamp(
                    strat.LearnedBias + LEARNING_RATE * signal, -1.5, 1.5);
            }

            agent.TotalPnL    += pnl;
            agent.TotalTrades++;
            if (pnl > 0) agent.WinCount++;

            var record = new TradeRecord
            {
                IsBuy      = false,
                Strategy   = pos.Strategy,
                ResourceId = pos.ResourceId,
                Price      = closePrice,
                Amount     = pos.Amount,
                PnL        = pnl,
                Timestamp  = _gm.GameTime,
            };
            AppendRecord(agent, record);
            OnTradeExecuted?.Invoke(agent, record);

            agent.OpenPositions.RemoveAt(posIdx);
        }

        private static void AppendRecord(TradingAgentData agent, TradeRecord record)
        {
            agent.RecentTrades.Add(record);
            if (agent.RecentTrades.Count > 50)
                agent.RecentTrades.RemoveAt(0);
        }

        private double TotalExposure(TradingAgentData agent)
        {
            double total = 0;
            foreach (var pos in agent.OpenPositions)
                total += pos.Amount * _simulation.GetMarketPrice(pos.ResourceId);
            return total;
        }

        // ─────────────────────────────────────────────────────────────
        // PUBLIC API
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Deploy a new trading agent. Returns null if at max capacity.
        /// </summary>
        public TradingAgentData CreateAgent(
            string name,
            double creditAllocation = 0.30,
            double evalInterval     = 10.0)
        {
            if (_agents.Count >= _maxAgents) return null;

            var agent = new TradingAgentData
            {
                Id               = $"trader_{_agents.Count}",
                Name             = name,
                Active           = true,
                EvalInterval     = evalInterval,
                CreditAllocation = creditAllocation,
                Strategies       = new List<StrategyProfile>
                {
                    new() { Type = TradingStrategyType.MeanReversion,  Weight = 1.0 },
                    new() { Type = TradingStrategyType.Momentum,        Weight = 0.8 },
                    new() { Type = TradingStrategyType.Arbitrage,       Weight = 0.9 },
                    new() { Type = TradingStrategyType.ValueBased,      Weight = 0.7 },
                    new() { Type = TradingStrategyType.VolatilityPlay,  Weight = 0.6 },
                },
            };

            _agents.Add(agent);
            return agent;
        }

        public void SetAgentActive(string agentId, bool active)
        {
            var a = _agents.Find(x => x.Id == agentId);
            if (a != null) a.Active = active;
        }

        /// <summary>
        /// Force-close all open positions for an agent (e.g. on prestige reset).
        /// </summary>
        public void LiquidateAll(TradingAgentData agent)
        {
            for (int i = agent.OpenPositions.Count - 1; i >= 0; i--)
            {
                double price = _simulation.GetMarketPrice(agent.OpenPositions[i].ResourceId);
                ClosePosition(agent, i, price);
            }
        }

        public void UpgradeMaxAgents(int additional) => _maxAgents += additional;

        public double[] GetMovingAverages(string resourceId)
            => _movingAverages.TryGetValue(resourceId, out var ma) ? ma : new double[2];

        public IEnumerable<double> GetPriceHistory(string resourceId)
            => _priceHistory.TryGetValue(resourceId, out var q) ? q : Enumerable.Empty<double>();
    }
}
