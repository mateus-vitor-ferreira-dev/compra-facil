import { formatMoney } from "@/lib/format";
import type { ReceiptLine } from "@/lib/sales-overview";
import { cn } from "@/lib/utils";

const brl = (cents: number) => formatMoney({ amount_cents: Math.abs(cents), currency: "BRL" });

/**
 * O extrato do período (#382): a conta inteira, na ordem em que o dinheiro sai, como numa nota de caixa. Cada
 * linha tem o sinal escrito ("−"), não só a cor; o resultado fecha numa etiqueta amarela, como o preço de
 * campanha na vitrine. É o número de destaque da tela, e só ele.
 */
export function Receipt({ lines, left, partial }: { lines: ReceiptLine[]; left: number; partial: boolean }) {
  return (
    <section
      aria-labelledby="extrato"
      className="bg-surface border-line flex flex-col rounded-lg border p-5 sm:p-6"
    >
      <h2 id="extrato" className="text-lg font-semibold">
        Extrato do período
      </h2>
      <p className="text-ink-soft mt-1 text-sm">Do que os clientes pagaram até o que sobrou para a loja.</p>

      <table className="mt-5 w-full border-collapse text-left">
        <caption className="sr-only">Vendas, estornos e custos do período, e o que sobrou</caption>
        <tbody>
          {lines.map((l, i) => (
            <tr key={l.label} className={cn("align-top", i > 0 && "border-line border-t border-dashed")}>
              <th scope="row" className="py-2.5 pr-4 font-normal">
                <span className="block font-medium">{l.label}</span>
                {l.detail && <span className="text-ink-soft block text-sm">{l.detail}</span>}
              </th>
              <td className="py-2.5 text-right whitespace-nowrap tabular-nums">
                {l.cents < 0 ? "− " : ""}
                {brl(l.cents)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>

      <div className="border-ink mt-3 flex flex-wrap items-end justify-between gap-x-4 gap-y-3 border-t-2 pt-4">
        <p className="font-semibold">{left < 0 ? "Faltou" : "Sobrou"}</p>
        {/* a etiqueta: o recorte do canto e o furo, como a do preço de campanha */}
        <p
          className={cn(
            "font-condensed relative rounded-sm px-4 py-1.5 text-5xl font-extrabold [clip-path:polygon(0.9rem_0,100%_0,100%_100%,0.9rem_100%,0_50%)]",
            left < 0 ? "bg-danger text-on-danger" : "bg-tag text-tag-ink",
          )}
        >
          <span
            aria-hidden="true"
            className="bg-surface absolute top-1/2 left-3 size-2 -translate-y-1/2 rounded-full"
          />
          <span className="pl-3">
            {left < 0 ? "− " : ""}
            {brl(left)}
          </span>
        </p>
      </div>
      {partial && (
        <p className="text-ink-soft mt-4 text-sm">
          O custo, o frete e as taxas não carregaram agora. O extrato mostra só as vendas e os estornos.
        </p>
      )}
    </section>
  );
}
