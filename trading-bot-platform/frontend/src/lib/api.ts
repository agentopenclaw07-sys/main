import type {
  Agent,
  AgentCreate,
  AgentLog,
  Trade,
  Strategy,
  RiskSettings,
  PriceCandle,
} from "@/types";

const BASE = process.env.NEXT_PUBLIC_API_URL || "http://localhost:8000";

async function request<T>(path: string, opts?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { "Content-Type": "application/json" },
    ...opts,
  });
  if (!res.ok) {
    const body = await res.text();
    throw new Error(`API ${res.status}: ${body}`);
  }
  if (res.status === 204) return undefined as T;
  return res.json();
}

// Agents
export const api = {
  listAgents: () => request<Agent[]>("/api/agents"),
  getAgent: (id: number) => request<Agent>(`/api/agents/${id}`),
  createAgent: (data: AgentCreate) =>
    request<Agent>("/api/agents", { method: "POST", body: JSON.stringify(data) }),
  updateAgent: (id: number, data: Partial<AgentCreate>) =>
    request<Agent>(`/api/agents/${id}`, { method: "PATCH", body: JSON.stringify(data) }),
  deleteAgent: (id: number) =>
    request<void>(`/api/agents/${id}`, { method: "DELETE" }),
  startAgent: (id: number) =>
    request<Agent>(`/api/agents/${id}/start`, { method: "POST" }),
  stopAgent: (id: number) =>
    request<Agent>(`/api/agents/${id}/stop`, { method: "POST" }),
  getTrades: (id: number, limit = 100) =>
    request<Trade[]>(`/api/agents/${id}/trades?limit=${limit}`),
  getLogs: (id: number, limit = 200) =>
    request<AgentLog[]>(`/api/agents/${id}/logs?limit=${limit}`),

  // Market
  getCandles: (symbol: string, limit = 200) =>
    request<PriceCandle[]>(`/api/market/candles/${encodeURIComponent(symbol)}?limit=${limit}`),

  // Strategies
  listStrategies: () => request<Strategy[]>("/api/strategies"),

  // Risk
  getRiskSettings: () => request<RiskSettings>("/api/risk"),
  updateRiskSettings: (data: Partial<RiskSettings>) =>
    request<RiskSettings>("/api/risk", { method: "PATCH", body: JSON.stringify(data) }),
  killAll: () => request<{ message: string }>("/api/risk/kill-all", { method: "POST" }),

  // Health
  health: () => request<{ status: string }>("/api/health"),
};
