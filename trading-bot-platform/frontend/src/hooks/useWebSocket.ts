"use client";
import { useEffect, useRef, useCallback, useState } from "react";
import type { WsMessage } from "@/types";

const WS_BASE = process.env.NEXT_PUBLIC_WS_URL || "ws://localhost:8000";

export function useAgentWebSocket(agentId: number | null, onMessage: (msg: WsMessage) => void) {
  const wsRef = useRef<WebSocket | null>(null);

  useEffect(() => {
    if (agentId === null) return;
    const ws = new WebSocket(`${WS_BASE}/ws/agent/${agentId}`);
    wsRef.current = ws;

    ws.onmessage = (event) => {
      try {
        const data = JSON.parse(event.data);
        onMessage(data);
      } catch {}
    };

    ws.onclose = () => {
      // Reconnect after 3 seconds
      setTimeout(() => {
        if (wsRef.current === ws) {
          wsRef.current = null;
        }
      }, 3000);
    };

    return () => {
      ws.close();
      wsRef.current = null;
    };
  }, [agentId, onMessage]);
}

export function useDashboardWebSocket(onMessage: (msg: WsMessage) => void) {
  const wsRef = useRef<WebSocket | null>(null);

  useEffect(() => {
    const ws = new WebSocket(`${WS_BASE}/ws/dashboard`);
    wsRef.current = ws;

    ws.onmessage = (event) => {
      try {
        const data = JSON.parse(event.data);
        onMessage(data);
      } catch {}
    };

    ws.onclose = () => {
      setTimeout(() => {
        if (wsRef.current === ws) {
          wsRef.current = null;
        }
      }, 3000);
    };

    return () => {
      ws.close();
      wsRef.current = null;
    };
  }, [onMessage]);
}
