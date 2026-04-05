import os
from pydantic_settings import BaseSettings


def _default_db_url() -> str:
    """Use SQLite if no DATABASE_URL is set — zero-dependency local dev."""
    if os.environ.get("DATABASE_URL"):
        return os.environ["DATABASE_URL"]
    db_dir = os.path.join(os.path.dirname(__file__), "..", "..", "data")
    os.makedirs(db_dir, exist_ok=True)
    return f"sqlite+aiosqlite:///{os.path.abspath(os.path.join(db_dir, 'tradingbot.db'))}"


class Settings(BaseSettings):
    app_name: str = "Trading Bot Platform"
    environment: str = "development"
    database_url: str = _default_db_url()
    redis_url: str = "redis://localhost:6379/0"
    use_redis: bool = True  # auto-disabled if Redis is unreachable

    # Risk controls
    max_position_size: float = 10000.0  # max USD per position
    max_drawdown_pct: float = 0.10  # 10% max drawdown before kill
    global_kill_switch: bool = False

    # Market data
    price_fetch_interval: int = 5  # seconds

    model_config = {"env_file": ".env", "env_file_encoding": "utf-8"}


settings = Settings()
