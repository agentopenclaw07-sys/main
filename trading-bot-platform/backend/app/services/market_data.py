"""Market data service — generates simulated price data for paper trading.

Can be swapped for real exchange APIs (Binance, etc.) by implementing
the same interface.
"""
import asyncio
import json
import math
import random
from datetime import datetime, timedelta

from sqlalchemy import select

from app.core.database import async_session
import app.core.redis as _redis_mod
from app.models.price import PriceCandle


# Simulated price state per symbol
_price_state: dict[str, float] = {}

SYMBOLS = ["BTC/USDT", "ETH/USDT", "SOL/USDT"]
BASE_PRICES = {"BTC/USDT": 65000.0, "ETH/USDT": 3200.0, "SOL/USDT": 145.0}


def _next_price(symbol: str) -> dict:
    """Generate next simulated price tick using geometric Brownian motion."""
    if symbol not in _price_state:
        _price_state[symbol] = BASE_PRICES.get(symbol, 100.0)

    price = _price_state[symbol]
    # GBM parameters
    dt = 1 / (24 * 3600)  # 1 second
    mu = 0.0001  # slight upward drift
    sigma = 0.002  # volatility

    rand = random.gauss(0, 1)
    price *= math.exp((mu - 0.5 * sigma ** 2) * dt + sigma * math.sqrt(dt) * rand)
    _price_state[symbol] = price

    noise = price * 0.0005
    return {
        "symbol": symbol,
        "open": round(price + random.uniform(-noise, noise), 2),
        "high": round(price + abs(random.gauss(0, noise)), 2),
        "low": round(price - abs(random.gauss(0, noise)), 2),
        "close": round(price, 2),
        "volume": round(random.uniform(10, 1000), 2),
        "timestamp": datetime.utcnow().isoformat(),
    }


async def generate_historical_data(symbol: str, days: int = 30):
    """Generate historical candle data for backtesting."""
    async with async_session() as db:
        # Check if data already exists
        result = await db.execute(
            select(PriceCandle)
            .where(PriceCandle.symbol == symbol)
            .limit(1)
        )
        if result.scalar_one_or_none():
            return

        price = BASE_PRICES.get(symbol, 100.0)
        now = datetime.utcnow()
        candles = []

        for hour in range(days * 24):
            ts = now - timedelta(hours=(days * 24 - hour))
            noise = price * 0.002
            rand = random.gauss(0, 1)
            price *= math.exp(0.00001 + 0.005 * rand)

            candle = PriceCandle(
                symbol=symbol,
                open=round(price + random.uniform(-noise, noise), 2),
                high=round(price + abs(random.gauss(0, noise * 2)), 2),
                low=round(price - abs(random.gauss(0, noise * 2)), 2),
                close=round(price, 2),
                volume=round(random.uniform(100, 5000), 2),
                timestamp=ts,
            )
            candles.append(candle)

        db.add_all(candles)
        await db.commit()


async def price_feed_loop(interval: int = 5):
    """Main loop that generates prices and publishes to Redis."""
    # Generate historical data first
    for symbol in SYMBOLS:
        await generate_historical_data(symbol)

    while True:
        for symbol in SYMBOLS:
            tick = _next_price(symbol)

            # Publish to Redis for WebSocket consumers
            await _redis_mod.redis_client.publish("price_updates", json.dumps(tick))

            # Store latest price for agent workers
            await _redis_mod.redis_client.set(f"price:{symbol}", json.dumps(tick), ex=30)

        await asyncio.sleep(interval)


def get_current_price(symbol: str) -> float:
    """Synchronous helper to get latest cached price."""
    return _price_state.get(symbol, BASE_PRICES.get(symbol, 100.0))
