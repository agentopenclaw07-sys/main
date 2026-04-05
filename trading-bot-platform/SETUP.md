# Trading Bot Platform — Setup Guide

## Architecture

```
Frontend (Next.js :3000) ──REST/WS──▶ Backend (FastAPI :8000)
                                          │
                                    ┌─────┼─────┐
                                    ▼     ▼     ▼
                                 Postgres Redis  Agent Workers
                                 :5432   :6379   (async tasks)
```

## Quick Start (Docker)

```bash
cd trading-bot-platform
docker compose up --build
```

- Frontend: http://localhost:3000
- Backend API: http://localhost:8000
- API docs: http://localhost:8000/docs

## Local Development (without Docker)

### Prerequisites
- Python 3.12+
- Node.js 20+
- PostgreSQL 16+
- Redis 7+

### Backend

```bash
cd backend
python -m venv .venv
source .venv/bin/activate  # or .venv\Scripts\activate on Windows
pip install -r requirements.txt

# Copy and configure environment
cp .env.example .env

# Start the API server
uvicorn app.main:app --reload --port 8000
```

### Frontend

```bash
cd frontend
npm install
npm run dev
```

### Run Tests

```bash
cd backend
pip install pytest
python -m pytest tests/ -v
```

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | /api/health | Health check |
| GET | /api/strategies | List available strategies |
| GET | /api/agents | List all agents |
| POST | /api/agents | Create agent |
| GET | /api/agents/:id | Get agent details |
| PATCH | /api/agents/:id | Update agent |
| DELETE | /api/agents/:id | Delete agent |
| POST | /api/agents/:id/start | Start agent |
| POST | /api/agents/:id/stop | Stop agent |
| GET | /api/agents/:id/trades | Get agent trades |
| GET | /api/agents/:id/logs | Get agent logs |
| GET | /api/agents/:id/versions | Get strategy versions |
| GET | /api/market/candles/:symbol | Get price candles |
| POST | /api/backtest | Run backtest |
| GET | /api/risk | Get risk settings |
| PATCH | /api/risk | Update risk settings |
| POST | /api/risk/kill-all | Emergency kill all agents |

## WebSocket Endpoints

| Endpoint | Description |
|----------|-------------|
| ws://localhost:8000/ws/dashboard | Global updates (all agents + prices) |
| ws://localhost:8000/ws/agent/:id | Per-agent updates (metrics + logs) |

## Strategies

| Name | Description | Parameters |
|------|-------------|------------|
| sma_crossover | SMA crossover signals | fast_period, slow_period |
| ema_crossover | EMA crossover signals | fast_period, slow_period |
| mean_reversion | Mean reversion trading | period, threshold |

## Project Structure

```
trading-bot-platform/
├── docker-compose.yml
├── backend/
│   ├── Dockerfile
│   ├── requirements.txt
│   ├── app/
│   │   ├── main.py              # FastAPI app entry point
│   │   ├── core/
│   │   │   ├── config.py        # Settings & env vars
│   │   │   ├── database.py      # PostgreSQL async engine
│   │   │   └── redis.py         # Redis client
│   │   ├── api/
│   │   │   ├── agents.py        # Agent CRUD endpoints
│   │   │   ├── market.py        # Market data endpoints
│   │   │   ├── risk.py          # Risk control endpoints
│   │   │   ├── backtest.py      # Backtesting endpoint
│   │   │   ├── websocket.py     # WebSocket hub
│   │   │   └── schemas.py       # Pydantic schemas
│   │   ├── models/
│   │   │   ├── agent.py         # Agent DB model
│   │   │   ├── trade.py         # Trade DB model
│   │   │   ├── price.py         # Price candle model
│   │   │   ├── strategy.py      # Strategy version model
│   │   │   └── log.py           # Agent log model
│   │   ├── services/
│   │   │   ├── agent_manager.py # Agent execution engine
│   │   │   ├── market_data.py   # Simulated market data
│   │   │   └── backtester.py    # Backtesting engine
│   │   └── strategies/
│   │       ├── base.py          # Base strategy class
│   │       ├── sma_crossover.py # SMA crossover
│   │       ├── ema_crossover.py # EMA crossover
│   │       ├── mean_reversion.py# Mean reversion
│   │       └── registry.py      # Strategy registry
│   └── tests/
│       └── test_strategies.py
└── frontend/
    ├── Dockerfile
    ├── package.json
    └── src/
        ├── app/
        │   ├── layout.tsx
        │   ├── page.tsx         # Dashboard page
        │   └── globals.css
        ├── components/
        │   ├── AgentCard.tsx     # Agent summary card
        │   ├── AgentDetail.tsx   # Agent detail view
        │   ├── CreateAgentModal.tsx
        │   ├── PriceChart.tsx    # Canvas price chart
        │   ├── LogConsole.tsx    # Log viewer
        │   └── RiskPanel.tsx     # Risk controls modal
        ├── hooks/
        │   └── useWebSocket.ts  # WebSocket hooks
        ├── lib/
        │   └── api.ts           # API client
        └── types/
            └── index.ts         # TypeScript types
```
