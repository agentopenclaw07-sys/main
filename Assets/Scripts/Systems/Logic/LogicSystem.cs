using System;
using System.Collections.Generic;
using Evolve.Core;
using UnityEngine;

namespace Evolve.Logic
{
    // ═══════════════════════════════════════════════════════════════════
    // LOGIC SYSTEM — Player-defined IF/THEN rules engine
    // ═══════════════════════════════════════════════════════════════════
    // Unlocks at: 100,000 energy (GamePhase.Logic)
    //
    // This is the game's core differentiator. Players build real logic:
    //   IF storage("energy") > 50000 THEN sell("energy", 10000)
    //   IF generator("reactor").count < 10 THEN buy("reactor")
    //   IF resource("data").perSecond < 100 THEN assignWorker("data")
    //
    // Architecture:
    //   Rule = Condition[] (AND) → Action[]
    //   Conditions evaluate numeric comparisons on game state
    //   Actions trigger game commands
    //   Rules evaluated in priority order, max N per tick
    //
    // Scaling: Rules are simple data. Evaluation is O(rules * conditions).
    //          Cap at 50 rules initially, expandable via upgrades.
    // ═══════════════════════════════════════════════════════════════════

    public enum ConditionType
    {
        ResourceAbove,       // resource amount > threshold
        ResourceBelow,       // resource amount < threshold
        ProductionAbove,     // per-second rate > threshold
        ProductionBelow,     // per-second rate < threshold
        GeneratorCountAbove, // owned count > threshold
        GeneratorCountBelow, // owned count < threshold
        StoragePercentAbove, // amount/capacity > threshold (0-1)
        StoragePercentBelow, // amount/capacity < threshold (0-1)
        PhaseAtLeast,        // current game phase >= value
        TimeSinceLastRun,    // seconds since this rule last fired > threshold
    }

    public enum ActionType
    {
        BuyGenerator,       // buy 1 or N of a generator
        SellResource,       // convert resource to credits
        AssignWorker,       // assign a worker to a task
        EnableAutoBuy,      // toggle auto-buy for a generator
        DisableAutoBuy,
        ActivateBoost,      // use a stored boost
        SwitchProduction,   // change what a facility produces
        SendNotification,   // alert the player
    }

    [Serializable]
    public class RuleCondition
    {
        public ConditionType Type;
        public string TargetId;   // resource/generator ID
        public double Threshold;

        public bool Evaluate(ResourceSystem resources, GameManager gm)
        {
            switch (Type)
            {
                case ConditionType.ResourceAbove:
                    return resources.GetAmount(TargetId).Value > Threshold;

                case ConditionType.ResourceBelow:
                    return resources.GetAmount(TargetId).Value < Threshold;

                case ConditionType.ProductionAbove:
                    return resources.GetPerSecond(TargetId).Value > Threshold;

                case ConditionType.ProductionBelow:
                    return resources.GetPerSecond(TargetId).Value < Threshold;

                case ConditionType.GeneratorCountAbove:
                {
                    var gen = resources.GetGenerator(TargetId);
                    return gen != null && gen.Owned > Threshold;
                }

                case ConditionType.GeneratorCountBelow:
                {
                    var gen = resources.GetGenerator(TargetId);
                    return gen != null && gen.Owned < Threshold;
                }

                case ConditionType.StoragePercentAbove:
                {
                    var res = resources.Resources[TargetId];
                    if (res.Capacity <= BigNumber.Zero) return true;
                    return (res.Amount / res.Capacity).Value > Threshold;
                }

                case ConditionType.StoragePercentBelow:
                {
                    var res = resources.Resources[TargetId];
                    if (res.Capacity <= BigNumber.Zero) return false;
                    return (res.Amount / res.Capacity).Value < Threshold;
                }

                case ConditionType.PhaseAtLeast:
                    return (int)gm.CurrentPhase >= (int)Threshold;

                default:
                    return false;
            }
        }
    }

    [Serializable]
    public class RuleAction
    {
        public ActionType Type;
        public string TargetId;   // generator/resource/worker ID
        public double Value;      // amount, count, etc.
        public string StringParam; // for complex params

        public void Execute(ResourceSystem resources, Automation.AutomationSystem automation)
        {
            switch (Type)
            {
                case ActionType.BuyGenerator:
                    int count = Math.Max(1, (int)Value);
                    if (count == 1) resources.BuyGenerator(TargetId);
                    else resources.BuyGeneratorBulk(TargetId, count);
                    break;

                case ActionType.SellResource:
                    // Sell resource for credits at a rate
                    BigNumber sellAmount = new(Value);
                    if (resources.SpendResource(TargetId, sellAmount))
                    {
                        BigNumber creditGain = sellAmount * 0.01; // 100:1 ratio
                        resources.AddResource("credits", creditGain);
                    }
                    break;

                case ActionType.AssignWorker:
                    automation?.AssignWorker(TargetId, StringParam);
                    break;

                case ActionType.EnableAutoBuy:
                    automation?.SetAutoBuyEnabled(TargetId, true);
                    break;

                case ActionType.DisableAutoBuy:
                    automation?.SetAutoBuyEnabled(TargetId, false);
                    break;

                case ActionType.SendNotification:
                    Debug.Log($"[Rule Notification] {StringParam}");
                    break;
            }
        }
    }

    [Serializable]
    public class LogicRule
    {
        public string Id;
        public string Name;             // player-defined name
        public int Priority;            // lower = runs first
        public bool Enabled;
        public List<RuleCondition> Conditions = new(); // ALL must be true (AND)
        public List<RuleAction> Actions = new();
        public double Cooldown;         // min seconds between firings
        public double LastFiredTime;    // game time when last fired

        public bool CanFire(double gameTime)
        {
            return Enabled && (gameTime - LastFiredTime) >= Cooldown;
        }
    }

    public class LogicSystem : IGameSystem
    {
        public bool IsActive => GameManager.Instance != null
            && GameManager.Instance.CurrentPhase >= GamePhase.Logic;

        private GameManager _gm;
        private ResourceSystem _resources;
        private Automation.AutomationSystem _automation;

        private readonly List<LogicRule> _rules = new();
        private double _evalTimer;
        private const double EVAL_INTERVAL = 0.5; // evaluate rules every 0.5s
        private int _maxRules = 20; // upgradeable

        public int MaxRules => _maxRules;
        public int RuleCount => _rules.Count;
        public IReadOnlyList<LogicRule> Rules => _rules;

        public event Action<LogicRule> OnRuleFired;
        public event Action<LogicRule> OnRuleAdded;

        public void Initialize(GameManager gm)
        {
            _gm = gm;
            _resources = gm.GetSystem<ResourceSystem>();
            _automation = gm.GetSystem<Automation.AutomationSystem>();
        }

        public void Tick(double delta)
        {
            _evalTimer += delta;
            if (_evalTimer < EVAL_INTERVAL) return;
            _evalTimer -= EVAL_INTERVAL;

            EvaluateRules();
        }

        private void EvaluateRules()
        {
            // Sort by priority
            _rules.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            int maxPerTick = 10; // prevent infinite loops
            int fired = 0;

            for (int i = 0; i < _rules.Count && fired < maxPerTick; i++)
            {
                var rule = _rules[i];
                if (!rule.CanFire(_gm.GameTime)) continue;

                // Check all conditions (AND logic)
                bool allMet = true;
                for (int c = 0; c < rule.Conditions.Count; c++)
                {
                    if (!rule.Conditions[c].Evaluate(_resources, _gm))
                    {
                        allMet = false;
                        break;
                    }
                }

                if (!allMet) continue;

                // Execute all actions
                for (int a = 0; a < rule.Actions.Count; a++)
                {
                    rule.Actions[a].Execute(_resources, _automation);
                }

                rule.LastFiredTime = _gm.GameTime;
                fired++;
                OnRuleFired?.Invoke(rule);
            }
        }

        // ─── PUBLIC API ─────────────────────────────────────────────

        public bool AddRule(LogicRule rule)
        {
            if (_rules.Count >= _maxRules) return false;

            rule.Id = $"rule_{_rules.Count}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            _rules.Add(rule);
            OnRuleAdded?.Invoke(rule);
            return true;
        }

        public bool RemoveRule(string ruleId)
        {
            return _rules.RemoveAll(r => r.Id == ruleId) > 0;
        }

        public void SetRuleEnabled(string ruleId, bool enabled)
        {
            var rule = _rules.Find(r => r.Id == ruleId);
            if (rule != null) rule.Enabled = enabled;
        }

        public void UpgradeMaxRules(int additional)
        {
            _maxRules += additional;
        }

        /// <summary>
        /// Create a rule from a simplified template (for tutorial/quick-add).
        /// Example: QuickRule("Auto sell energy", "energy", ConditionType.ResourceAbove, 50000, ActionType.SellResource, "energy", 10000)
        /// </summary>
        public LogicRule CreateQuickRule(string name, string condTarget,
            ConditionType condType, double condThreshold,
            ActionType actType, string actTarget, double actValue,
            double cooldown = 1.0)
        {
            var rule = new LogicRule
            {
                Name = name,
                Priority = _rules.Count,
                Enabled = true,
                Cooldown = cooldown,
                Conditions = new List<RuleCondition>
                {
                    new() { Type = condType, TargetId = condTarget, Threshold = condThreshold }
                },
                Actions = new List<RuleAction>
                {
                    new() { Type = actType, TargetId = actTarget, Value = actValue }
                }
            };

            return rule;
        }
    }
}
