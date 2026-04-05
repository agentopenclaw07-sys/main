using System;
using System.Collections.Generic;
using Evolve.Core;
using UnityEngine;

namespace Evolve.Monetization
{
    // ═══════════════════════════════════════════════════════════════════
    // MONETIZATION SYSTEM — IAP, rewarded ads, boosts
    // ═══════════════════════════════════════════════════════════════════
    // Always active. Non-intrusive, value-driven monetization.
    //
    // Three pillars:
    //   1. Rewarded Ads — optional, clear value (2x speed, offline bonus)
    //   2. IAP — permanent unlocks + consumable boosts
    //   3. Battle Pass — seasonal content progression
    //
    // Integration: hooks into resource/automation/meta systems via events.
    // ═══════════════════════════════════════════════════════════════════

    [Serializable]
    public class BoostData
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public BoostType Type;
        public double Multiplier;
        public double DurationSeconds;
        public double RemainingSeconds;
        public bool IsActive => RemainingSeconds > 0;
    }

    public enum BoostType
    {
        ProductionMultiplier,  // 2x all production
        ClickMultiplier,       // 5x tap value
        OfflineMultiplier,     // 2x offline earnings (applied on return)
        AutoSpeed,             // 2x automation speed
        ExperienceBoost,       // faster progression
    }

    [Serializable]
    public class IAPProduct
    {
        public string Id;
        public string DisplayName;
        public string Description;
        public string PriceUSD;        // "$0.99", "$4.99", etc.
        public IAPType Type;
        public bool Purchased;         // for permanent unlocks
        public int Quantity;           // for consumables: how many owned

        // What it gives
        public string RewardType;      // "boost", "currency", "permanent"
        public string RewardId;
        public double RewardAmount;
    }

    public enum IAPType
    {
        Consumable,     // can buy multiple
        NonConsumable,  // buy once (permanent)
        Subscription,   // recurring
    }

    [Serializable]
    public class AdPlacement
    {
        public string Id;
        public string TriggerContext;  // "offline_return", "prestige", "shop"
        public double CooldownSeconds;
        public double LastShownTime;
        public BoostType RewardBoostType;
        public double RewardDuration;
        public double RewardMultiplier;
        public int DailyLimit;
        public int DailyWatched;
    }

    public class MonetizationSystem : IGameSystem
    {
        public bool IsActive => true;

        private GameManager _gm;
        private ResourceSystem _resources;

        private readonly List<BoostData> _activeBoosts = new();
        private readonly List<IAPProduct> _products = new();
        private readonly List<AdPlacement> _adPlacements = new();

        public IReadOnlyList<BoostData> ActiveBoosts => _activeBoosts;
        public IReadOnlyList<IAPProduct> Products => _products;
        public IReadOnlyList<AdPlacement> AdPlacements => _adPlacements;

        // Computed multipliers from boosts
        public double ProductionBoostMultiplier { get; private set; } = 1.0;
        public double ClickBoostMultiplier { get; private set; } = 1.0;

        public event Action<BoostData> OnBoostActivated;
        public event Action<BoostData> OnBoostExpired;
        public event Action<IAPProduct> OnPurchaseComplete;

        public void Initialize(GameManager gm)
        {
            _gm = gm;
            _resources = gm.GetSystem<ResourceSystem>();

            SetupProducts();
            SetupAdPlacements();
        }

        private void SetupProducts()
        {
            _products.AddRange(new[]
            {
                // ─── PERMANENT UNLOCKS ───
                new IAPProduct
                {
                    Id = "remove_ads", DisplayName = "Remove Ads",
                    Description = "Remove all interstitial ads forever",
                    PriceUSD = "$2.99", Type = IAPType.NonConsumable,
                    RewardType = "permanent", RewardId = "no_ads"
                },
                new IAPProduct
                {
                    Id = "starter_pack", DisplayName = "Starter Pack",
                    Description = "10K Energy + 2x Production (permanent) + 5 Boosts",
                    PriceUSD = "$4.99", Type = IAPType.NonConsumable,
                    RewardType = "bundle", RewardId = "starter",
                    RewardAmount = 10000
                },
                new IAPProduct
                {
                    Id = "auto_clicker", DisplayName = "Auto Clicker",
                    Description = "Automatic tapping at 5/sec forever",
                    PriceUSD = "$1.99", Type = IAPType.NonConsumable,
                    RewardType = "permanent", RewardId = "auto_click"
                },

                // ─── CONSUMABLES ───
                new IAPProduct
                {
                    Id = "boost_pack_small", DisplayName = "5x Speed Boosts (3)",
                    Description = "3 boosts that give 5x production for 30 min each",
                    PriceUSD = "$0.99", Type = IAPType.Consumable,
                    RewardType = "boost", RewardId = "mega_production",
                    RewardAmount = 3
                },
                new IAPProduct
                {
                    Id = "energy_pack_medium", DisplayName = "Energy Pack (50K)",
                    Description = "Instantly gain 50,000 Energy",
                    PriceUSD = "$1.99", Type = IAPType.Consumable,
                    RewardType = "currency", RewardId = "energy",
                    RewardAmount = 50000
                },
                new IAPProduct
                {
                    Id = "prestige_pack", DisplayName = "Prestige Point Pack (50)",
                    Description = "50 Prestige Points instantly",
                    PriceUSD = "$4.99", Type = IAPType.Consumable,
                    RewardType = "currency", RewardId = "prestige_points",
                    RewardAmount = 50
                },
            });
        }

        private void SetupAdPlacements()
        {
            _adPlacements.AddRange(new[]
            {
                new AdPlacement
                {
                    Id = "ad_2x_production",
                    TriggerContext = "main_screen",
                    CooldownSeconds = 300, // 5 min
                    RewardBoostType = BoostType.ProductionMultiplier,
                    RewardDuration = 600, // 10 min
                    RewardMultiplier = 2.0,
                    DailyLimit = 10
                },
                new AdPlacement
                {
                    Id = "ad_offline_bonus",
                    TriggerContext = "offline_return",
                    CooldownSeconds = 0, // always available on return
                    RewardBoostType = BoostType.OfflineMultiplier,
                    RewardDuration = 0, // instant
                    RewardMultiplier = 2.0,
                    DailyLimit = 5
                },
                new AdPlacement
                {
                    Id = "ad_prestige_bonus",
                    TriggerContext = "prestige",
                    CooldownSeconds = 0,
                    RewardBoostType = BoostType.ExperienceBoost,
                    RewardDuration = 0,
                    RewardMultiplier = 1.5, // +50% prestige points
                    DailyLimit = 3
                },
            });
        }

        public void Tick(double delta)
        {
            UpdateBoosts(delta);
        }

        private void UpdateBoosts(double delta)
        {
            ProductionBoostMultiplier = 1.0;
            ClickBoostMultiplier = 1.0;

            for (int i = _activeBoosts.Count - 1; i >= 0; i--)
            {
                var boost = _activeBoosts[i];
                boost.RemainingSeconds -= delta;

                if (boost.RemainingSeconds <= 0)
                {
                    OnBoostExpired?.Invoke(boost);
                    _activeBoosts.RemoveAt(i);
                    continue;
                }

                // Accumulate multipliers
                switch (boost.Type)
                {
                    case BoostType.ProductionMultiplier:
                        ProductionBoostMultiplier *= boost.Multiplier;
                        break;
                    case BoostType.ClickMultiplier:
                        ClickBoostMultiplier *= boost.Multiplier;
                        break;
                }
            }
        }

        // ─── PUBLIC API ─────────────────────────────────────────────

        public void ActivateBoost(string id, BoostType type, double multiplier, double duration)
        {
            var boost = new BoostData
            {
                Id = id,
                Type = type,
                Multiplier = multiplier,
                DurationSeconds = duration,
                RemainingSeconds = duration
            };

            _activeBoosts.Add(boost);
            OnBoostActivated?.Invoke(boost);
        }

        /// <summary>
        /// Called when a rewarded ad finishes playing.
        /// </summary>
        public void OnRewardedAdCompleted(string placementId)
        {
            var placement = _adPlacements.Find(p => p.Id == placementId);
            if (placement == null) return;

            if (placement.DailyWatched >= placement.DailyLimit) return;
            placement.DailyWatched++;
            placement.LastShownTime = _gm.GameTime;

            if (placement.RewardDuration > 0)
            {
                ActivateBoost(
                    $"ad_{placementId}",
                    placement.RewardBoostType,
                    placement.RewardMultiplier,
                    placement.RewardDuration
                );
            }
            else
            {
                // Instant reward (e.g., 2x offline bonus applied immediately)
                HandleInstantAdReward(placement);
            }
        }

        private void HandleInstantAdReward(AdPlacement placement)
        {
            switch (placement.TriggerContext)
            {
                case "offline_return":
                    // Double the last offline earnings (caller should pass amount)
                    break;
                case "prestige":
                    // Boost applied to prestige reward by caller
                    break;
            }
        }

        /// <summary>
        /// Process an IAP purchase after store confirmation.
        /// </summary>
        public void ProcessPurchase(string productId)
        {
            var product = _products.Find(p => p.Id == productId);
            if (product == null) return;

            switch (product.Type)
            {
                case IAPType.NonConsumable:
                    product.Purchased = true;
                    ApplyPermanentPurchase(product);
                    break;

                case IAPType.Consumable:
                    ApplyConsumablePurchase(product);
                    break;
            }

            OnPurchaseComplete?.Invoke(product);
        }

        private void ApplyPermanentPurchase(IAPProduct product)
        {
            switch (product.RewardId)
            {
                case "starter":
                    _resources.AddResource("energy", new BigNumber(product.RewardAmount));
                    _resources.ApplyGlobalMultiplier(2.0);
                    ActivateBoost("starter_boost", BoostType.ProductionMultiplier, 2, 3600);
                    break;

                case "auto_click":
                    // Flag checked in tap processing
                    break;
            }
        }

        private void ApplyConsumablePurchase(IAPProduct product)
        {
            switch (product.RewardType)
            {
                case "currency":
                    _resources.AddResource(product.RewardId, new BigNumber(product.RewardAmount));
                    break;

                case "boost":
                    product.Quantity += (int)product.RewardAmount;
                    break;
            }
        }

        public bool IsAdAvailable(string placementId)
        {
            var placement = _adPlacements.Find(p => p.Id == placementId);
            if (placement == null) return false;
            if (placement.DailyWatched >= placement.DailyLimit) return false;
            if (_gm.GameTime - placement.LastShownTime < placement.CooldownSeconds) return false;
            return true;
        }

        public void ResetDailyAdCounts()
        {
            foreach (var p in _adPlacements) p.DailyWatched = 0;
        }

        public bool HasPermanentPurchase(string productId)
        {
            var product = _products.Find(p => p.Id == productId);
            return product is { Type: IAPType.NonConsumable, Purchased: true };
        }
    }
}
