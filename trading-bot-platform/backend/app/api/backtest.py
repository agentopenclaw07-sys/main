from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.database import get_db
from app.models.agent import Agent
from app.models.price import PriceCandle
from app.api.schemas import BacktestRequest, BacktestResult
from app.services.backtester import run_backtest

router = APIRouter(prefix="/api/backtest", tags=["backtest"])


@router.post("", response_model=BacktestResult)
async def backtest(data: BacktestRequest, db: AsyncSession = Depends(get_db)):
    # Get agent for symbol info
    result = await db.execute(select(Agent).where(Agent.id == data.agent_id))
    agent = result.scalar_one_or_none()
    if not agent:
        raise HTTPException(404, "Agent not found")

    # Get price data
    candle_result = await db.execute(
        select(PriceCandle)
        .where(PriceCandle.symbol == agent.symbol)
        .order_by(PriceCandle.timestamp.asc())
        .limit(data.days * 24)  # hourly candles
    )
    candles = candle_result.scalars().all()
    if len(candles) < 50:
        raise HTTPException(400, "Not enough price data for backtest")

    return run_backtest(
        candles=candles,
        strategy_name=data.strategy_name,
        params=data.params,
        initial_capital=agent.initial_capital,
    )
