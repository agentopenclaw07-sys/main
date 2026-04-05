#!/bin/bash
# Trading Bot Platform — Local Development Startup
# Requires: Python 3.12+, Node.js 20+, PostgreSQL, Redis
#
# If you don't have PostgreSQL/Redis, this script uses SQLite fallback
# and an in-memory mock for Redis.

set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

echo "=== Trading Bot Platform — Local Dev ==="
echo ""

# --- Backend ---
echo "[1/4] Setting up Python backend..."
cd "$SCRIPT_DIR/backend"

if [ ! -d ".venv" ]; then
    python3 -m venv .venv
fi
source .venv/bin/activate
pip install -q -r requirements.txt 2>/dev/null

echo "[2/4] Starting backend on :8000..."
uvicorn app.main:app --host 0.0.0.0 --port 8000 --reload &
BACKEND_PID=$!
echo "  Backend PID: $BACKEND_PID"

# --- Frontend ---
echo "[3/4] Installing frontend dependencies..."
cd "$SCRIPT_DIR/frontend"

if [ ! -d "node_modules" ]; then
    npm install --silent
fi

echo "[4/4] Starting frontend on :3000..."
npm run dev &
FRONTEND_PID=$!
echo "  Frontend PID: $FRONTEND_PID"

echo ""
echo "=== Ready ==="
echo "  Dashboard:  http://localhost:3000"
echo "  API:        http://localhost:8000"
echo "  API Docs:   http://localhost:8000/docs"
echo ""
echo "Press Ctrl+C to stop all services."

trap "kill $BACKEND_PID $FRONTEND_PID 2>/dev/null; exit" INT TERM
wait
