"""Base strategy interface. All strategies must implement this."""
from abc import ABC, abstractmethod


class BaseStrategy(ABC):
    def __init__(self, params: dict):
        self.params = params

    @abstractmethod
    def evaluate(self, prices: list[float]) -> str:
        """Evaluate the strategy given a list of historical close prices.

        Returns:
            "buy", "sell", or "hold"
        """
        pass

    @staticmethod
    def sma(prices: list[float], period: int) -> float:
        """Simple moving average."""
        if len(prices) < period:
            return 0.0
        return sum(prices[-period:]) / period

    @staticmethod
    def ema(prices: list[float], period: int) -> float:
        """Exponential moving average."""
        if len(prices) < period:
            return 0.0
        multiplier = 2 / (period + 1)
        ema_val = sum(prices[:period]) / period
        for price in prices[period:]:
            ema_val = (price - ema_val) * multiplier + ema_val
        return ema_val
