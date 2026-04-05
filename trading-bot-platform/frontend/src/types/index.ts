export interface Agent {
  id: number;
  name: string;
  description: string;
  symbol: string;
  strategy_name: string;
  strategy_params: Record<string, number>;
  strategy_version: number;
  status: "idle" | "running" | "paused" | "stopped" | "error";
  is_paper: boolean;
  initial_capital: number;
  current_capital: number;
  pnl: number;
  pnl_pct: number;
  total_trades: number;
  winning_trades: number;
  win_rate: number;
  max_drawdown: number;
  position_side: string;
  position_size: number;
  position_entry_price: number;
  max_position_size: number;
  stop_loss_pct: number;
  created_at: string;
  updated_at: string;
}

export interface Trade {
  id: number;
  agent_id: number;
  symbol: string;
  side: "buy" | "sell";
  price: number;
  quantity: number;
  value: number;
  pnl: number;
  reason: string;
  strategy_version: number;
  timestamp: string;
}

export interface AgentLog {
  id: number;
  agent_id: number;
  level: "debug" | "info" | "warning" | "error";
  message: string;
  timestamp: string;
}

export interface Strategy {
  name: string;
  label: string;
  description: string;
  default_params: Record<string, number>;
}

export interface RiskSettings {
  max_position_size: number;
  max_drawdown_pct: number;
  global_kill_switch: boolean;
}

export interface PriceCandle {
  symbol: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
  timestamp: string;
}

export interface AgentCreate {
  name: string;
  description?: string;
  symbol: string;
  strategy_name: string;
  strategy_params: Record<string, number>;
  initial_capital: number;
  max_position_size: number;
  stop_loss_pct: number;
  is_paper: boolean;
}

export interface WsMessage {
  type: "metrics" | "log" | "price";
  agent_id?: number;
  [key: string]: unknown;
}
