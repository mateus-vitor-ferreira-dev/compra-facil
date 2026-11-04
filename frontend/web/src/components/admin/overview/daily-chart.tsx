"use client";

import { useState, type KeyboardEvent } from "react";
import { formatMoney } from "@/lib/format";
import { niceTicks, type Bucket } from "@/lib/sales-overview";
import { cn } from "@/lib/utils";

const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });
// o eixo em reais redondos e curtos: R$ 2,5 mil, R$ 30 mi
const compact = new Intl.NumberFormat("pt-BR", {
  style: "currency",
  currency: "BRL",
  notation: "compact",
  maximumFractionDigits: 1,
});
const axis = (cents: number) => compact.format(cents / 100);
const orders = (n: number) => `${n} ${n === 1 ? "pedido pago" : "pedidos pagos"}`;

/**
 * Vendas brutas por dia (ou por semana, num período longo): uma série, em colunas, sem legenda (o título diz o
 * que é). A coluna inteira, do chão ao teto, é a área do mouse e do toque; pelo teclado, Tab entra no gráfico
 * e as setas passam de dia em dia. O estorno do dia vai no balão e na tabela, que tem todos os números.
 */
export function DailyChart({ items, unit }: { items: Bucket[]; unit: "dia" | "semana" }) {
  const [active, setActive] = useState<number | null>(null);
  const [focus, setFocus] = useState(items.length - 1); // o dia mais recente
  const ticks = niceTicks(Math.max(...items.map((b) => b.gross), 0));
  const top = ticks[ticks.length - 1] || 1;
  const shown = active ?? null;
  const labelIdx = new Set([0, Math.floor((items.length - 1) / 2), items.length - 1]);

  const move = (e: KeyboardEvent<HTMLDivElement>) => {
    const next =
      e.key === "ArrowRight"
        ? focus + 1
        : e.key === "ArrowLeft"
          ? focus - 1
          : e.key === "Home"
            ? 0
            : e.key === "End"
              ? items.length - 1
              : null;
    if (next === null) return;
    e.preventDefault();
    const i = Math.max(0, Math.min(items.length - 1, next));
    setFocus(i);
    setActive(i);
    (e.currentTarget.querySelector(`[data-i="${i}"]`) as HTMLElement | null)?.focus();
  };

  return (
    <div className="space-y-3">
      <div className="flex gap-3 pt-3">
        {/* o eixo: valores redondos, em texto da interface, nunca na cor da série */}
        <div aria-hidden="true" className="text-ink-soft relative h-52 w-16 shrink-0 text-xs tabular-nums">
          {ticks.map((t) => (
            <span
              key={t}
              className="absolute right-0 -translate-y-1/2 whitespace-nowrap"
              style={{ bottom: `${(t / top) * 100}%` }}
            >
              {axis(t)}
            </span>
          ))}
        </div>
        <div className="relative h-52 min-w-0 flex-1">
          {ticks.map((t) => (
            <div
              key={t}
              aria-hidden="true"
              className={cn("absolute inset-x-0 h-px", t === 0 ? "bg-ink/40" : "bg-line")}
              style={{ bottom: `${(t / top) * 100}%` }}
            />
          ))}
          <div
            role="group"
            aria-label={`Vendas brutas por ${unit}. Use as setas para passar de ${unit} em ${unit}.`}
            onKeyDown={move}
            onPointerLeave={() => setActive(null)}
            className={cn("absolute inset-0 flex items-end", items.length <= 60 ? "gap-0.5" : "gap-px")}
          >
            {items.map((b, i) => (
              <button
                key={b.from}
                type="button"
                data-i={i}
                tabIndex={i === focus ? 0 : -1}
                aria-label={`${b.label}: ${brl(b.gross)} em ${orders(b.orders)}${b.refunds ? `, ${brl(b.refunds)} estornados` : ""}`}
                onPointerEnter={() => setActive(i)}
                onFocus={() => {
                  setFocus(i);
                  setActive(i);
                }}
                onBlur={() => setActive(null)}
                className="group relative flex h-full min-w-0 flex-1 cursor-default items-end justify-center focus-visible:outline-offset-0"
              >
                <span
                  className={cn(
                    // até 24 px de largura, cantos de 4 px só na ponta, quadrado no chão
                    "bg-chart-1 w-full max-w-6 rounded-t-[4px] transition-opacity",
                    shown !== null && shown !== i && "opacity-55",
                  )}
                  style={{ height: b.gross > 0 ? `max(2px, ${(b.gross / top) * 100}%)` : 0 }}
                />
              </button>
            ))}
          </div>
          {shown !== null && items[shown] && (
            <Tooltip bucket={items[shown]} at={(shown + 0.5) / items.length} />
          )}
        </div>
      </div>
      <div aria-hidden="true" className="text-ink-soft relative ml-[4.75rem] h-4 text-xs">
        {items.map((b, i) =>
          labelIdx.has(i) ? (
            <span
              key={b.from}
              className={cn(
                "absolute whitespace-nowrap",
                i === 0 ? "left-0" : i === items.length - 1 ? "right-0" : "-translate-x-1/2",
              )}
              style={
                i === 0 || i === items.length - 1
                  ? undefined
                  : { left: `${((i + 0.5) / items.length) * 100}%` }
              }
            >
              {b.label}
            </span>
          ) : null,
        )}
      </div>

      <details className="text-sm">
        <summary className="text-ink inline-flex min-h-11 cursor-pointer items-center font-medium underline-offset-4 hover:underline pointer-fine:min-h-8">
          Ver os números por {unit}
        </summary>
        <div className="mt-2 max-h-72 overflow-y-auto">
          <table className="w-full text-left">
            <thead className="text-ink-soft bg-surface sticky top-0">
              <tr>
                <th scope="col" className="py-1.5 pr-3 font-medium">
                  {unit === "dia" ? "Dia" : "Semana"}
                </th>
                <th scope="col" className="py-1.5 pr-3 text-right font-medium">
                  Pedidos pagos
                </th>
                <th scope="col" className="py-1.5 pr-3 text-right font-medium">
                  Vendas brutas
                </th>
                <th scope="col" className="py-1.5 text-right font-medium">
                  Estornos
                </th>
              </tr>
            </thead>
            <tbody className="tabular-nums">
              {[...items].reverse().map((b) => (
                <tr key={b.from} className="border-line border-t">
                  <th scope="row" className="py-1.5 pr-3 font-normal">
                    {b.label}
                  </th>
                  <td className="py-1.5 pr-3 text-right">{b.orders}</td>
                  <td className="py-1.5 pr-3 text-right">{brl(b.gross)}</td>
                  <td className="py-1.5 text-right">{b.refunds ? brl(b.refunds) : "—"}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </details>
    </div>
  );
}

/** O balão: o valor primeiro e forte, o rótulo depois; preso às bordas do gráfico. */
function Tooltip({ bucket: b, at }: { bucket: Bucket; at: number }) {
  return (
    <div
      aria-hidden="true"
      className="bg-ink text-paper pointer-events-none absolute top-0 z-10 w-max max-w-56 rounded-md px-3 py-2 text-sm shadow-lg"
      style={{
        left: `${at * 100}%`,
        transform: `translateX(${at < 0.2 ? "0" : at > 0.8 ? "-100%" : "-50%"})`,
      }}
    >
      <p className="font-condensed text-lg leading-tight font-bold tabular-nums">{brl(b.gross)}</p>
      <p className="opacity-80">{b.label}</p>
      <p className="opacity-80">{orders(b.orders)}</p>
      {b.refunds > 0 && <p className="opacity-80">{brl(b.refunds)} estornados</p>}
    </div>
  );
}
