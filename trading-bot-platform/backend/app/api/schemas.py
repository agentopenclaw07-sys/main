from datetime import datetime
from typing import Optional

from pydantic import BaseModel, Field


# --- Agent schemas ---

class AgentCreate(BaseModel):
    name: str = Field(..., min_length=1, max_length=100)
    description: str = ""
    symbol: str = Field(..., min_length=1, max_length=20)
    strategy_name: str = Field(default="sma_crossover")
    strategy_params: dict = Field(default_factory=lambda: {"fast_period": 10, "slow_period": 30})
    initial_capital: float = Field(default=10000.0, gt=0)
    max_position_size: float = Field(default=1000.0, gt=0)
    stop_loss_pct: float = Field(default=0.02, gt=0, le=0.5)
    is_paper: bool = True


class AgentUpdate(BaseModel):
    name: Optional[str] = None
    description: Optional[str] = None
    strategy_params: Optional[dict] = None
    max_position_size: Optional[float] = None
    stop_loss_pct: Optional[float] = None


class AgentResponse(BaseModel):
    id: int
    name: str
    description: str
    symbol: str
    strategy_name: str
    strategy_params: dict
    strategy_version: int
    status: str
    is_paper: bool
    initial_capital: float
    current_capital: float
    pnl: float
    pnl_pct: float
    total_trades: int
    winning_trades: int
    win_rate: float
    max_drawdown: float
    position_side: str
    position_size: float
    position_entry_price: float
    max_position_size: float
    stop_loss_pct: float
    created_at: datetime
    updated_at: datetime

    model_config = {"from_attributes": True}


# --- Trade schemas ---

class TradeResponse(BaseModel):
    id: int
    agent_id: int
    symbol: str
    side: str
    price: float
    quantity: float
    value: float
    pnl: float
    reason: str
    strategy_version: int
    timestamp: datetime

    model_config = {"from_attributes": True}


# --- Log schemas ---

class LogResponse(BaseModel):
    id: int
    agent_id: int
    level: str
    message: str
    timestamp: datetime

    model_config = {"from_attributes": True}


# --- Strategy schemas ---

class StrategyVersionResponse(BaseModel):
    id: int
    agent_id: int
    strategy_name: str
    version: int
    params: dict
    backtest_pnl: Optional[float]
    backtest_win_rate: Optional[float]
    backtest_trades: Optional[int]
    notes: str
    created_at: datetime

    model_config = {"from_attributes": True}


# --- Backtest schemas ---

class BacktestRequest(BaseModel):
    agent_id: int
    strategy_name: str = "sma_crossover"
    params: dict = Field(default_factory=lambda: {"fast_period": 10, "slow_period": 30})
    days: int = Field(default=30, ge=1, le=365)


class BacktestResult(BaseModel):
    total_trades: int
    winning_trades: int
    win_rate: float
    total_pnl: float
    max_drawdown: float
    trades: list[TradeResponse]


# --- Risk schemas ---

class RiskSettingsUpdate(BaseModel):
    max_position_size: Optional[float] = None
    max_drawdown_pct: Optional[float] = None
    global_kill_switch: Optional[bool] = None


class RiskSettings(BaseModel):
    max_position_size: float
    max_drawdown_pct: float
    global_kill_switch: bool


# --- Price schemas ---

class PriceCandleResponse(BaseModel):
    symbol: str
    open: float
    high: float
    low: float
    close: float
    volume: float
    timestamp: datetime

    model_config = {"from_attributes": True}
