"""Redis client with in-memory fallback for local dev without Redis."""
import asyncio
import json
import logging

from app.core.config import settings

logger = logging.getLogger(__name__)

redis_client = None


class InMemoryPubSub:
    """Minimal pub/sub replacement when Redis isn't available."""

    def __init__(self):
        self._subscribers: dict[str, list[asyncio.Queue]] = {}

    async def subscribe(self, *channels: str):
        for ch in channels:
            self._subscribers.setdefault(ch, [])

    async def listen(self):
        # Create a queue for this listener
        queues = []
        for ch, q_list in self._subscribers.items():
            q = asyncio.Queue()
            q_list.append(q)
            queues.append((ch, q))
        while True:
            for ch, q in queues:
                try:
                    data = q.get_nowait()
                    yield {"type": "message", "channel": ch, "data": data}
                except asyncio.QueueEmpty:
                    pass
            await asyncio.sleep(0.1)


class InMemoryRedis:
    """Minimal Redis replacement for local dev."""

    def __init__(self):
        self._store: dict[str, str] = {}
        self._pubsub = InMemoryPubSub()

    async def get(self, key: str) -> str | None:
        return self._store.get(key)

    async def set(self, key: str, value: str, ex: int | None = None):
        self._store[key] = value

    async def publish(self, channel: str, message: str):
        for q in self._pubsub._subscribers.get(channel, []):
            await q.put(message)

    def pubsub(self):
        return self._pubsub

    async def ping(self):
        return True


async def init_redis():
    """Try to connect to Redis; fall back to in-memory if unavailable."""
    global redis_client

    if not settings.use_redis:
        logger.info("Redis disabled — using in-memory fallback")
        redis_client = InMemoryRedis()
        return

    try:
        import redis.asyncio as redis
        client = redis.from_url(settings.redis_url, decode_responses=True)
        await client.ping()
        redis_client = client
        logger.info("Connected to Redis at %s", settings.redis_url)
    except Exception as e:
        logger.warning("Redis unavailable (%s) — using in-memory fallback", e)
        settings.use_redis = False
        redis_client = InMemoryRedis()


async def get_redis():
    return redis_client
