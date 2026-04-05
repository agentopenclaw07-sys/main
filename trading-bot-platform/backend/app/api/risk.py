from fastapi import APIRouter, Depends
from sqlalchemy import select, update
from sqlalchemy.ext.asyncio import AsyncSession

from app.core.config import settings
from app.core.database import get_db
from app.models.agent import Agent, AgentStatus
from app.api.schemas import RiskSettings, RiskSettingsUpdate
from app.services.agent_manager import agent_manager

router = APIRouter(prefix="/api/risk", tags=["risk"])


@router.get("", response_model=RiskSettings)
async def get_risk_settings():
    return RiskSettings(
        max_position_size=settings.max_position_size,
        max_drawdown_pct=settings.max_drawdown_pct,
        global_kill_switch=settings.global_kill_switch,
    )


@router.patch("", response_model=RiskSettings)
async def update_risk_settings(data: RiskSettingsUpdate):
    if data.max_position_size is not None:
        settings.max_position_size = data.max_position_size
    if data.max_drawdown_pct is not None:
        settings.max_drawdown_pct = data.max_drawdown_pct
    if data.global_kill_switch is not None:
        settings.global_kill_switch = data.global_kill_switch

    return RiskSettings(
        max_position_size=settings.max_position_size,
        max_drawdown_pct=settings.max_drawdown_pct,
        global_kill_switch=settings.global_kill_switch,
    )


@router.post("/kill-all", status_code=200)
async def kill_all_agents(db: AsyncSession = Depends(get_db)):
    """Emergency kill switch — stops all running agents."""
    settings.global_kill_switch = True
    await agent_manager.stop_all()
    await db.execute(
        update(Agent)
        .where(Agent.status == AgentStatus.RUNNING)
        .values(status=AgentStatus.STOPPED)
    )
    return {"message": "All agents stopped", "killed": True}
