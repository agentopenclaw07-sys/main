"use client";
import { useState, useEffect } from "react";
import { api } from "@/lib/api";
import type { RiskSettings } from "@/types";

interface Props {
  onClose: () => void;
}

export function RiskPanel({ onClose }: Props) {
  const [settings, setSettings] = useState<RiskSettings | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    api.getRiskSettings().then(setSettings).catch(console.error);
  }, []);

  const save = async (updates: Partial<RiskSettings>) => {
    setSaving(true);
    try {
      const updated = await api.updateRiskSettings(updates);
      setSettings(updated);
    } catch (err) {
      console.error(err);
    } finally {
      setSaving(false);
    }
  };

  if (!settings) {
    return (
      <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
        <div className="card w-full max-w-md">Loading...</div>
      </div>
    );
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60">
      <div className="card w-full max-w-md">
        <h2 className="mb-4 text-lg font-semibold">Risk Controls</h2>

        <div className="space-y-4">
          <div>
            <label className="label">Max Position Size ($)</label>
            <input
              className="input"
              type="number"
              value={settings.max_position_size}
              onChange={(e) =>
                setSettings({ ...settings, max_position_size: parseFloat(e.target.value) || 0 })
              }
              onBlur={() => save({ max_position_size: settings.max_position_size })}
            />
          </div>

          <div>
            <label className="label">Max Drawdown (%)</label>
            <input
              className="input"
              type="number"
              step="0.01"
              value={(settings.max_drawdown_pct * 100).toFixed(1)}
              onChange={(e) =>
                setSettings({
                  ...settings,
                  max_drawdown_pct: (parseFloat(e.target.value) || 0) / 100,
                })
              }
              onBlur={() => save({ max_drawdown_pct: settings.max_drawdown_pct })}
            />
          </div>

          <div className="flex items-center justify-between rounded-lg border border-red-900/50 bg-red-950/20 p-3">
            <div>
              <p className="font-medium text-red-400">Global Kill Switch</p>
              <p className="text-xs text-slate-400">
                Immediately stops all running agents
              </p>
            </div>
            <button
              onClick={() => {
                const next = !settings.global_kill_switch;
                setSettings({ ...settings, global_kill_switch: next });
                save({ global_kill_switch: next });
              }}
              className={`rounded-full px-4 py-1.5 text-sm font-medium ${
                settings.global_kill_switch
                  ? "bg-red-600 text-white"
                  : "bg-slate-700 text-slate-300"
              }`}
            >
              {settings.global_kill_switch ? "ACTIVE" : "OFF"}
            </button>
          </div>
        </div>

        <div className="mt-4 flex justify-end">
          <button onClick={onClose} className="btn btn-ghost">
            Close
          </button>
        </div>
      </div>
    </div>
  );
}
