"use client";
import type { Agent } from "@/types";

interface Props {
  agent: Agent;
  onClick: () => void;
  onStart: () => void;
  onStop: () => void;
  onDelete: () => void;
}

export function AgentCard({ agent, onClick, onStart, onStop, onDelete }: Props) {
  const statusBadge = {
    running: "badge-running",
    stopped: "badge-stopped",
    error: "badge-error",
    idle: "badge-idle",
    paused: "badge-stopped",
  }[agent.status];

  return (
    <div className="card cursor-pointer hover:border-slate-600" onClick={onClick}>
      <div className="mb-3 flex items-start justify-between">
        <div>
          <h3 className="font-semibold text-white">{agent.name}</h3>
          <p className="text-xs text-slate-400">
            {agent.symbol} &middot; {agent.strategy_name}
          </p>
        </div>
        <span className={`badge ${statusBadge}`}>{agent.status}</span>
      </div>

      <div className="mb-3 grid grid-cols-3 gap-2 text-center">
        <Metric
          label="PnL"
          value={`$${agent.pnl.toFixed(2)}`}
          color={agent.pnl >= 0 ? "green" : "red"}
        />
        <Metric label="Trades" value={agent.total_trades.toString()} />
        <Metric label="Win Rate" value={`${(agent.win_rate * 100).toFixed(1)}%`} />
      </div>

      <div className="mb-2 text-xs text-slate-500">
        Capital: ${agent.current_capital.toFixed(2)} &middot; Drawdown:{" "}
        {(agent.max_drawdown * 100).toFixed(2)}%
        {agent.position_side !== "none" && (
          <span className="ml-2 text-yellow-400">
            {agent.position_side.toUpperCase()} ${agent.position_size.toFixed(2)}
          </span>
        )}
      </div>

      <div className="flex gap-2" onClick={(e) => e.stopPropagation()}>
        {agent.status === "running" ? (
          <button onClick={onStop} className="btn btn-ghost flex-1 text-xs">
            Stop
          </button>
        ) : (
          <button onClick={onStart} className="btn btn-success flex-1 text-xs">
            Start
          </button>
        )}
        <button onClick={onDelete} className="btn btn-ghost text-xs text-red-400">
          Delete
        </button>
      </div>
    </div>
  );
}

function Metric({
  label,
  value,
  color,
}: {
  label: string;
  value: string;
  color?: string;
}) {
  return (
    <div>
      <p className="text-[10px] text-slate-500">{label}</p>
      <p
        className={`text-sm font-medium ${
          color === "green"
            ? "text-green-400"
            : color === "red"
            ? "text-red-400"
            : "text-slate-200"
        }`}
      >
        {value}
      </p>
    </div>
  );
}
