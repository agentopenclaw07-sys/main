"""Exponential Moving Average Crossover Strategy.

Uses EMA for faster signal response compared to SMA.
"""
from app.strategies.base import BaseStrategy


class EmaCrossoverStrategy(BaseStrategy):
    def evaluate(self, prices: list[float]) -> str:
        fast_period = self.params.get("fast_period", 12)
        slow_period = self.params.get("slow_period", 26)

        if len(prices) < slow_period + 1:
            return "hold"

        fast_now = self.ema(prices, fast_period)
        slow_now = self.ema(prices, slow_period)
        fast_prev = self.ema(prices[:-1], fast_period)
        slow_prev = self.ema(prices[:-1], slow_period)

        if fast_prev <= slow_prev and fast_now > slow_now:
            return "buy"
        elif fast_prev >= slow_prev and fast_now < slow_now:
            return "sell"

        return "hold"
