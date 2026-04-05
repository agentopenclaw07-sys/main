"use client";
import type { AgentLog } from "@/types";

interface Props {
  logs: AgentLog[];
}

const levelColors: Record<string, string> = {
  debug: "text-slate-500",
  info: "text-blue-400",
  warning: "text-yellow-400",
  error: "text-red-400",
};

export function LogConsole({ logs }: Props) {
  if (logs.length === 0) {
    return <div className="py-12 text-center text-slate-500">No logs yet</div>;
  }

  return (
    <div className="card max-h-96 overflow-auto font-mono text-xs">
      {logs.map((log) => (
        <div key={log.id} className="flex gap-3 border-b border-slate-800 py-1">
          <span className="w-36 shrink-0 text-slate-600">
            {new Date(log.timestamp).toLocaleTimeString()}
          </span>
          <span className={`w-16 shrink-0 uppercase ${levelColors[log.level] || ""}`}>
            {log.level}
          </span>
          <span className="text-slate-300">{log.message}</span>
        </div>
      ))}
    </div>
  );
}
