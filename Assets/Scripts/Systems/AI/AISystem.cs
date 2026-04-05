using System;
using System.Collections.Generic;
using Evolve.Core;
using UnityEngine;

namespace Evolve.AI
{
    // ═══════════════════════════════════════════════════════════════════
    // AI SYSTEM — Self-optimizing agents with utility-based decisions
    // ═══════════════════════════════════════════════════════════════════
    // Unlocks at: 10,000,000 energy (GamePhase.AI)
    //
    // Players deploy AI agents that autonomously optimize the empire.
    // Each agent uses a utility system to score possible actions and
    // picks the best one each evaluation cycle.
    //
    // Architecture:
    //   AIAgent has a personality (weights) and a set of possible actions.
    //   Each tick, the agent scores all actions via utility functions,
    //   then executes the highest-scoring action.
    //
    //   Agents learn: they track outcomes and adjust weights over time
    //   using a simple reinforcement signal (did the action increase
    //   total production? yes → increase weight, no → decrease).
    //
    // Scaling: Agents are lightweight. 10-50 agents with 5-15 actions each
    //          is trivial to evaluate. Agents share a pooled action set.
    // ═══════════════════════════════════════════════════════════════════

    public enum AIActionType
    {
        BuyBestGenerator,    // buy the most cost-efficient generator
        BuyCheapestGenerator,// buy whatever is cheapest
        UpgradeBest,         // buy the highest-impact upgrade
        RebalanceWorkers,    // reassign workers for max output
        SellSurplus,         // sell excess resources
        SaveForTarget,       // hold resources until a target is met
        OptimizeRules,       // enable/disable logic rules based on state
        ExpandCapacity,      // buy storage upgrades
    }

    [Serializable]
    public class AIActionOption
    {
        public AIActionType Type;
        public double Weight;          // base utility weight (tunable)
        public double LearnedBias;     // adjusted by reinforcement
        public int TimesChosen;
        public int TimesSuccessful;

        public double EffectiveWeight => Weight + LearnedBias;
    }

    [Serializable]
    public class AIAgentData
    {
        public string Id;
        public string Name;
        public bool Active;
        public double EvalInterval;    // seconds between decisions
        public double LastEvalTime;

        // Personality weights (normalized 0-1)
        public double AggressionWeight;    // prefers spending
        public double EfficiencyWeight;    // prefers best ROI
        public double GrowthWeight;        // prefers long-term gains
        public double StabilityWeight;     // prefers safe actions

        public List<AIActionOption> Actions = new();

        // Tracking for learning
        public double PreActionProductionRate;
        public AIActionType LastAction;
    }

    public class AISystem : IGameSystem
    {
        public bool IsActive => GameManager.Instance != null
            && GameManager.Instance.CurrentPhase >= GamePhase.AI;

        private GameManager _gm;
        private ResourceSystem _resources;
        private Automation.AutomationSystem _automation;

        private readonly List<AIAgentData> _agents = new();
        private int _maxAgents = 3;
        private const double LEARNING_RATE = 0.05;

        public IReadOnlyList<AIAgentData> Agents => _agents;
        public int MaxAgents => _maxAgents;

        public event Action<AIAgentData, AIActionType> OnAgentActed;

        public void Initialize(GameManager gm)
        {
            _gm = gm;
            _resources = gm.GetSystem<ResourceSystem>();
            _automation = gm.GetSystem<Automation.AutomationSystem>();
        }

        public void Tick(double delta)
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                var agent = _agents[i];
                if (!agent.Active) continue;

                if (_gm.GameTime - agent.LastEvalTime >= agent.EvalInterval)
                {
                    // Learn from previous action
                    LearnFromOutcome(agent);

                    // Make new decision
                    EvaluateAndAct(agent);

                    agent.LastEvalTime = _gm.GameTime;
                }
            }
        }

        private void EvaluateAndAct(AIAgentData agent)
        {
            // Record current state for learning
            agent.PreActionProductionRate = _resources.GetPerSecond("energy").Value;

            // Score all actions
            AIActionOption bestAction = null;
            double bestScore = double.MinValue;

            for (int i = 0; i < agent.Actions.Count; i++)
            {
                double score = ScoreAction(agent, agent.Actions[i]);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestAction = agent.Actions[i];
                }
            }

            if (bestAction == null) return;

            // Execute
            bool success = ExecuteAction(bestAction.Type);
            bestAction.TimesChosen++;
            if (success) bestAction.TimesSuccessful++;

            agent.LastAction = bestAction.Type;
            OnAgentActed?.Invoke(agent, bestAction.Type);
        }

        private double ScoreAction(AIAgentData agent, AIActionOption action)
        {
            double score = action.EffectiveWeight;
            double energy = _resources.GetAmount("energy").Value;
            double production = _resources.GetPerSecond("energy").Value;

            switch (action.Type)
            {
                case AIActionType.BuyBestGenerator:
                    // Higher score when we have lots of energy and want growth
                    score += agent.GrowthWeight * 0.5;
                    score += agent.EfficiencyWeight * 0.3;
                    if (energy > production * 30) score += 0.5; // can afford it
                    break;

                case AIActionType.BuyCheapestGenerator:
                    score += agent.AggressionWeight * 0.5;
                    if (energy > 100) score += 0.3;
                    break;

                case AIActionType.UpgradeBest:
                    score += agent.EfficiencyWeight * 0.5;
                    score += agent.GrowthWeight * 0.3;
                    break;

                case AIActionType.RebalanceWorkers:
                    score += agent.EfficiencyWeight * 0.4;
                    score += agent.StabilityWeight * 0.3;
                    break;

                case AIActionType.SellSurplus:
                    score += agent.StabilityWeight * 0.3;
                    // Score higher when resources near capacity
                    var dataRes = _resources.Resources.ContainsKey("data")
                        ? _resources.Resources["data"] : null;
                    if (dataRes != null && dataRes.Capacity > BigNumber.Zero)
                    {
                        double fillPercent = dataRes.Amount.Value / dataRes.Capacity.Value;
                        if (fillPercent > 0.8) score += 1.0;
                    }
                    break;

                case AIActionType.SaveForTarget:
                    score += agent.StabilityWeight * 0.5;
                    if (energy < production * 60) score += 0.5; // saving up
                    break;

                case AIActionType.ExpandCapacity:
                    score += agent.GrowthWeight * 0.3;
                    break;
            }

            // Add noise for exploration (epsilon-greedy style)
            score += UnityEngine.Random.Range(0f, 0.1f);

            return score;
        }

        private bool ExecuteAction(AIActionType actionType)
        {
            switch (actionType)
            {
                case AIActionType.BuyBestGenerator:
                    return BuyBestROIGenerator();

                case AIActionType.BuyCheapestGenerator:
                    return BuyCheapestGenerator();

                case AIActionType.UpgradeBest:
                    return BuyBestUpgrade();

                case AIActionType.SellSurplus:
                    return SellSurplusResources();

                case AIActionType.RebalanceWorkers:
                    // Would rebalance workers — simplified here
                    return false;

                case AIActionType.SaveForTarget:
                    return true; // "doing nothing" is the action

                default:
                    return false;
            }
        }

        private bool BuyBestROIGenerator()
        {
            // Find generator with best production/cost ratio
            double bestRatio = 0;
            string bestId = null;

            foreach (var gen in _resources.Generators)
            {
                if (gen.Tier == 0) continue;
                double cost = gen.CurrentCost.Value;
                if (cost <= 0) continue;

                double productionGain = gen.BaseProduction * gen.Multiplier;
                double ratio = productionGain / cost;

                if (ratio > bestRatio && _resources.GetAmount("energy") >= gen.CurrentCost)
                {
                    bestRatio = ratio;
                    bestId = gen.Id;
                }
            }

            return bestId != null && _resources.BuyGenerator(bestId);
        }

        private bool BuyCheapestGenerator()
        {
            BigNumber cheapest = new BigNumber(double.MaxValue);
            string cheapestId = null;

            foreach (var gen in _resources.Generators)
            {
                if (gen.Tier == 0) continue;
                if (gen.CurrentCost < cheapest)
                {
                    cheapest = gen.CurrentCost;
                    cheapestId = gen.Id;
                }
            }

            return cheapestId != null && _resources.BuyGenerator(cheapestId);
        }

        private bool BuyBestUpgrade()
        {
            foreach (var upg in _resources.Upgrades)
            {
                if (upg.IsMaxed) continue;
                if (_resources.GetAmount(upg.CostResource) >= upg.CurrentCost)
                {
                    return _resources.BuyUpgrade(upg.Id);
                }
            }
            return false;
        }

        private bool SellSurplusResources()
        {
            // Sell energy for credits if we have a lot
            BigNumber energy = _resources.GetAmount("energy");
            BigNumber production = _resources.GetPerSecond("energy");

            // Sell if we have more than 2 minutes of production stored
            if (energy.Value > production.Value * 120)
            {
                BigNumber sellAmount = energy * 0.1; // sell 10%
                if (_resources.SpendResource("energy", sellAmount))
                {
                    _resources.AddResource("credits", sellAmount * 0.01);
                    return true;
                }
            }
            return false;
        }

        private void LearnFromOutcome(AIAgentData agent)
        {
            if (agent.Actions.Count == 0) return;

            double currentProduction = _resources.GetPerSecond("energy").Value;
            double delta = currentProduction - agent.PreActionProductionRate;

            // Find the action that was last taken
            var lastAction = agent.Actions.Find(a => a.Type == agent.LastAction);
            if (lastAction == null) return;

            // Positive reinforcement if production increased
            if (delta > 0)
                lastAction.LearnedBias += LEARNING_RATE;
            else if (delta < 0)
                lastAction.LearnedBias -= LEARNING_RATE * 0.5; // punish less

            // Clamp bias
            lastAction.LearnedBias = Math.Clamp(lastAction.LearnedBias, -1.0, 1.0);
        }

        // ─── PUBLIC API ─────────────────────────────────────────────

        public AIAgentData CreateAgent(string name, double aggression = 0.5,
            double efficiency = 0.5, double growth = 0.5, double stability = 0.5)
        {
            if (_agents.Count >= _maxAgents) return null;

            var agent = new AIAgentData
            {
                Id = $"ai_{_agents.Count}",
                Name = name,
                Active = true,
                EvalInterval = 5.0, // decide every 5 seconds
                AggressionWeight = aggression,
                EfficiencyWeight = efficiency,
                GrowthWeight = growth,
                StabilityWeight = stability,
                Actions = new List<AIActionOption>
                {
                    new() { Type = AIActionType.BuyBestGenerator, Weight = 1.0 },
                    new() { Type = AIActionType.BuyCheapestGenerator, Weight = 0.8 },
                    new() { Type = AIActionType.UpgradeBest, Weight = 0.9 },
                    new() { Type = AIActionType.SellSurplus, Weight = 0.5 },
                    new() { Type = AIActionType.SaveForTarget, Weight = 0.3 },
                    new() { Type = AIActionType.RebalanceWorkers, Weight = 0.6 },
                }
            };

            _agents.Add(agent);
            return agent;
        }

        public void SetAgentActive(string agentId, bool active)
        {
            var agent = _agents.Find(a => a.Id == agentId);
            if (agent != null) agent.Active = active;
        }

        public void UpgradeMaxAgents(int additional) => _maxAgents += additional;
    }
}
