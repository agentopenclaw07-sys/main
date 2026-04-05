from fastapi import APIRouter, Depends, Query
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.database import get_db
from app.models.price import PriceCandle
from app.api.schemas import PriceCandleResponse

router = APIRouter(prefix="/api/market", tags=["market"])


@router.get("/candles/{symbol}", response_model=list[PriceCandleResponse])
async def get_candles(
    symbol: str,
    limit: int = Query(default=200, le=1000),
    db: AsyncSession = Depends(get_db),
):
    result = await db.execute(
        select(PriceCandle)
        .where(PriceCandle.symbol == symbol.upper())
        .order_by(PriceCandle.timestamp.desc())
        .limit(limit)
    )
    candles = result.scalars().all()
    return list(reversed(candles))
