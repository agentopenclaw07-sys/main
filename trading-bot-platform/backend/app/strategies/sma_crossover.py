"""Simple Moving Average Crossover Strategy.

Buys when fast SMA crosses above slow SMA.
Sells when fast SMA crosses below slow SMA.
"""
from app.strategies.base import BaseStrategy


class SmaCrossoverStrategy(BaseStrategy):
    def evaluate(self, prices: list[float]) -> str:
        fast_period = self.params.get("fast_period", 10)
        slow_period = self.params.get("slow_period", 30)

        if len(prices) < slow_period + 1:
            return "hold"

        fast_now = self.sma(prices, fast_period)
        slow_now = self.sma(prices, slow_period)
        fast_prev = self.sma(prices[:-1], fast_period)
        slow_prev = self.sma(prices[:-1], slow_period)

        # Crossover detection
        if fast_prev <= slow_prev and fast_now > slow_now:
            return "buy"
        elif fast_prev >= slow_prev and fast_now < slow_now:
            return "sell"

        return "hold"
