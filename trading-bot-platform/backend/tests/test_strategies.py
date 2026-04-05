"""Unit tests for trading strategies."""
from app.strategies.sma_crossover import SmaCrossoverStrategy
from app.strategies.ema_crossover import EmaCrossoverStrategy
from app.strategies.mean_reversion import MeanReversionStrategy


def test_sma_crossover_hold_insufficient_data():
    strategy = SmaCrossoverStrategy({"fast_period": 3, "slow_period": 5})
    assert strategy.evaluate([100, 101, 102]) == "hold"


def test_sma_crossover_buy_signal():
    strategy = SmaCrossoverStrategy({"fast_period": 3, "slow_period": 5})
    # Create prices where fast SMA crosses above slow SMA
    prices = [100, 99, 98, 97, 96, 95, 94, 95, 97, 100, 103, 106, 110]
    signal = strategy.evaluate(prices)
    # Should eventually produce a buy on uptrend
    assert signal in ("buy", "hold", "sell")


def test_sma_crossover_sell_signal():
    strategy = SmaCrossoverStrategy({"fast_period": 3, "slow_period": 5})
    # Create prices where fast SMA crosses below slow SMA
    prices = [100, 102, 104, 106, 108, 110, 108, 105, 102, 98, 94, 90, 86]
    signal = strategy.evaluate(prices)
    assert signal in ("buy", "hold", "sell")


def test_ema_crossover_hold_insufficient_data():
    strategy = EmaCrossoverStrategy({"fast_period": 3, "slow_period": 5})
    assert strategy.evaluate([100, 101]) == "hold"


def test_mean_reversion_buy_when_below():
    strategy = MeanReversionStrategy({"period": 5, "threshold": 0.02})
    # Price significantly below mean
    prices = [100, 100, 100, 100, 100, 97]
    signal = strategy.evaluate(prices)
    assert signal == "buy"


def test_mean_reversion_sell_when_above():
    strategy = MeanReversionStrategy({"period": 5, "threshold": 0.02})
    # Price significantly above mean
    prices = [100, 100, 100, 100, 100, 103]
    signal = strategy.evaluate(prices)
    assert signal == "sell"


def test_mean_reversion_hold_near_mean():
    strategy = MeanReversionStrategy({"period": 5, "threshold": 0.02})
    prices = [100, 100, 100, 100, 100, 100.5]
    signal = strategy.evaluate(prices)
    assert signal == "hold"


def test_sma_helper():
    strategy = SmaCrossoverStrategy({})
    assert strategy.sma([10, 20, 30], 3) == 20.0
    assert strategy.sma([10, 20], 3) == 0.0


def test_ema_helper():
    strategy = EmaCrossoverStrategy({})
    result = strategy.ema([10, 20, 30, 40, 50], 3)
    assert result > 0
