using System;
using System.Collections.Generic;
using Evolve.Core;
using UnityEngine;

namespace Evolve.Automation
{
    // ═══════════════════════════════════════════════════════════════════
    // AUTOMATION SYSTEM — Auto-buying, workers, task assignment
    // ═══════════════════════════════════════════════════════════════════
    // Unlocks at: 1,000 energy (GamePhase.Automation)
    //
    // Responsibilities:
    //   - Auto-buy generators when affordable
    //   - Manage workers that can be assigned to tasks
    //   - Handle task queues and worker allocation
    //
    // Inputs:  Resource balances, player configuration, worker counts
    // Outputs: Automatic purchases, task completion, efficiency bonuses
    //
    // Scaling: Workers are lightweight structs. Tasks are pooled.
    //          Max workers scales with prestige level.
    // ═══════════════════════════════════════════════════════════════════

    [Serializable]
    public class WorkerData
    {
        public string Id;
        public string AssignedTask; // "" = idle
        public double Efficiency;   // 0.3 to 1.0
        public double HoursWorked;  // affects efficiency via formula
        public int Level;           // higher level = more output

        public void Reset()
        {
            HoursWorked = 0;
            Efficiency = 1.0;
        }
    }

    [Serializable]
    public class AutoBuyRule
    {
        public string GeneratorId;
        public bool Enabled;
        public int Priority; // lower = higher priority
        public double MaxSpendPercent; // max % of balance to spend (0-1)
    }

    [Serializable]
    public enum TaskType
    {
        ProduceEnergy,
        ProduceData,
        ProduceCredits,
        Research,
        Maintenance // reduces efficiency decay
    }

    [Serializable]
    public class TaskSlot
    {
        public TaskType Type;
        public string ResourceTarget;
        public double BaseOutput;     // per worker per second
        public int AssignedWorkers;
        public bool Unlocked;
    }

    public class AutomationSystem : IGameSystem
    {
        public bool IsActive => GameManager.Instance != null
            && GameManager.Instance.CurrentPhase >= GamePhase.Automation;

        private GameManager _gm;
        private ResourceSystem _resources;

        private readonly List<WorkerData> _workers = new();
        private readonly List<AutoBuyRule> _autoBuyRules = new();
        private readonly List<TaskSlot> _taskSlots = new();

        private double _autoBuyTimer;
        private const double AUTO_BUY_INTERVAL = 1.0; // check every 1s

        public int MaxWorkers { get; set; } = 3; // increases with upgrades/prestige
        public IReadOnlyList<WorkerData> Workers => _workers;
        public IReadOnlyList<AutoBuyRule> AutoBuyRules => _autoBuyRules;
        public IReadOnlyList<TaskSlot> TaskSlots => _taskSlots;

        public event Action<WorkerData> OnWorkerHired;
        public event Action<string> OnAutoBuyTriggered;

        public void Initialize(GameManager gm)
        {
            _gm = gm;
            _resources = gm.GetSystem<ResourceSystem>();

            SetupTaskSlots();
            SetupDefaultAutoBuyRules();
        }

        private void SetupTaskSlots()
        {
            _taskSlots.Add(new TaskSlot
            {
                Type = TaskType.ProduceEnergy,
                ResourceTarget = "energy",
                BaseOutput = 5,
                Unlocked = true
            });

            _taskSlots.Add(new TaskSlot
            {
                Type = TaskType.ProduceData,
                ResourceTarget = "data",
                BaseOutput = 2,
                Unlocked = true
            });

            _taskSlots.Add(new TaskSlot
            {
                Type = TaskType.ProduceCredits,
                ResourceTarget = "credits",
                BaseOutput = 1,
                Unlocked = false // unlocks later
            });

            _taskSlots.Add(new TaskSlot
            {
                Type = TaskType.Research,
                ResourceTarget = "research",
                BaseOutput = 0.5,
                Unlocked = false
            });
        }

        private void SetupDefaultAutoBuyRules()
        {
            // Default: auto-buy cheapest generator first
            string[] gens = { "micro_gen", "power_cell", "reactor", "fusion_core",
                              "quantum_array", "dyson_collector", "singularity_engine" };

            for (int i = 0; i < gens.Length; i++)
            {
                _autoBuyRules.Add(new AutoBuyRule
                {
                    GeneratorId = gens[i],
                    Enabled = false, // player enables manually
                    Priority = i,
                    MaxSpendPercent = 0.5
                });
            }
        }

        public void Tick(double delta)
        {
            ProcessWorkers(delta);
            ProcessAutoBuy(delta);
        }

        // ─── WORKERS ─────────────────────────────────────────────────

        private void ProcessWorkers(double delta)
        {
            for (int i = 0; i < _workers.Count; i++)
            {
                var worker = _workers[i];
                if (string.IsNullOrEmpty(worker.AssignedTask)) continue;

                // Find task slot
                var task = _taskSlots.Find(t => t.Type.ToString() == worker.AssignedTask);
                if (task == null) continue;

                // Update fatigue
                worker.HoursWorked += delta / 3600.0;
                worker.Efficiency = GameFormulas.WorkerEfficiency(worker.HoursWorked);

                // Produce resources
                double output = task.BaseOutput * worker.Efficiency * (1 + worker.Level * 0.2) * delta;
                _resources.AddResource(task.ResourceTarget, new BigNumber(output));
            }
        }

        // ─── AUTO-BUY ────────────────────────────────────────────────

        private void ProcessAutoBuy(double delta)
        {
            _autoBuyTimer += delta;
            if (_autoBuyTimer < AUTO_BUY_INTERVAL) return;
            _autoBuyTimer -= AUTO_BUY_INTERVAL;

            // Sort by priority
            _autoBuyRules.Sort((a, b) => a.Priority.CompareTo(b.Priority));

            foreach (var rule in _autoBuyRules)
            {
                if (!rule.Enabled) continue;

                var gen = _resources.GetGenerator(rule.GeneratorId);
                if (gen == null) continue;

                BigNumber balance = _resources.GetAmount("energy");
                BigNumber maxSpend = balance * rule.MaxSpendPercent;
                BigNumber cost = gen.CurrentCost;

                if (cost <= maxSpend)
                {
                    if (_resources.BuyGenerator(rule.GeneratorId))
                    {
                        OnAutoBuyTriggered?.Invoke(rule.GeneratorId);
                    }
                }
            }
        }

        // ─── PUBLIC API ─────────────────────────────────────────────

        public bool HireWorker()
        {
            if (_workers.Count >= MaxWorkers) return false;

            // Cost: 100 * 2^(workerCount) energy
            BigNumber cost = new(100 * Math.Pow(2, _workers.Count));
            if (!_resources.SpendResource("energy", cost)) return false;

            var worker = new WorkerData
            {
                Id = $"worker_{_workers.Count}",
                Efficiency = 1.0,
                Level = 1
            };
            _workers.Add(worker);
            OnWorkerHired?.Invoke(worker);
            return true;
        }

        public bool AssignWorker(string workerId, string taskType)
        {
            var worker = _workers.Find(w => w.Id == workerId);
            if (worker == null) return false;

            // Unassign from previous task
            if (!string.IsNullOrEmpty(worker.AssignedTask))
            {
                var oldTask = _taskSlots.Find(t => t.Type.ToString() == worker.AssignedTask);
                if (oldTask != null) oldTask.AssignedWorkers--;
            }

            worker.AssignedTask = taskType;
            worker.Reset(); // reset fatigue on reassignment

            var newTask = _taskSlots.Find(t => t.Type.ToString() == taskType);
            if (newTask != null) newTask.AssignedWorkers++;

            return true;
        }

        public void SetAutoBuyEnabled(string generatorId, bool enabled)
        {
            var rule = _autoBuyRules.Find(r => r.GeneratorId == generatorId);
            if (rule != null) rule.Enabled = enabled;
        }

        public void SetAutoBuyPriority(string generatorId, int priority)
        {
            var rule = _autoBuyRules.Find(r => r.GeneratorId == generatorId);
            if (rule != null) rule.Priority = priority;
        }

        public void RestWorker(string workerId)
        {
            var worker = _workers.Find(w => w.Id == workerId);
            if (worker == null) return;

            if (!string.IsNullOrEmpty(worker.AssignedTask))
            {
                var task = _taskSlots.Find(t => t.Type.ToString() == worker.AssignedTask);
                if (task != null) task.AssignedWorkers--;
            }

            worker.AssignedTask = "";
            worker.Reset();
        }
    }
}
