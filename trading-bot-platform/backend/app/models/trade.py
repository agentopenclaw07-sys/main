import enum
from datetime import datetime

from sqlalchemy import String, Float, Enum, DateTime, Integer, ForeignKey
from sqlalchemy.orm import Mapped, mapped_column

from app.core.database import Base


class TradeSide(str, enum.Enum):
    BUY = "buy"
    SELL = "sell"


class Trade(Base):
    __tablename__ = "trades"

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    agent_id: Mapped[int] = mapped_column(ForeignKey("agents.id"), index=True)
    symbol: Mapped[str] = mapped_column(String(20))
    side: Mapped[TradeSide] = mapped_column(Enum(TradeSide))
    price: Mapped[float] = mapped_column(Float)
    quantity: Mapped[float] = mapped_column(Float)
    value: Mapped[float] = mapped_column(Float)  # price * quantity
    pnl: Mapped[float] = mapped_column(Float, default=0.0)  # realized PnL for this trade
    reason: Mapped[str] = mapped_column(String(200), default="")
    strategy_version: Mapped[int] = mapped_column(Integer, default=1)
    timestamp: Mapped[datetime] = mapped_column(DateTime, default=datetime.utcnow, index=True)
