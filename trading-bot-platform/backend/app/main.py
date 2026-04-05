import asyncio
from contextlib import asynccontextmanager

from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware

from app.core.database import init_db
from app.api.agents import router as agents_router
from app.api.market import router as market_router
from app.api.risk import router as risk_router
from app.api.backtest import router as backtest_router
from app.api.websocket import router as ws_router, redis_listener
from app.services.market_data import price_feed_loop
from app.strategies.registry import list_strategies


@asynccontextmanager
async def lifespan(app: FastAPI):
    # Startup
    await init_db()

    # Start background tasks
    price_task = asyncio.create_task(price_feed_loop(interval=5))
    ws_task = asyncio.create_task(redis_listener())

    yield

    # Shutdown
    price_task.cancel()
    ws_task.cancel()
    from app.services.agent_manager import agent_manager
    await agent_manager.stop_all()


app = FastAPI(
    title="Trading Bot Platform",
    version="1.0.0",
    lifespan=lifespan,
)

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Register routers
app.include_router(agents_router)
app.include_router(market_router)
app.include_router(risk_router)
app.include_router(backtest_router)
app.include_router(ws_router)


@app.get("/api/health")
async def health():
    return {"status": "ok"}


@app.get("/api/strategies")
async def strategies():
    return list_strategies()
