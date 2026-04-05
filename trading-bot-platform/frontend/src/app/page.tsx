"use client";
import { useEffect, useState, useCallback } from "react";
import { api } from "@/lib/api";
import { useDashboardWebSocket } from "@/hooks/useWebSocket";
import type { Agent, WsMessage, RiskSettings } from "@/types";
import { AgentCard } from "@/components/AgentCard";
import { CreateAgentModal } from "@/components/CreateAgentModal";
import { AgentDetail } from "@/components/AgentDetail";
import { RiskPanel } from "@/components/RiskPanel";

export default function Dashboard() {
  const [agents, setAgents] = useState<Agent[]>([]);
  const [selectedAgent, setSelectedAgent] = useState<number | null>(null);
  const [showCreate, setShowCreate] = useState(false);
  const [showRisk, setShowRisk] = useState(false);
  const [loading, setLoading] = useState(true);

  const loadAgents = useCallback(async () => {
    try {
      const data = await api.listAgents();
      setAgents(data);
    } catch (err) {
      console.error("Failed to load agents:", err);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    loadAgents();
    const interval = setInterval(loadAgents, 10000);
    return () => clearInterval(interval);
  }, [loadAgents]);

  const handleWsMessage = useCallback((msg: WsMessage) => {
    if (msg.type === "metrics" && msg.agent_id) {
      setAgents((prev) =>
        prev.map((a) =>
          a.id === msg.agent_id
            ? {
                ...a,
                current_capital: (msg.current_capital as number) ?? a.current_capital,
                pnl: (msg.pnl as number) ?? a.pnl,
                pnl_pct: (msg.pnl_pct as number) ?? a.pnl_pct,
                total_trades: (msg.total_trades as number) ?? a.total_trades,
                winning_trades: (msg.winning_trades as number) ?? a.winning_trades,
                win_rate: (msg.win_rate as number) ?? a.win_rate,
                max_drawdown: (msg.max_drawdown as number) ?? a.max_drawdown,
                position_side: (msg.position_side as string) ?? a.position_side,
                status: (msg.status as Agent["status"]) ?? a.status,
              }
            : a
        )
      );
    }
  }, []);

  useDashboardWebSocket(handleWsMessage);

  const handleStart = async (id: number) => {
    await api.startAgent(id);
    loadAgents();
  };

  const handleStop = async (id: number) => {
    await api.stopAgent(id);
    loadAgents();
  };

  const handleDelete = async (id: number) => {
    if (!confirm("Delete this agent?")) return;
    await api.deleteAgent(id);
    if (selectedAgent === id) setSelectedAgent(null);
    loadAgents();
  };

  const handleKillAll = async () => {
    if (!confirm("EMERGENCY: Stop ALL agents?")) return;
    await api.killAll();
    loadAgents();
  };

  if (selectedAgent !== null) {
    const agent = agents.find((a) => a.id === selectedAgent);
    if (agent) {
      return (
        <AgentDetail
          agent={agent}
          onBack={() => setSelectedAgent(null)}
          onStart={() => handleStart(agent.id)}
          onStop={() => handleStop(agent.id)}
          onRefresh={loadAgents}
        />
      );
    }
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-6">
      {/* Header */}
      <div className="mb-8 flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-white">Trading Bot Platform</h1>
          <p className="text-sm text-slate-400">
            {agents.length} agent{agents.length !== 1 ? "s" : ""} &middot;{" "}
            {agents.filter((a) => a.status === "running").length} running
          </p>
        </div>
        <div className="flex gap-2">
          <button onClick={() => setShowRisk(true)} className="btn btn-ghost">
            Risk Controls
          </button>
          <button onClick={handleKillAll} className="btn btn-danger">
            Kill All
          </button>
          <button onClick={() => setShowCreate(true)} className="btn btn-primary">
            + New Agent
          </button>
        </div>
      </div>

      {/* Summary cards */}
      <div className="mb-6 grid grid-cols-4 gap-4">
        <SummaryCard
          label="Total PnL"
          value={`$${agents.reduce((s, a) => s + a.pnl, 0).toFixed(2)}`}
          color={agents.reduce((s, a) => s + a.pnl, 0) >= 0 ? "green" : "red"}
        />
        <SummaryCard
          label="Total Capital"
          value={`$${agents.reduce((s, a) => s + a.current_capital, 0).toFixed(2)}`}
        />
        <SummaryCard
          label="Total Trades"
          value={agents.reduce((s, a) => s + a.total_trades, 0).toString()}
        />
        <SummaryCard
          label="Avg Win Rate"
          value={
            agents.length > 0
              ? `${((agents.reduce((s, a) => s + a.win_rate, 0) / agents.length) * 100).toFixed(1)}%`
              : "0%"
          }
        />
      </div>

      {/* Agent grid */}
      {loading ? (
        <div className="flex h-40 items-center justify-center text-slate-500">
          Loading agents...
        </div>
      ) : agents.length === 0 ? (
        <div className="flex h-40 flex-col items-center justify-center text-slate-500">
          <p>No agents yet</p>
          <button onClick={() => setShowCreate(true)} className="btn btn-primary mt-4">
            Create your first agent
          </button>
        </div>
      ) : (
        <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
          {agents.map((agent) => (
            <AgentCard
              key={agent.id}
              agent={agent}
              onClick={() => setSelectedAgent(agent.id)}
              onStart={() => handleStart(agent.id)}
              onStop={() => handleStop(agent.id)}
              onDelete={() => handleDelete(agent.id)}
            />
          ))}
        </div>
      )}

      {/* Modals */}
      {showCreate && (
        <CreateAgentModal
          onClose={() => setShowCreate(false)}
          onCreated={() => {
            setShowCreate(false);
            loadAgents();
          }}
        />
      )}
      {showRisk && <RiskPanel onClose={() => setShowRisk(false)} />}
    </div>
  );
}

function SummaryCard({
  label,
  value,
  color,
}: {
  label: string;
  value: string;
  color?: string;
}) {
  return (
    <div className="card">
      <p className="text-xs text-slate-400">{label}</p>
      <p
        className={`mt-1 text-xl font-semibold ${
          color === "green"
            ? "text-green-400"
            : color === "red"
            ? "text-red-400"
            : "text-white"
        }`}
      >
        {value}
      </p>
    </div>
  );
}
