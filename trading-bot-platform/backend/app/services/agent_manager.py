"""Agent Manager — orchestrates starting/stopping of trading agent workers."""
import asyncio
import json
from datetime import datetime

from sqlalchemy import select

from app.core.database import async_session
from app.core.config import settings
from app.core.redis import redis_client
from app.models.agent import Agent, AgentStatus
from app.models.trade import Trade, TradeSide
from app.models.log import AgentLog, LogLevel
from app.strategies.registry import get_strategy


class AgentManager:
    def __init__(self):
        self._tasks: dict[int, asyncio.Task] = {}

    async def start_agent(self, agent_id: int):
        if agent_id in self._tasks and not self._tasks[agent_id].done():
            return
        task = asyncio.create_task(self._run_agent(agent_id))
        self._tasks[agent_id] = task

    async def stop_agent(self, agent_id: int):
        task = self._tasks.get(agent_id)
        if task and not task.done():
            task.cancel()
            try:
                await task
            except asyncio.CancelledError:
                pass
        self._tasks.pop(agent_id, None)

    async def stop_all(self):
        for agent_id in list(self._tasks.keys()):
            await self.stop_agent(agent_id)

    async def _run_agent(self, agent_id: int):
        """Main agent execution loop."""
        await self._log(agent_id, LogLevel.INFO, "Agent started")

        try:
            while True:
                if settings.global_kill_switch:
                    await self._log(agent_id, LogLevel.WARNING, "Kill switch activated — stopping")
                    break

                async with async_session() as db:
                    result = await db.execute(select(Agent).where(Agent.id == agent_id))
                    agent = result.scalar_one_or_none()
                    if not agent or agent.status != AgentStatus.RUNNING:
                        break

                    # Get latest price from Redis
                    price_data = await redis_client.get(f"price:{agent.symbol}")
                    if not price_data:
                        await asyncio.sleep(2)
                        continue

                    tick = json.loads(price_data)
                    current_price = tick["close"]

                    # Check stop-loss
                    if agent.position_side == "long":
                        loss_pct = (agent.position_entry_price - current_price) / agent.position_entry_price
                        if loss_pct >= agent.stop_loss_pct:
                            await self._close_position(db, agent, current_price, "Stop-loss triggered")
                            await db.commit()
                            await asyncio.sleep(settings.price_fetch_interval)
                            continue

                    # Get historical prices for strategy
                    price_history = await self._get_price_history(agent.symbol)

                    # Evaluate strategy
                    strategy = get_strategy(agent.strategy_name, agent.strategy_params)
                    signal = strategy.evaluate(price_history)

                    if signal == "buy" and agent.position_side == "none":
                        await self._open_position(db, agent, current_price)
                    elif signal == "sell" and agent.position_side == "long":
                        await self._close_position(db, agent, current_price, "Strategy signal: sell")

                    # Update drawdown
                    if agent.current_capital > agent.peak_capital:
                        agent.peak_capital = agent.current_capital
                    if agent.peak_capital > 0:
                        dd = (agent.peak_capital - agent.current_capital) / agent.peak_capital
                        if dd > agent.max_drawdown:
                            agent.max_drawdown = round(dd, 4)

                    # Check max drawdown risk control
                    if agent.max_drawdown >= settings.max_drawdown_pct:
                        await self._log(agent_id, LogLevel.WARNING, f"Max drawdown {agent.max_drawdown:.2%} exceeded limit")
                        agent.status = AgentStatus.STOPPED
                        await db.commit()
                        break

                    await db.commit()

                    # Broadcast update
                    await self._broadcast_update(agent, current_price)

                await asyncio.sleep(settings.price_fetch_interval)

        except asyncio.CancelledError:
            await self._log(agent_id, LogLevel.INFO, "Agent stopped by user")
        except Exception as e:
            await self._log(agent_id, LogLevel.ERROR, f"Agent error: {str(e)}")
            async with async_session() as db:
                result = await db.execute(select(Agent).where(Agent.id == agent_id))
                agent = result.scalar_one_or_none()
                if agent:
                    agent.status = AgentStatus.ERROR
                    await db.commit()

    async def _open_position(self, db, agent: Agent, price: float):
        size = min(agent.max_position_size, agent.current_capital * 0.1)
        if size <= 0:
            return

        quantity = size / price
        agent.position_side = "long"
        agent.position_size = round(size, 2)
        agent.position_entry_price = round(price, 2)

        trade = Trade(
            agent_id=agent.id,
            symbol=agent.symbol,
            side=TradeSide.BUY,
            price=round(price, 2),
            quantity=round(quantity, 6),
            value=round(size, 2),
            pnl=0.0,
            reason="Strategy signal: buy",
            strategy_version=agent.strategy_version,
        )
        db.add(trade)
        agent.total_trades += 1
        await self._log(agent.id, LogLevel.INFO, f"BUY {quantity:.6f} {agent.symbol} @ {price:.2f}")

    async def _close_position(self, db, agent: Agent, price: float, reason: str):
        if agent.position_side != "long" or agent.position_entry_price <= 0:
            return

        quantity = agent.position_size / agent.position_entry_price
        exit_value = quantity * price
        pnl = exit_value - agent.position_size

        agent.current_capital += pnl
        agent.pnl = round(agent.current_capital - agent.initial_capital, 2)
        agent.pnl_pct = round(agent.pnl / agent.initial_capital, 4)
        if pnl > 0:
            agent.winning_trades += 1

        trade = Trade(
            agent_id=agent.id,
            symbol=agent.symbol,
            side=TradeSide.SELL,
            price=round(price, 2),
            quantity=round(quantity, 6),
            value=round(exit_value, 2),
            pnl=round(pnl, 2),
            reason=reason,
            strategy_version=agent.strategy_version,
        )
        db.add(trade)
        agent.total_trades += 1
        agent.position_side = "none"
        agent.position_size = 0.0
        agent.position_entry_price = 0.0

        await self._log(
            agent.id, LogLevel.INFO,
            f"SELL {quantity:.6f} {agent.symbol} @ {price:.2f} | PnL: {pnl:+.2f}"
        )

    async def _get_price_history(self, symbol: str) -> list[float]:
        """Get recent prices from the database for strategy evaluation."""
        from app.models.price import PriceCandle

        async with async_session() as db:
            result = await db.execute(
                select(PriceCandle.close)
                .where(PriceCandle.symbol == symbol)
                .order_by(PriceCandle.timestamp.desc())
                .limit(100)
            )
            prices = [row[0] for row in result.all()]
            return list(reversed(prices))

    async def _log(self, agent_id: int, level: LogLevel, message: str):
        async with async_session() as db:
            log = AgentLog(agent_id=agent_id, level=level, message=message)
            db.add(log)
            await db.commit()

        await redis_client.publish("agent_updates", json.dumps({
            "type": "log",
            "agent_id": agent_id,
            "level": level.value,
            "message": message,
            "timestamp": datetime.utcnow().isoformat(),
        }))

    async def _broadcast_update(self, agent: Agent, current_price: float):
        await redis_client.publish("agent_updates", json.dumps({
            "type": "metrics",
            "agent_id": agent.id,
            "status": agent.status.value,
            "current_capital": agent.current_capital,
            "pnl": agent.pnl,
            "pnl_pct": agent.pnl_pct,
            "total_trades": agent.total_trades,
            "winning_trades": agent.winning_trades,
            "win_rate": agent.win_rate,
            "max_drawdown": agent.max_drawdown,
            "position_side": agent.position_side,
            "position_size": agent.position_size,
            "current_price": current_price,
            "timestamp": datetime.utcnow().isoformat(),
        }))


# Singleton
agent_manager = AgentManager()
