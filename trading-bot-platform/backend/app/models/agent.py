import enum
from datetime import datetime

from sqlalchemy import String, Float, DateTime, Boolean, Integer, JSON
from sqlalchemy.orm import Mapped, mapped_column

from app.core.database import Base


class AgentStatus(str, enum.Enum):
    IDLE = "idle"
    RUNNING = "running"
    PAUSED = "paused"
    STOPPED = "stopped"
    ERROR = "error"


class Agent(Base):
    __tablename__ = "agents"

    id: Mapped[int] = mapped_column(primary_key=True, autoincrement=True)
    name: Mapped[str] = mapped_column(String(100), nullable=False)
    description: Mapped[str] = mapped_column(String(500), default="")
    symbol: Mapped[str] = mapped_column(String(20), nullable=False)  # e.g. BTC/USDT
    strategy_name: Mapped[str] = mapped_column(String(100), nullable=False)
    strategy_params: Mapped[dict] = mapped_column(JSON, default=dict)
    strategy_version: Mapped[int] = mapped_column(Integer, default=1)

    status: Mapped[AgentStatus] = mapped_column(
        String(20), default=AgentStatus.IDLE
    )
    is_paper: Mapped[bool] = mapped_column(Boolean, default=True)

    # Performance metrics
    initial_capital: Mapped[float] = mapped_column(Float, default=10000.0)
    current_capital: Mapped[float] = mapped_column(Float, default=10000.0)
    pnl: Mapped[float] = mapped_column(Float, default=0.0)
    pnl_pct: Mapped[float] = mapped_column(Float, default=0.0)
    total_trades: Mapped[int] = mapped_column(Integer, default=0)
    winning_trades: Mapped[int] = mapped_column(Integer, default=0)
    max_drawdown: Mapped[float] = mapped_column(Float, default=0.0)
    peak_capital: Mapped[float] = mapped_column(Float, default=10000.0)

    # Risk controls
    max_position_size: Mapped[float] = mapped_column(Float, default=1000.0)
    stop_loss_pct: Mapped[float] = mapped_column(Float, default=0.02)  # 2%

    # Position state
    position_side: Mapped[str] = mapped_column(String(10), default="none")  # none/long/short
    position_size: Mapped[float] = mapped_column(Float, default=0.0)
    position_entry_price: Mapped[float] = mapped_column(Float, default=0.0)

    created_at: Mapped[datetime] = mapped_column(DateTime, default=datetime.utcnow)
    updated_at: Mapped[datetime] = mapped_column(
        DateTime, default=datetime.utcnow, onupdate=datetime.utcnow
    )

    @property
    def win_rate(self) -> float:
        if self.total_trades == 0:
            return 0.0
        return self.winning_trades / self.total_trades
