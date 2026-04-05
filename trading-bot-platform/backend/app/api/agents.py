from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy import select
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.database import get_db
from app.models.agent import Agent, AgentStatus
from app.models.trade import Trade
from app.models.log import AgentLog
from app.models.strategy import StrategyVersion
from app.api.schemas import (
    AgentCreate, AgentUpdate, AgentResponse,
    TradeResponse, LogResponse, StrategyVersionResponse,
)
from app.services.agent_manager import agent_manager

router = APIRouter(prefix="/api/agents", tags=["agents"])


@router.get("", response_model=list[AgentResponse])
async def list_agents(db: AsyncSession = Depends(get_db)):
    result = await db.execute(select(Agent).order_by(Agent.created_at.desc()))
    agents = result.scalars().all()
    return [_agent_to_response(a) for a in agents]


@router.post("", response_model=AgentResponse, status_code=201)
async def create_agent(data: AgentCreate, db: AsyncSession = Depends(get_db)):
    agent = Agent(
        name=data.name,
        description=data.description,
        symbol=data.symbol.upper(),
        strategy_name=data.strategy_name,
        strategy_params=data.strategy_params,
        initial_capital=data.initial_capital,
        current_capital=data.initial_capital,
        peak_capital=data.initial_capital,
        max_position_size=data.max_position_size,
        stop_loss_pct=data.stop_loss_pct,
        is_paper=data.is_paper,
    )
    db.add(agent)
    await db.flush()
    await db.refresh(agent)

    # Save initial strategy version
    sv = StrategyVersion(
        agent_id=agent.id,
        strategy_name=agent.strategy_name,
        version=1,
        params=agent.strategy_params,
        notes="Initial version",
    )
    db.add(sv)
    return _agent_to_response(agent)


@router.get("/{agent_id}", response_model=AgentResponse)
async def get_agent(agent_id: int, db: AsyncSession = Depends(get_db)):
    agent = await _get_agent(agent_id, db)
    return _agent_to_response(agent)


@router.patch("/{agent_id}", response_model=AgentResponse)
async def update_agent(agent_id: int, data: AgentUpdate, db: AsyncSession = Depends(get_db)):
    agent = await _get_agent(agent_id, db)
    if agent.status == AgentStatus.RUNNING:
        raise HTTPException(400, "Stop agent before updating")

    update_data = data.model_dump(exclude_unset=True)

    if "strategy_params" in update_data:
        agent.strategy_version += 1
        agent.strategy_params = update_data.pop("strategy_params")
        sv = StrategyVersion(
            agent_id=agent.id,
            strategy_name=agent.strategy_name,
            version=agent.strategy_version,
            params=agent.strategy_params,
            notes="Parameter update",
        )
        db.add(sv)

    for key, value in update_data.items():
        setattr(agent, key, value)

    return _agent_to_response(agent)


@router.delete("/{agent_id}", status_code=204)
async def delete_agent(agent_id: int, db: AsyncSession = Depends(get_db)):
    agent = await _get_agent(agent_id, db)
    if agent.status == AgentStatus.RUNNING:
        await agent_manager.stop_agent(agent_id)
    await db.delete(agent)


@router.post("/{agent_id}/start", response_model=AgentResponse)
async def start_agent(agent_id: int, db: AsyncSession = Depends(get_db)):
    agent = await _get_agent(agent_id, db)
    if agent.status == AgentStatus.RUNNING:
        raise HTTPException(400, "Agent is already running")
    agent.status = AgentStatus.RUNNING
    await db.flush()
    await agent_manager.start_agent(agent_id)
    return _agent_to_response(agent)


@router.post("/{agent_id}/stop", response_model=AgentResponse)
async def stop_agent(agent_id: int, db: AsyncSession = Depends(get_db)):
    agent = await _get_agent(agent_id, db)
    if agent.status != AgentStatus.RUNNING:
        raise HTTPException(400, "Agent is not running")
    await agent_manager.stop_agent(agent_id)
    agent.status = AgentStatus.STOPPED
    return _agent_to_response(agent)


@router.get("/{agent_id}/trades", response_model=list[TradeResponse])
async def get_trades(agent_id: int, limit: int = 100, db: AsyncSession = Depends(get_db)):
    result = await db.execute(
        select(Trade)
        .where(Trade.agent_id == agent_id)
        .order_by(Trade.timestamp.desc())
        .limit(limit)
    )
    return result.scalars().all()


@router.get("/{agent_id}/logs", response_model=list[LogResponse])
async def get_logs(agent_id: int, limit: int = 200, db: AsyncSession = Depends(get_db)):
    result = await db.execute(
        select(AgentLog)
        .where(AgentLog.agent_id == agent_id)
        .order_by(AgentLog.timestamp.desc())
        .limit(limit)
    )
    return result.scalars().all()


@router.get("/{agent_id}/versions", response_model=list[StrategyVersionResponse])
async def get_versions(agent_id: int, db: AsyncSession = Depends(get_db)):
    result = await db.execute(
        select(StrategyVersion)
        .where(StrategyVersion.agent_id == agent_id)
        .order_by(StrategyVersion.version.desc())
    )
    return result.scalars().all()


async def _get_agent(agent_id: int, db: AsyncSession) -> Agent:
    result = await db.execute(select(Agent).where(Agent.id == agent_id))
    agent = result.scalar_one_or_none()
    if not agent:
        raise HTTPException(404, f"Agent {agent_id} not found")
    return agent


def _agent_to_response(agent: Agent) -> AgentResponse:
    return AgentResponse(
        id=agent.id,
        name=agent.name,
        description=agent.description,
        symbol=agent.symbol,
        strategy_name=agent.strategy_name,
        strategy_params=agent.strategy_params,
        strategy_version=agent.strategy_version,
        status=agent.status.value,
        is_paper=agent.is_paper,
        initial_capital=agent.initial_capital,
        current_capital=agent.current_capital,
        pnl=agent.pnl,
        pnl_pct=agent.pnl_pct,
        total_trades=agent.total_trades,
        winning_trades=agent.winning_trades,
        win_rate=agent.win_rate,
        max_drawdown=agent.max_drawdown,
        position_side=agent.position_side,
        position_size=agent.position_size,
        position_entry_price=agent.position_entry_price,
        max_position_size=agent.max_position_size,
        stop_loss_pct=agent.stop_loss_pct,
        created_at=agent.created_at,
        updated_at=agent.updated_at,
    )
