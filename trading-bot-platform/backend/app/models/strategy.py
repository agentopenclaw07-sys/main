from datetime import datetime

from sqlalchemy import String, Integer, Float, DateTime, JSON
from sqlalchemy.orm import Mapped, mapped_column

from app.core.database import Base


class StrategyVersion(Base):
    __tablename__ = "strategy_versions"

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    agent_id: Mapped[int] = mapped_column(Integer, index=True)
    strategy_name: Mapped[str] = mapped_column(String(100))
    version: Mapped[int] = mapped_column(Integer)
    params: Mapped[dict] = mapped_column(JSON)
    backtest_pnl: Mapped[float] = mapped_column(Float, nullable=True)
    backtest_win_rate: Mapped[float] = mapped_column(Float, nullable=True)
    backtest_trades: Mapped[int] = mapped_column(Integer, nullable=True)
    notes: Mapped[str] = mapped_column(String(500), default="")
    created_at: Mapped[datetime] = mapped_column(DateTime, default=datetime.utcnow)
