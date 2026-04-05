"use client";
import { useEffect, useRef, useState } from "react";
import { api } from "@/lib/api";
import type { PriceCandle, Trade } from "@/types";

interface Props {
  symbol: string;
  trades: Trade[];
}

export function PriceChart({ symbol, trades }: Props) {
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const [candles, setCandles] = useState<PriceCandle[]>([]);

  useEffect(() => {
    api.getCandles(symbol, 100).then(setCandles).catch(console.error);
  }, [symbol]);

  useEffect(() => {
    if (!canvasRef.current || candles.length === 0) return;
    const canvas = canvasRef.current;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;

    const dpr = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    canvas.width = rect.width * dpr;
    canvas.height = rect.height * dpr;
    ctx.scale(dpr, dpr);

    const W = rect.width;
    const H = rect.height;
    const padding = { top: 20, right: 60, bottom: 30, left: 10 };
    const chartW = W - padding.left - padding.right;
    const chartH = H - padding.top - padding.bottom;

    // Clear
    ctx.fillStyle = "#1e293b";
    ctx.fillRect(0, 0, W, H);

    const closes = candles.map((c) => c.close);
    const minP = Math.min(...closes) * 0.999;
    const maxP = Math.max(...closes) * 1.001;
    const rangeP = maxP - minP;

    const toX = (i: number) => padding.left + (i / (candles.length - 1)) * chartW;
    const toY = (p: number) => padding.top + (1 - (p - minP) / rangeP) * chartH;

    // Grid lines
    ctx.strokeStyle = "#334155";
    ctx.lineWidth = 0.5;
    for (let i = 0; i <= 4; i++) {
      const y = padding.top + (i / 4) * chartH;
      ctx.beginPath();
      ctx.moveTo(padding.left, y);
      ctx.lineTo(W - padding.right, y);
      ctx.stroke();

      const price = maxP - (i / 4) * rangeP;
      ctx.fillStyle = "#64748b";
      ctx.font = "11px monospace";
      ctx.textAlign = "left";
      ctx.fillText(`$${price.toFixed(0)}`, W - padding.right + 5, y + 4);
    }

    // Price line
    ctx.strokeStyle = "#6366f1";
    ctx.lineWidth = 2;
    ctx.beginPath();
    candles.forEach((c, i) => {
      const x = toX(i);
      const y = toY(c.close);
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    });
    ctx.stroke();

    // Fill under line
    const gradient = ctx.createLinearGradient(0, padding.top, 0, H - padding.bottom);
    gradient.addColorStop(0, "rgba(99, 102, 241, 0.3)");
    gradient.addColorStop(1, "rgba(99, 102, 241, 0)");
    ctx.fillStyle = gradient;
    ctx.beginPath();
    candles.forEach((c, i) => {
      const x = toX(i);
      const y = toY(c.close);
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    });
    ctx.lineTo(toX(candles.length - 1), H - padding.bottom);
    ctx.lineTo(toX(0), H - padding.bottom);
    ctx.closePath();
    ctx.fill();

    // Trade markers
    const candleTimes = candles.map((c) => new Date(c.timestamp).getTime());
    trades.forEach((trade) => {
      const tradeTime = new Date(trade.timestamp).getTime();
      // Find closest candle
      let closestIdx = 0;
      let closestDist = Infinity;
      candleTimes.forEach((t, i) => {
        const dist = Math.abs(t - tradeTime);
        if (dist < closestDist) {
          closestDist = dist;
          closestIdx = i;
        }
      });

      if (closestIdx >= 0 && closestIdx < candles.length) {
        const x = toX(closestIdx);
        const y = toY(trade.price);
        const isBuy = trade.side === "buy";

        ctx.beginPath();
        if (isBuy) {
          // Up triangle
          ctx.moveTo(x, y - 8);
          ctx.lineTo(x - 6, y + 4);
          ctx.lineTo(x + 6, y + 4);
        } else {
          // Down triangle
          ctx.moveTo(x, y + 8);
          ctx.lineTo(x - 6, y - 4);
          ctx.lineTo(x + 6, y - 4);
        }
        ctx.closePath();
        ctx.fillStyle = isBuy ? "#22c55e" : "#ef4444";
        ctx.fill();
      }
    });

    // Symbol label
    ctx.fillStyle = "#94a3b8";
    ctx.font = "bold 12px sans-serif";
    ctx.textAlign = "left";
    ctx.fillText(symbol, padding.left + 5, padding.top + 15);
  }, [candles, trades, symbol]);

  return (
    <div className="card">
      <canvas
        ref={canvasRef}
        className="h-80 w-full"
        style={{ display: "block" }}
      />
      <div className="mt-2 flex gap-4 text-xs text-slate-500">
        <span className="flex items-center gap-1">
          <span className="inline-block h-2 w-2 bg-green-500" /> Buy
        </span>
        <span className="flex items-center gap-1">
          <span className="inline-block h-2 w-2 bg-red-500" /> Sell
        </span>
      </div>
    </div>
  );
}
