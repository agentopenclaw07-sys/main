"""Strategy registry — maps strategy names to classes."""
from app.strategies.base import BaseStrategy
from app.strategies.sma_crossover import SmaCrossoverStrategy
from app.strategies.ema_crossover import EmaCrossoverStrategy
from app.strategies.mean_reversion import MeanReversionStrategy

STRATEGIES: dict[str, type[BaseStrategy]] = {
    "sma_crossover": SmaCrossoverStrategy,
    "ema_crossover": EmaCrossoverStrategy,
    "mean_reversion": MeanReversionStrategy,
}


def get_strategy(name: str, params: dict) -> BaseStrategy:
    cls = STRATEGIES.get(name)
    if not cls:
        raise ValueError(f"Unknown strategy: {name}. Available: {list(STRATEGIES.keys())}")
    return cls(params)


def list_strategies() -> list[dict]:
    return [
        {
            "name": "sma_crossover",
            "label": "SMA Crossover",
            "description": "Trades on simple moving average crossovers",
            "default_params": {"fast_period": 10, "slow_period": 30},
        },
        {
            "name": "ema_crossover",
            "label": "EMA Crossover",
            "description": "Trades on exponential moving average crossovers",
            "default_params": {"fast_period": 12, "slow_period": 26},
        },
        {
            "name": "mean_reversion",
            "label": "Mean Reversion",
            "description": "Trades when price deviates from moving average",
            "default_params": {"period": 20, "threshold": 0.02},
        },
    ]
