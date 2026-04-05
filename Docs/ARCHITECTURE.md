# EVOLVE: Autonomous Empire — Technical Architecture

## Overview

A mobile incremental game that evolves through 6 phases, from simple tapping
to a self-running autonomous empire with AI agents and economic simulation.

---

## System Dependency Graph

```
┌──────────────────────────────────────────────────────────────┐
│                     GameManager (Core)                       │
│  Tick loop → System registry → Phase management → Save/Load │
└──────────┬───────────────────────────────────────────────────┘
           │
    ┌──────┴──────┐
    │ ResourceSys │ ← Foundation: currencies, generators, upgrades
    └──────┬──────┘
           │
    ┌──────┴──────────┐
    │ AutomationSys   │ ← Workers, auto-buy (reads ResourceSys)
    └──────┬──────────┘
           │
    ┌──────┴──────┐
    │  LogicSys   │ ← IF/THEN rules (reads Resource, writes Automation)
    └──────┬──────┘
           │
    ┌──────┴──────┐
    │   AISys     │ ← Utility agents (reads all, writes Resource/Auto)
    └──────┬──────┘
           │
    ┌──────┴──────────┐
    │ SimulationSys   │ ← Economy, markets, population
    └──────┬──────────┘
           │
    ┌──────┴──────┐
    │  MetaSys    │ ← Prestige/Ascension (resets & amplifies all above)
    └─────────────┘

  Cross-cutting:
    MonetizationSys ← Boosts, IAP, ads (reads/writes Resource, hooks into all)
    LiveOpsSys      ← Events, daily rewards, remote config (modifies multipliers)
    SaveSys         ← Serializes all system state
    UIManager       ← Reads all systems, writes player actions
```

---

## First 5 Minutes — Exact Gameplay

```
0:00  Game opens. Single screen: big "TAP" button, Energy counter at 0.
0:01  Player taps. Energy: 1. Tap feedback "+1" floats up.
0:05  Player has ~5 Energy. Nothing else visible yet.
0:10  Energy hits 10. "Micro Generator" appears in list with [Buy: 10] button.
0:12  Player buys Micro Generator. Energy drops to 0. "+1/s" appears.
0:20  Energy auto-accumulates. Player keeps tapping. Gets to ~20.
0:30  Player buys 2nd Micro Gen. Now +2/s.
0:45  Energy ~40. "Click Power" upgrade appears [Cost: 50].
1:00  Player buys upgrade. Taps now give +2. Generators still producing.
1:30  Energy ~100. "Power Cell" unlocks [Buy: 100]. Player saves up.
2:00  Player buys Power Cell. Now +10/s total. Growth accelerates.
3:00  Energy ~800. Player has 5 Micro Gens, 2 Power Cells.
4:00  Energy ~1000. "NEW SYSTEM UNLOCKED: AUTOMATION" banner appears.
4:10  Automation tab appears. Player can hire first worker.
5:00  Player has a worker producing energy. Game is now partially autonomous.
```

---

## System Update Order (Per Tick)

1. **ResourceSystem** — compute production, apply to balances
2. **AutomationSystem** — process workers, check auto-buy
3. **LogicSystem** — evaluate player rules, fire actions
4. **AISystem** — evaluate agents, execute decisions
5. **SimulationSystem** — update businesses, markets, population
6. **MetaSystem** — passive (only acts on player input)
7. **MonetizationSystem** — update boost timers
8. **LiveOpsSystem** — check event status

Tick rate: 10 Hz (0.1s intervals). UI refreshes at 10 Hz independently.

---

## Performance Strategy

### Handling Thousands of Entities

1. **Flat arrays (SoA)**: Businesses stored as `BusinessEntity[]`, not List<BusinessEntity>.
   Cache-friendly sequential access.

2. **Batch processing**: `BatchProcessor<T>` processes N entities/frame.
   At 10,000 businesses, process 200/frame = all updated in 50 frames (0.8s at 60fps).

3. **LOD updates**: Low-level businesses update every 5 ticks instead of every tick.
   Reduces processing by 5x for mature empires.

4. **Update scheduling**: `UpdateScheduler` staggers expensive operations.
   Markets update at 1 Hz, population at 0.2 Hz, AI at 0.2 Hz.

5. **Object pooling**: All UI list items, particles, and floating text use pools.
   Zero allocation during gameplay.

### Mobile-Specific Optimizations

- **BigNumber uses double**: No BigInteger allocation. Sufficient for 1e308.
- **String caching**: `BigNumber.ToString()` results cached per frame.
- **UI throttling**: 10 Hz refresh, only visible elements updated.
- **Off-screen disable**: Hidden screens have CanvasGroup.interactable=false.
- **JSON over binary**: Smaller save files, debuggable, compressible.
- **No LINQ in hot paths**: All loops are manual for/foreach.

### Memory Budget

| System        | Estimated Memory |
|---------------|-----------------|
| Resources     | < 1 KB          |
| Generators    | < 2 KB          |
| Workers       | < 5 KB          |
| Logic Rules   | < 10 KB         |
| AI Agents     | < 5 KB          |
| Businesses    | ~400 KB (10K)   |
| Markets       | < 1 KB          |
| Save Data     | ~50-200 KB      |
| UI Pools      | ~1-5 MB         |
| **Total**     | **< 10 MB**     |

---

## MVP Build Plan

### V1 — Core Loop (2-3 weeks)

Build order:
1. GameManager + tick loop
2. BigNumber
3. ResourceSystem (currencies + generators)
4. MainScreen + GeneratorScreen UI
5. Tap mechanic + generator purchasing
6. Upgrade system
7. SaveManager (local JSON save)
8. Offline progress calculation
9. Daily rewards (simple version)
10. Basic achievements

**CUT from V1:**
- AI System
- Simulation System
- Prestige/Meta System
- Market economy
- Events system
- Cloud save
- IAP integration
- Battle pass

### V1.1 — Automation (1-2 weeks)

11. AutomationSystem (workers + auto-buy)
12. Automation UI screen
13. Worker hiring, assignment, fatigue

### V1.2 — Logic (2-3 weeks)

14. LogicSystem (rule engine)
15. Rule editor UI (condition picker + action picker)
16. Rule templates for onboarding

### V2 — Intelligence (3-4 weeks)

17. AISystem (utility agents)
18. AI management UI
19. Agent personality customization
20. Learning/adaptation loop

### V2.5 — Economy (3-4 weeks)

21. SimulationSystem (businesses, markets, population)
22. Simulation UI (business list, market chart, pop stats)
23. Market buy/sell mechanics

### V3 — Meta (2-3 weeks)

24. MetaSystem (prestige, prestige upgrades)
25. Prestige UI
26. Ascension (layer 2)

### V3.5 — Monetization + Live Ops (2-3 weeks)

27. MonetizationSystem (boosts, IAP hooks, ad placements)
28. Shop UI
29. LiveOpsSystem (events, remote config)
30. Cloud save integration

---

## Key Formulas Reference

| Formula | Expression | Notes |
|---------|-----------|-------|
| Generator cost | `base * 1.15^owned` | Industry standard idle game curve |
| Upgrade cost | `base * growth^level` | Growth varies: 1.5-3.0 |
| Production | `baseRate * owned * multiplier` | Multiplier stacks from upgrades, prestige, boosts |
| Prestige PP | `floor(150 * sqrt(lifetime / 1e10))` | Sqrt ensures diminishing returns |
| Prestige mult | `1 + (PP * 0.01)^0.5` | Sublinear: lots of PP needed for big mult |
| Worker efficiency | `max(0.3, 1.0 - 0.01 * hours)` | Degrades over time, floor at 30% |
| Market price | `base * (1 + sin(t*f) * v) + S/D` | Organic fluctuation + economics |
| Population | `P * (1 + r * (1 - P/K))` | Logistic growth with carrying capacity |
| Utility score | `weight * min(current/target, 2)` | Capped at 2x target for stability |

---

## Save Schema Version Strategy

```
Version 0 → 1: Add prestige fields
Version 1 → 2: Add simulation data (future)
Version 2 → 3: Add ascension data (future)

Migration is forward-only. Each version bump has a migration function.
Unknown fields in newer saves are ignored (forward-compatible reads).
```

---

## File Structure

```
Assets/
├── Scripts/
│   ├── Core/
│   │   ├── GameManager.cs      — Tick loop, system registry, lifecycle
│   │   ├── BigNumber.cs        — Double-based large number display
│   │   └── GameFormulas.cs     — All game math (costs, production, scaling)
│   ├── Systems/
│   │   ├── Resource/
│   │   │   └── ResourceSystem.cs — Currencies, generators, upgrades
│   │   ├── Automation/
│   │   │   └── AutomationSystem.cs — Workers, auto-buy, task slots
│   │   ├── Logic/
│   │   │   └── LogicSystem.cs  — IF/THEN rule engine
│   │   ├── AI/
│   │   │   └── AISystem.cs     — Utility-based AI agents
│   │   ├── Simulation/
│   │   │   └── SimulationSystem.cs — Economy, markets, population
│   │   ├── Meta/
│   │   │   └── MetaSystem.cs   — Prestige, ascension
│   │   ├── Monetization/
│   │   │   └── MonetizationSystem.cs — IAP, ads, boosts
│   │   └── LiveOps/
│   │       └── LiveOpsSystem.cs — Events, daily rewards, config
│   ├── UI/
│   │   └── UIManager.cs       — Screen management, data binding
│   ├── Save/
│   │   └── SaveManager.cs     — Serialization, versioning, cloud
│   └── Utils/
│       └── ObjectPool.cs      — Pooling, batching, scheduling
├── Data/
│   ├── generators.json        — Generator definitions + balance data
│   ├── progression.json       — Phase unlocks, upgrade trees, curves
│   └── events.json            — Daily rewards, event templates, achievements
└── Docs/
    └── ARCHITECTURE.md        — This file
```
