using System;
using System.Collections.Generic;
using Evolve.Core;
using UnityEngine;

namespace Evolve.LiveOps
{
    // ═══════════════════════════════════════════════════════════════════
    // LIVE OPS SYSTEM — Daily rewards, events, remote config
    // ═══════════════════════════════════════════════════════════════════
    // Always active. Data-driven, server-configurable.
    //
    // Sub-systems:
    //   1. Daily Login Rewards — 7-day cycle with milestone bonuses
    //   2. Events — time-limited gameplay modifiers
    //   3. Remote Config — key-value overrides for tuning without updates
    //   4. Achievements — track and reward milestones
    // ═══════════════════════════════════════════════════════════════════

    [Serializable]
    public class DailyReward
    {
        public int Day;                // 1-7
        public string ResourceId;
        public double Amount;
        public bool IsMilestone;       // day 7 = special reward
        public string BonusBoostId;    // optional boost reward
    }

    [Serializable]
    public class DailyRewardState
    {
        public int CurrentDay;         // 0-6
        public long LastClaimTimestamp; // unix seconds
        public int TotalDaysClaimed;
        public bool ClaimedToday;
    }

    [Serializable]
    public class GameEvent
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public long StartTimestamp;
        public long EndTimestamp;
        public EventType Type;
        public double Modifier;        // e.g., 2.0 = double production
        public string TargetResource;  // which resource is affected
        public bool Active;

        public bool IsLive(long now) => now >= StartTimestamp && now <= EndTimestamp;
    }

    public enum EventType
    {
        ProductionBoost,    // X multiplier to production
        CostReduction,      // X multiplier to costs (0.5 = half price)
        BonusResource,      // extra resource drops
        PrestigeBonus,      // more prestige points
        SpecialUnlock,      // temporary access to premium feature
    }

    [Serializable]
    public class Achievement
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string TrackingKey;     // what stat to track
        public double TargetValue;
        public double CurrentValue;
        public bool Completed;
        public bool Claimed;
        public string RewardResource;
        public double RewardAmount;
    }

    [Serializable]
    public class RemoteConfigEntry
    {
        public string Key;
        public string Value;
        public string DefaultValue;
    }

    public class LiveOpsSystem : IGameSystem
    {
        public bool IsActive => true;

        private GameManager _gm;
        private ResourceSystem _resources;

        private DailyRewardState _dailyState = new();
        private readonly List<DailyReward> _dailyRewards = new();
        private readonly List<GameEvent> _activeEvents = new();
        private readonly List<Achievement> _achievements = new();
        private readonly Dictionary<string, RemoteConfigEntry> _remoteConfig = new();

        private double _eventCheckTimer;

        public DailyRewardState DailyState => _dailyState;
        public IReadOnlyList<DailyReward> DailyRewards => _dailyRewards;
        public IReadOnlyList<GameEvent> ActiveEvents => _activeEvents;
        public IReadOnlyList<Achievement> Achievements => _achievements;

        // Computed event multipliers
        public double EventProductionMultiplier { get; private set; } = 1.0;
        public double EventCostMultiplier { get; private set; } = 1.0;

        public event Action<DailyReward> OnDailyRewardClaimed;
        public event Action<GameEvent> OnEventStarted;
        public event Action<GameEvent> OnEventEnded;
        public event Action<Achievement> OnAchievementCompleted;

        public void Initialize(GameManager gm)
        {
            _gm = gm;
            _resources = gm.GetSystem<ResourceSystem>();

            SetupDailyRewards();
            SetupAchievements();
            SetupDefaultRemoteConfig();
        }

        private void SetupDailyRewards()
        {
            _dailyRewards.AddRange(new[]
            {
                new DailyReward { Day = 1, ResourceId = "energy", Amount = 100 },
                new DailyReward { Day = 2, ResourceId = "energy", Amount = 250 },
                new DailyReward { Day = 3, ResourceId = "data", Amount = 50 },
                new DailyReward { Day = 4, ResourceId = "energy", Amount = 500 },
                new DailyReward { Day = 5, ResourceId = "credits", Amount = 100 },
                new DailyReward { Day = 6, ResourceId = "energy", Amount = 1000 },
                new DailyReward
                {
                    Day = 7, ResourceId = "energy", Amount = 5000,
                    IsMilestone = true, BonusBoostId = "daily_mega_boost"
                },
            });
        }

        private void SetupAchievements()
        {
            _achievements.AddRange(new[]
            {
                new Achievement
                {
                    Id = "first_tap", DisplayName = "First Steps",
                    Description = "Tap 1 time", TrackingKey = "total_taps",
                    TargetValue = 1, RewardResource = "energy", RewardAmount = 10
                },
                new Achievement
                {
                    Id = "tap_100", DisplayName = "Tap Happy",
                    Description = "Tap 100 times", TrackingKey = "total_taps",
                    TargetValue = 100, RewardResource = "energy", RewardAmount = 500
                },
                new Achievement
                {
                    Id = "energy_1k", DisplayName = "Power Surge",
                    Description = "Accumulate 1,000 Energy", TrackingKey = "total_energy",
                    TargetValue = 1000, RewardResource = "energy", RewardAmount = 200
                },
                new Achievement
                {
                    Id = "energy_1m", DisplayName = "Megawatt",
                    Description = "Accumulate 1,000,000 Energy", TrackingKey = "total_energy",
                    TargetValue = 1_000_000, RewardResource = "data", RewardAmount = 1000
                },
                new Achievement
                {
                    Id = "first_gen", DisplayName = "Automation Begins",
                    Description = "Buy your first generator", TrackingKey = "generators_bought",
                    TargetValue = 1, RewardResource = "energy", RewardAmount = 50
                },
                new Achievement
                {
                    Id = "first_worker", DisplayName = "Employee #1",
                    Description = "Hire your first worker", TrackingKey = "workers_hired",
                    TargetValue = 1, RewardResource = "energy", RewardAmount = 500
                },
                new Achievement
                {
                    Id = "first_rule", DisplayName = "Logician",
                    Description = "Create your first logic rule", TrackingKey = "rules_created",
                    TargetValue = 1, RewardResource = "data", RewardAmount = 100
                },
                new Achievement
                {
                    Id = "first_prestige", DisplayName = "Rebirth",
                    Description = "Prestige for the first time", TrackingKey = "prestige_count",
                    TargetValue = 1, RewardResource = "prestige_points", RewardAmount = 5
                },
                new Achievement
                {
                    Id = "prestige_10", DisplayName = "Veteran",
                    Description = "Prestige 10 times", TrackingKey = "prestige_count",
                    TargetValue = 10, RewardResource = "prestige_points", RewardAmount = 50
                },
            });
        }

        private void SetupDefaultRemoteConfig()
        {
            // These can be overridden by server-fetched config
            SetConfig("tick_rate", "0.1");
            SetConfig("offline_cap_hours", "8");
            SetConfig("max_generators", "8");
            SetConfig("prestige_threshold", "1e10");
            SetConfig("event_active", "false");
            SetConfig("event_id", "");
            SetConfig("maintenance_mode", "false");
        }

        public void Tick(double delta)
        {
            _eventCheckTimer += delta;
            if (_eventCheckTimer >= 60.0) // check every minute
            {
                _eventCheckTimer = 0;
                UpdateEvents();
            }

            CheckDailyReset();
        }

        // ─── DAILY REWARDS ──────────────────────────────────────────

        private void CheckDailyReset()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long daysSinceEpoch = now / 86400;
            long lastClaimDay = _dailyState.LastClaimTimestamp / 86400;

            if (daysSinceEpoch > lastClaimDay)
                _dailyState.ClaimedToday = false;

            // Reset streak if missed a day
            if (daysSinceEpoch > lastClaimDay + 1)
                _dailyState.CurrentDay = 0;
        }

        public bool CanClaimDaily() => !_dailyState.ClaimedToday;

        public DailyReward ClaimDailyReward()
        {
            if (_dailyState.ClaimedToday) return null;

            var reward = _dailyRewards[_dailyState.CurrentDay];
            _resources.AddResource(reward.ResourceId, new BigNumber(reward.Amount));

            _dailyState.ClaimedToday = true;
            _dailyState.LastClaimTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _dailyState.TotalDaysClaimed++;
            _dailyState.CurrentDay = (_dailyState.CurrentDay + 1) % 7;

            if (reward.IsMilestone && !string.IsNullOrEmpty(reward.BonusBoostId))
            {
                var monetization = _gm.GetSystem<Monetization.MonetizationSystem>();
                monetization?.ActivateBoost(reward.BonusBoostId,
                    Monetization.BoostType.ProductionMultiplier, 3.0, 3600);
            }

            OnDailyRewardClaimed?.Invoke(reward);
            return reward;
        }

        // ─── EVENTS ─────────────────────────────────────────────────

        private void UpdateEvents()
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            EventProductionMultiplier = 1.0;
            EventCostMultiplier = 1.0;

            for (int i = _activeEvents.Count - 1; i >= 0; i--)
            {
                var evt = _activeEvents[i];
                if (!evt.IsLive(now))
                {
                    evt.Active = false;
                    OnEventEnded?.Invoke(evt);
                    _activeEvents.RemoveAt(i);
                    continue;
                }

                // Apply event effects
                switch (evt.Type)
                {
                    case EventType.ProductionBoost:
                        EventProductionMultiplier *= evt.Modifier;
                        break;
                    case EventType.CostReduction:
                        EventCostMultiplier *= evt.Modifier;
                        break;
                }
            }
        }

        public void AddEvent(GameEvent evt)
        {
            _activeEvents.Add(evt);
            evt.Active = true;
            OnEventStarted?.Invoke(evt);
        }

        // ─── ACHIEVEMENTS ───────────────────────────────────────────

        public void UpdateAchievementProgress(string trackingKey, double value)
        {
            for (int i = 0; i < _achievements.Count; i++)
            {
                var ach = _achievements[i];
                if (ach.Completed || ach.TrackingKey != trackingKey) continue;

                ach.CurrentValue = Math.Max(ach.CurrentValue, value);
                if (ach.CurrentValue >= ach.TargetValue)
                {
                    ach.Completed = true;
                    OnAchievementCompleted?.Invoke(ach);
                }
            }
        }

        public bool ClaimAchievement(string achievementId)
        {
            var ach = _achievements.Find(a => a.Id == achievementId);
            if (ach == null || !ach.Completed || ach.Claimed) return false;

            _resources.AddResource(ach.RewardResource, new BigNumber(ach.RewardAmount));
            ach.Claimed = true;
            return true;
        }

        // ─── REMOTE CONFIG ──────────────────────────────────────────

        public void SetConfig(string key, string value)
        {
            _remoteConfig[key] = new RemoteConfigEntry
            {
                Key = key, Value = value, DefaultValue = value
            };
        }

        public string GetConfig(string key, string defaultValue = "")
        {
            return _remoteConfig.TryGetValue(key, out var entry) ? entry.Value : defaultValue;
        }

        public double GetConfigDouble(string key, double defaultValue = 0)
        {
            if (_remoteConfig.TryGetValue(key, out var entry)
                && double.TryParse(entry.Value, out double result))
                return result;
            return defaultValue;
        }

        /// <summary>
        /// Apply a batch of remote config values (e.g., fetched from server).
        /// </summary>
        public void ApplyRemoteConfig(Dictionary<string, string> config)
        {
            foreach (var kvp in config)
            {
                if (_remoteConfig.ContainsKey(kvp.Key))
                    _remoteConfig[kvp.Key].Value = kvp.Value;
                else
                    SetConfig(kvp.Key, kvp.Value);
            }

            Debug.Log($"[LiveOps] Applied {config.Count} remote config values");
        }
    }
}
