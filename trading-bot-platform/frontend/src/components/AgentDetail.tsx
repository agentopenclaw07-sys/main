"use client";
import { useEffect, useState, useCallback } from "react";
import { api } from "@/lib/api";
import { useAgentWebSocket } from "@/hooks/useWebSocket";
import type { Agent, Trade, AgentLog, WsMessage } from "@/types";
import { PriceChart } from "@/components/PriceChart";
import { LogConsole } from "@/components/LogConsole";

interface Props {
  agent: Agent;
  onBack: () => void;
  onStart: () => void;
  onStop: () => void;
  onRefresh: () => void;
}

export function AgentDetail({ agent, onBack, onStart, onStop, onRefresh }: Props) {
  const [trades, setTrades] = useState<Trade[]>([]);
  const [logs, setLogs] = useState<AgentLog[]>([]);
  const [tab, setTab] = useState<"trades" | "logs" | "chart">("chart");
  const [liveMetrics, setLiveMetrics] = useState({
    pnl: agent.pnl,
    current_capital: agent.current_capital,
    total_trades: agent.total_trades,
    win_rate: agent.win_rate,
    max_drawdown: agent.max_drawdown,
    position_side: agent.position_side,
    position_size: agent.position_size,
  });

  useEffect(() => {
    api.getTrades(agent.id).then(setTrades).catch(console.error);
    api.getLogs(agent.id).then(setLogs).catch(console.error);
  }, [agent.id]);

  const handleWs = useCallback((msg: WsMessage) => {
    if (msg.type === "metrics") {
      setLiveMetrics({
        pnl: (msg.pnl as number) ?? liveMetrics.pnl,
        current_capital: (msg.current_capital as number) ?? liveMetrics.current_capital,
        total_trades: (msg.total_trades as number) ?? liveMetrics.total_trades,
        win_rate: (msg.win_rate as number) ?? liveMetrics.win_rate,
        max_drawdown: (msg.max_drawdown as number) ?? liveMetrics.max_drawdown,
        position_side: (msg.position_side as string) ?? liveMetrics.position_side,
        position_size: (msg.position_size as number) ?? liveMetrics.position_size,
      });
    } else if (msg.type === "log") {
      setLogs((prev) => [
        {
          id: Date.now(),
          agent_id: agent.id,
          level: (msg.level as AgentLog["level"]) || "info",
          message: (msg.message as string) || "",
          timestamp: (msg.timestamp as string) || new Date().toISOString(),
        },
        ...prev.slice(0, 199),
      ]);
    }
  }, [agent.id, liveMetrics]);

  useAgentWebSocket(agent.id, handleWs);

  const statusBadge = {
    running: "badge-running",
    stopped: "badge-stopped",
    error: "badge-error",
    idle: "badge-idle",
    paused: "badge-stopped",
  }[agent.status];

  return (
    <div className="mx-auto max-w-7xl px-4 py-6">
      {/* Header */}
      <div className="mb-6 flex items-center gap-4">
        <button onClick={onBack} className="btn btn-ghost text-sm">
          &larr; Back
        </button>
        <div className="flex-1">
          <div className="flex items-center gap-3">
            <h1 className="text-xl font-bold">{agent.name}</h1>
            <span className={`badge ${statusBadge}`}>{agent.status}</span>
            <span className="text-sm text-slate-400">
              {agent.symbol} &middot; v{agent.strategy_version}
            </span>
          </div>
          <p className="text-xs text-slate-500">{agent.description || agent.strategy_name}</p>
        </div>
        <div className="flex gap-2">
          {agent.status === "running" ? (
            <button onClick={onStop} className="btn btn-danger">
              Stop
            </button>
          ) : (
            <button onClick={onStart} className="btn btn-success">
              Start
            </button>
          )}
        </div>
      </div>

      {/* Metrics row */}
      <div className="mb-6 grid grid-cols-6 gap-3">
        <MetricCard
          label="PnL"
          value={`$${liveMetrics.pnl.toFixed(2)}`}
          sub={`${(agent.pnl_pct * 100).toFixed(2)}%`}
          color={liveMetrics.pnl >= 0 ? "green" : "red"}
        />
        <MetricCard label="Capital" value={`$${liveMetrics.current_capital.toFixed(2)}`} />
        <MetricCard label="Total Trades" value={liveMetrics.total_trades.toString()} />
        <MetricCard label="Win Rate" value={`${(liveMetrics.win_rate * 100).toFixed(1)}%`} />
        <MetricCard
          label="Max Drawdown"
          value={`${(liveMetrics.max_drawdown * 100).toFixed(2)}%`}
          color={liveMetrics.max_drawdown > 0.05 ? "red" : undefined}
        />
        <MetricCard
          label="Position"
          value={
            liveMetrics.position_side !== "none"
              ? `${liveMetrics.position_side.toUpperCase()} $${liveMetrics.position_size.toFixed(0)}`
              : "None"
          }
          color={liveMetrics.position_side !== "none" ? "yellow" : undefined}
        />
      </div>

      {/* Tabs */}
      <div className="mb-4 flex gap-1 border-b border-slate-700">
        {(["chart", "trades", "logs"] as const).map((t) => (
          <button
            key={t}
            onClick={() => setTab(t)}
            className={`px-4 py-2 text-sm font-medium transition-colors ${
              tab === t
                ? "border-b-2 border-indigo-500 text-white"
                : "text-slate-400 hover:text-slate-200"
            }`}
          >
            {t.charAt(0).toUpperCase() + t.slice(1)}
          </button>
        ))}
      </div>

      {/* Tab content */}
      {tab === "chart" && <PriceChart symbol={agent.symbol} trades={trades} />}
      {tab === "trades" && <TradesTable trades={trades} />}
      {tab === "logs" && <LogConsole logs={logs} />}
    </div>
  );
}

function MetricCard({
  label,
  value,
  sub,
  color,
}: {
  label: string;
  value: string;
  sub?: string;
  color?: string;
}) {
  const textColor =
    color === "green"
      ? "text-green-400"
      : color === "red"
      ? "text-red-400"
      : color === "yellow"
      ? "text-yellow-400"
      : "text-white";
  return (
    <div className="card">
      <p className="text-[10px] text-slate-500">{label}</p>
      <p className={`text-lg font-semibold ${textColor}`}>{value}</p>
      {sub && <p className={`text-xs ${textColor}`}>{sub}</p>}
    </div>
  );
}

function TradesTable({ trades }: { trades: Trade[] }) {
  if (trades.length === 0) {
    return <div className="py-12 text-center text-slate-500">No trades yet</div>;
  }
  return (
    <div className="overflow-auto">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-slate-700 text-left text-xs text-slate-400">
            <th className="pb-2">Time</th>
            <th className="pb-2">Side</th>
            <th className="pb-2">Price</th>
            <th className="pb-2">Qty</th>
            <th className="pb-2">Value</th>
            <th className="pb-2">PnL</th>
            <th className="pb-2">Reason</th>
          </tr>
        </thead>
        <tbody>
          {trades.map((t) => (
            <tr key={t.id} className="border-b border-slate-800">
              <td className="py-2 text-slate-400">
                {new Date(t.timestamp).toLocaleString()}
              </td>
              <td
                className={`py-2 font-medium ${
                  t.side === "buy" ? "text-green-400" : "text-red-400"
                }`}
              >
                {t.side.toUpperCase()}
              </td>
              <td className="py-2">${t.price.toFixed(2)}</td>
              <td className="py-2">{t.quantity.toFixed(6)}</td>
              <td className="py-2">${t.value.toFixed(2)}</td>
              <td
                className={`py-2 ${
                  t.pnl > 0 ? "text-green-400" : t.pnl < 0 ? "text-red-400" : ""
                }`}
              >
                {t.pnl !== 0 ? `$${t.pnl.toFixed(2)}` : "—"}
              </td>
              <td className="py-2 text-xs text-slate-500">{t.reason}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
