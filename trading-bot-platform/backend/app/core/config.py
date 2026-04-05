from pydantic_settings import BaseSettings


class Settings(BaseSettings):
    app_name: str = "Trading Bot Platform"
    environment: str = "development"
    database_url: str = "postgresql+asyncpg://tradingbot:tradingbot_dev@localhost:5432/tradingbot"
    redis_url: str = "redis://localhost:6379/0"

    # Risk controls
    max_position_size: float = 10000.0  # max USD per position
    max_drawdown_pct: float = 0.10  # 10% max drawdown before kill
    global_kill_switch: bool = False

    # Market data
    price_fetch_interval: int = 5  # seconds

    model_config = {"env_file": ".env"}


settings = Settings()
