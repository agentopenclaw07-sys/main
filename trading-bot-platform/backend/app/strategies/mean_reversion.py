"""Mean Reversion Strategy.

Buys when price drops significantly below moving average.
Sells when price returns above moving average.
"""
from app.strategies.base import BaseStrategy


class MeanReversionStrategy(BaseStrategy):
    def evaluate(self, prices: list[float]) -> str:
        period = self.params.get("period", 20)
        threshold = self.params.get("threshold", 0.02)  # 2% deviation

        if len(prices) < period:
            return "hold"

        ma = self.sma(prices, period)
        current = prices[-1]
        deviation = (current - ma) / ma

        if deviation < -threshold:
            return "buy"  # Price below mean — expect reversion up
        elif deviation > threshold:
            return "sell"  # Price above mean — expect reversion down

        return "hold"
