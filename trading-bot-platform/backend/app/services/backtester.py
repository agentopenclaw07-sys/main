"""Simple backtesting engine that runs strategies against historical data."""
from datetime import datetime

from app.api.schemas import BacktestResult, TradeResponse
from app.models.price import PriceCandle
from app.strategies.registry import get_strategy


def run_backtest(
    candles: list[PriceCandle],
    strategy_name: str,
    params: dict,
    initial_capital: float = 10000.0,
) -> BacktestResult:
    strategy = get_strategy(strategy_name, params)

    capital = initial_capital
    peak = capital
    max_drawdown = 0.0
    position_side = "none"
    position_size = 0.0
    entry_price = 0.0
    trades = []
    winning = 0

    prices = [c.close for c in candles]

    for i, candle in enumerate(candles):
        signal = strategy.evaluate(prices[: i + 1])

        if signal == "buy" and position_side == "none":
            # Open long
            position_size = min(capital * 0.1, capital)  # 10% of capital
            entry_price = candle.close
            quantity = position_size / entry_price
            position_side = "long"
            trades.append(TradeResponse(
                id=len(trades) + 1,
                agent_id=0,
                symbol=candle.symbol,
                side="buy",
                price=entry_price,
                quantity=round(quantity, 6),
                value=round(position_size, 2),
                pnl=0.0,
                reason=f"Signal: {strategy_name} buy",
                strategy_version=1,
                timestamp=candle.timestamp,
            ))

        elif signal == "sell" and position_side == "long":
            # Close long
            quantity = position_size / entry_price
            exit_value = quantity * candle.close
            pnl = exit_value - position_size
            capital += pnl
            if pnl > 0:
                winning += 1

            trades.append(TradeResponse(
                id=len(trades) + 1,
                agent_id=0,
                symbol=candle.symbol,
                side="sell",
                price=candle.close,
                quantity=round(quantity, 6),
                value=round(exit_value, 2),
                pnl=round(pnl, 2),
                reason=f"Signal: {strategy_name} sell",
                strategy_version=1,
                timestamp=candle.timestamp,
            ))
            position_side = "none"
            position_size = 0.0
            entry_price = 0.0

        # Track drawdown
        if capital > peak:
            peak = capital
        dd = (peak - capital) / peak if peak > 0 else 0
        if dd > max_drawdown:
            max_drawdown = dd

    total = len([t for t in trades if t.side == "sell"])
    return BacktestResult(
        total_trades=total,
        winning_trades=winning,
        win_rate=round(winning / total, 4) if total > 0 else 0.0,
        total_pnl=round(capital - initial_capital, 2),
        max_drawdown=round(max_drawdown, 4),
        trades=trades,
    )
