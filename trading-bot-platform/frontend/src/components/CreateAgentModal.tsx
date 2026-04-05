"use client";
import { useState, useEffect } from "react";
import { api } from "@/lib/api";
import type { Strategy } from "@/types";

interface Props {
  onClose: () => void;
  onCreated: () => void;
}

const SYMBOLS = ["BTC/USDT", "ETH/USDT", "SOL/USDT"];

export function CreateAgentModal({ onClose, onCreated }: Props) {
  const [strategies, setStrategies] = useState<Strategy[]>([]);
  const [name, setName] = useState("");
  const [symbol, setSymbol] = useState("BTC/USDT");
  const [strategyName, setStrategyName] = useState("sma_crossover");
  const [capital, setCapital] = useState("10000");
  const [maxPos, setMaxPos] = useState("1000");
  const [stopLoss, setStopLoss] = useState("0.02");
  const [error, setError] = useState("");
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    api.listStrategies().then(setStrategies).catch(console.error);
  }, []);

  const selectedStrategy = strategies.find((s) => s.name === strategyName);
  const [params, setParams] = useState<Record<string, number>>({});

  useEffect(() => {
    if (selectedStrategy) {
      setParams(selectedStrategy.default_params);
    }
  }, [selectedStrategy]);

  const handleSubmit = async () => {
    if (!name.trim()) {
      setError("Name is required");
      return;
    }
    setSubmitting(true);
    setError("");
    try {
      await api.createAgent({
        name: name.trim(),
        symbol,
        strategy_name: strategyName,
        strategy_params: params,
        initial_capital: parseFloat(capital),
        max_position_size: parseFloat(maxPos),
        stop_loss_pct: parseFloat(stopLoss),
        is_paper: true,
      });
      onCreated();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : "Failed to create agent");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
      <div className="card w-full max-w-lg">
        <h2 className="mb-4 text-lg font-semibold">Create New Agent</h2>

        <div className="space-y-3">
          <div>
            <label className="label">Name</label>
            <input
              className="input"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="My BTC Bot"
            />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div>
              <label className="label">Symbol</label>
              <select
                className="input"
                value={symbol}
                onChange={(e) => setSymbol(e.target.value)}
              >
                {SYMBOLS.map((s) => (
                  <option key={s} value={s}>
                    {s}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="label">Strategy</label>
              <select
                className="input"
                value={strategyName}
                onChange={(e) => setStrategyName(e.target.value)}
              >
                {strategies.map((s) => (
                  <option key={s.name} value={s.name}>
                    {s.label}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {selectedStrategy && (
            <div>
              <label className="label">Strategy Parameters</label>
              <div className="grid grid-cols-2 gap-2">
                {Object.entries(params).map(([key, val]) => (
                  <div key={key}>
                    <label className="text-[10px] text-slate-500">{key}</label>
                    <input
                      className="input"
                      type="number"
                      value={val}
                      onChange={(e) =>
                        setParams({ ...params, [key]: parseFloat(e.target.value) || 0 })
                      }
                    />
                  </div>
                ))}
              </div>
            </div>
          )}

          <div className="grid grid-cols-3 gap-3">
            <div>
              <label className="label">Initial Capital ($)</label>
              <input
                className="input"
                type="number"
                value={capital}
                onChange={(e) => setCapital(e.target.value)}
              />
            </div>
            <div>
              <label className="label">Max Position ($)</label>
              <input
                className="input"
                type="number"
                value={maxPos}
                onChange={(e) => setMaxPos(e.target.value)}
              />
            </div>
            <div>
              <label className="label">Stop Loss (%)</label>
              <input
                className="input"
                type="number"
                step="0.01"
                value={stopLoss}
                onChange={(e) => setStopLoss(e.target.value)}
              />
            </div>
          </div>

          <p className="text-xs text-yellow-400">Paper trading only — no real money.</p>
        </div>

        {error && <p className="mt-2 text-sm text-red-400">{error}</p>}

        <div className="mt-4 flex justify-end gap-2">
          <button onClick={onClose} className="btn btn-ghost">
            Cancel
          </button>
          <button onClick={handleSubmit} disabled={submitting} className="btn btn-primary">
            {submitting ? "Creating..." : "Create Agent"}
          </button>
        </div>
      </div>
    </div>
  );
}
