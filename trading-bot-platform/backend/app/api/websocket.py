import asyncio
import json

from fastapi import APIRouter, WebSocket, WebSocketDisconnect

from app.core.redis import redis_client

router = APIRouter()


class ConnectionManager:
    def __init__(self):
        self.active: dict[int, list[WebSocket]] = {}  # agent_id -> connections
        self.global_connections: list[WebSocket] = []

    async def connect_agent(self, websocket: WebSocket, agent_id: int):
        await websocket.accept()
        self.active.setdefault(agent_id, []).append(websocket)

    async def connect_global(self, websocket: WebSocket):
        await websocket.accept()
        self.global_connections.append(websocket)

    def disconnect_agent(self, websocket: WebSocket, agent_id: int):
        if agent_id in self.active:
            self.active[agent_id] = [c for c in self.active[agent_id] if c != websocket]

    def disconnect_global(self, websocket: WebSocket):
        self.global_connections = [c for c in self.global_connections if c != websocket]

    async def broadcast_to_agent(self, agent_id: int, data: dict):
        connections = self.active.get(agent_id, [])
        dead = []
        for conn in connections:
            try:
                await conn.send_json(data)
            except Exception:
                dead.append(conn)
        for conn in dead:
            self.disconnect_agent(conn, agent_id)

    async def broadcast_global(self, data: dict):
        dead = []
        for conn in self.global_connections:
            try:
                await conn.send_json(data)
            except Exception:
                dead.append(conn)
        for conn in dead:
            self.disconnect_global(conn)


manager = ConnectionManager()


@router.websocket("/ws/agent/{agent_id}")
async def agent_ws(websocket: WebSocket, agent_id: int):
    await manager.connect_agent(websocket, agent_id)
    try:
        while True:
            await websocket.receive_text()  # keep alive
    except WebSocketDisconnect:
        manager.disconnect_agent(websocket, agent_id)


@router.websocket("/ws/dashboard")
async def dashboard_ws(websocket: WebSocket):
    await manager.connect_global(websocket)
    try:
        while True:
            await websocket.receive_text()
    except WebSocketDisconnect:
        manager.disconnect_global(websocket)


async def redis_listener():
    """Subscribe to Redis pub/sub and forward to WebSocket clients."""
    pubsub = redis_client.pubsub()
    await pubsub.subscribe("agent_updates", "price_updates")
    async for message in pubsub.listen():
        if message["type"] != "message":
            continue
        try:
            data = json.loads(message["data"])
            channel = message["channel"]
            if channel == "agent_updates":
                agent_id = data.get("agent_id")
                if agent_id:
                    await manager.broadcast_to_agent(agent_id, data)
                await manager.broadcast_global(data)
            elif channel == "price_updates":
                await manager.broadcast_global(data)
        except Exception:
            pass
