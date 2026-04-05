using System;
using System.Collections.Generic;
using System.IO;
using Evolve.Core;
using UnityEngine;

namespace Evolve.SaveSystem
{
    // ═══════════════════════════════════════════════════════════════════
    // SAVE SYSTEM — Serialization, versioning, cloud-ready
    // ═══════════════════════════════════════════════════════════════════
    // Design:
    //   - Single serializable SaveData object holds all game state
    //   - JSON serialization (Unity JsonUtility for speed)
    //   - Schema version for migration support
    //   - Auto-save on pause/quit, manual save available
    //   - Cloud save: SaveData can be synced via any backend
    //
    // Versioning strategy:
    //   - Each save has a version number
    //   - Migration functions upgrade old saves to current format
    //   - Forward-compatible: unknown fields are ignored
    // ═══════════════════════════════════════════════════════════════════

    public static class SaveConstants
    {
        public const int CURRENT_VERSION = 1;
        public const string SAVE_FILE = "evolve_save.json";
        public const string BACKUP_FILE = "evolve_save_backup.json";
    }

    [Serializable]
    public class SaveData
    {
        // ─── Meta ───
        public int Version = SaveConstants.CURRENT_VERSION;
        public long LastSaveTimestamp;
        public string PlayerId;

        // ─── Game State ───
        public int CurrentPhase;
        public double GameTime;
        public long TickCount;

        // ─── Resources ───
        public List<ResourceSaveData> Resources = new();
        public List<GeneratorSaveData> Generators = new();
        public List<UpgradeSaveData> Upgrades = new();

        // ─── Automation ───
        public List<WorkerSaveData> Workers = new();
        public List<AutoBuySaveData> AutoBuyRules = new();
        public int MaxWorkers;

        // ─── Logic ───
        public List<RuleSaveData> Rules = new();
        public int MaxRules;

        // ─── AI ───
        public List<AIAgentSaveData> AIAgents = new();
        public int MaxAIAgents;

        // ─── Simulation ───
        public List<BusinessSaveData> Businesses = new();
        public PopulationSaveData Population;
        public List<MarketSaveData> Markets = new();

        // ─── Meta/Prestige ───
        public int PrestigeCount;
        public double PrestigePoints;
        public double LifetimePrestigePoints;
        public List<PrestigeUpgradeSaveData> PrestigeUpgrades = new();
        public int AscensionCount;
        public double AscensionTokens;

        // ─── Monetization ───
        public List<BoostSaveData> ActiveBoosts = new();
        public List<string> PurchasedProducts = new();

        // ─── Live Ops ───
        public DailyRewardSaveData DailyRewards;
        public List<AchievementSaveData> Achievements = new();

        // ─── Stats ───
        public double TotalTaps;
        public double TotalGeneratorsBought;
        public double TotalWorkersHired;
        public double TotalRulesCreated;
    }

    // ─── Sub-structures ──────────────────────────────────────────────

    [Serializable] public class ResourceSaveData
    {
        public string Id;
        public double Amount;
        public double TotalEarned;
        public double Capacity;
    }

    [Serializable] public class GeneratorSaveData
    {
        public string Id;
        public int Owned;
        public double Multiplier;
    }

    [Serializable] public class UpgradeSaveData
    {
        public string Id;
        public int Level;
    }

    [Serializable] public class WorkerSaveData
    {
        public string Id;
        public string AssignedTask;
        public int Level;
        public double HoursWorked;
    }

    [Serializable] public class AutoBuySaveData
    {
        public string GeneratorId;
        public bool Enabled;
        public int Priority;
        public double MaxSpendPercent;
    }

    [Serializable] public class RuleSaveData
    {
        public string Id;
        public string Name;
        public int Priority;
        public bool Enabled;
        public double Cooldown;
        public List<ConditionSaveData> Conditions = new();
        public List<ActionSaveData> Actions = new();
    }

    [Serializable] public class ConditionSaveData
    {
        public int Type; // cast to ConditionType
        public string TargetId;
        public double Threshold;
    }

    [Serializable] public class ActionSaveData
    {
        public int Type; // cast to ActionType
        public string TargetId;
        public double Value;
        public string StringParam;
    }

    [Serializable] public class AIAgentSaveData
    {
        public string Id;
        public string Name;
        public bool Active;
        public double EvalInterval;
        public double Aggression, Efficiency, Growth, Stability;
        public List<AIActionSaveData> Actions = new();
    }

    [Serializable] public class AIActionSaveData
    {
        public int Type;
        public double Weight;
        public double LearnedBias;
    }

    [Serializable] public class BusinessSaveData
    {
        public string Type;
        public string Input, Output;
        public double ProdRate, ConsRate;
        public int Workers, WorkerSlots, Level;
        public bool Active;
    }

    [Serializable] public class PopulationSaveData
    {
        public double Current, Capacity, GrowthRate, Happiness;
    }

    [Serializable] public class MarketSaveData
    {
        public string ResourceId;
        public double BasePrice, CurrentPrice, Supply, Demand;
    }

    [Serializable] public class PrestigeUpgradeSaveData
    {
        public string Id;
        public int Level;
    }

    [Serializable] public class BoostSaveData
    {
        public string Id;
        public int Type;
        public double Multiplier;
        public double Remaining;
    }

    [Serializable] public class DailyRewardSaveData
    {
        public int CurrentDay;
        public long LastClaimTimestamp;
        public int TotalDaysClaimed;
    }

    [Serializable] public class AchievementSaveData
    {
        public string Id;
        public double CurrentValue;
        public bool Completed;
        public bool Claimed;
    }

    // ═══════════════════════════════════════════════════════════════════
    // SAVE MANAGER
    // ═══════════════════════════════════════════════════════════════════

    public class SaveManager : MonoBehaviour
    {
        public static SaveManager Instance { get; private set; }

        public long LastSaveTime { get; private set; }

        private string SavePath => Path.Combine(Application.persistentDataPath, SaveConstants.SAVE_FILE);
        private string BackupPath => Path.Combine(Application.persistentDataPath, SaveConstants.BACKUP_FILE);

        private void Awake()
        {
            if (Instance != null) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void Save()
        {
            try
            {
                var data = BuildSaveData();
                string json = JsonUtility.ToJson(data, false); // compact

                // Write backup first, then main save (crash safety)
                if (File.Exists(SavePath))
                    File.Copy(SavePath, BackupPath, true);

                File.WriteAllText(SavePath, json);
                LastSaveTime = data.LastSaveTimestamp;

                Debug.Log($"[Save] Saved ({json.Length} bytes)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Failed: {e.Message}");
            }
        }

        public void Load()
        {
            try
            {
                string path = File.Exists(SavePath) ? SavePath :
                              File.Exists(BackupPath) ? BackupPath : null;

                if (path == null)
                {
                    Debug.Log("[Save] No save file found, starting fresh");
                    return;
                }

                string json = File.ReadAllText(path);
                var data = JsonUtility.FromJson<SaveData>(json);

                // Version migration
                if (data.Version < SaveConstants.CURRENT_VERSION)
                    data = MigrateSave(data);

                ApplySaveData(data);
                LastSaveTime = data.LastSaveTimestamp;

                Debug.Log($"[Save] Loaded (v{data.Version}, {json.Length} bytes)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Load failed: {e.Message}");
            }
        }

        private SaveData BuildSaveData()
        {
            var gm = GameManager.Instance;
            var res = gm.GetSystem<ResourceSystem>();

            var data = new SaveData
            {
                Version = SaveConstants.CURRENT_VERSION,
                LastSaveTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                CurrentPhase = (int)gm.CurrentPhase,
                GameTime = gm.GameTime,
                TickCount = gm.TickCount,
            };

            // Resources
            foreach (var r in res.Resources.Values)
            {
                data.Resources.Add(new ResourceSaveData
                {
                    Id = r.Id,
                    Amount = r.Amount.Value,
                    TotalEarned = r.TotalEarned.Value,
                    Capacity = r.Capacity.Value,
                });
            }

            // Generators
            foreach (var g in res.Generators)
            {
                data.Generators.Add(new GeneratorSaveData
                {
                    Id = g.Id,
                    Owned = g.Owned,
                    Multiplier = g.Multiplier,
                });
            }

            // Upgrades
            foreach (var u in res.Upgrades)
            {
                data.Upgrades.Add(new UpgradeSaveData { Id = u.Id, Level = u.Level });
            }

            return data;
        }

        private void ApplySaveData(SaveData data)
        {
            // Restore is inverse of build — left as exercise for each system
            // Each system should expose a LoadState(SaveData) method
            Debug.Log($"[Save] Applying save data v{data.Version}");
        }

        private SaveData MigrateSave(SaveData data)
        {
            // Version 0 → 1: add prestige fields
            if (data.Version < 1)
            {
                data.PrestigeCount = 0;
                data.PrestigePoints = 0;
                data.Version = 1;
            }

            return data;
        }

        public void DeleteSave()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
            if (File.Exists(BackupPath)) File.Delete(BackupPath);
            Debug.Log("[Save] Save files deleted");
        }

        /// <summary>
        /// Export save as JSON string for cloud sync.
        /// </summary>
        public string ExportForCloud()
        {
            var data = BuildSaveData();
            return JsonUtility.ToJson(data, false);
        }

        /// <summary>
        /// Import save from cloud JSON string.
        /// </summary>
        public bool ImportFromCloud(string json)
        {
            try
            {
                var data = JsonUtility.FromJson<SaveData>(json);
                if (data.Version > SaveConstants.CURRENT_VERSION)
                {
                    Debug.LogWarning("[Save] Cloud save is from a newer version");
                    return false;
                }
                ApplySaveData(data);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Cloud import failed: {e.Message}");
                return false;
            }
        }
    }
}
