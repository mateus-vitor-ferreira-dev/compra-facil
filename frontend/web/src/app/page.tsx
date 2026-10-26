import { connection } from "next/server";
import { api } from "@/lib/api/client";
import { PriceTag } from "@/components/commerce/price-tag";

/**
 * Página provisória da fundação (#148): prova o caminho front → BFF → gateway → catalog com o cliente
 * tipado. A vitrine de verdade, com o design system, vem na #150.
 */
export default async function Home() {
  await connection(); // renderiza a cada requisição: o catálogo não está disponível no build
  const { data, error } = await api.catalog().GET("/v1/products", { params: { query: { limit: 8 } } });

  return (
    <main id="conteudo" className="mx-auto max-w-3xl px-6 py-12">
      <h1 className="text-3xl font-semibold tracking-tight">CompraFácil</h1>
      <p className="text-muted-foreground mt-2">
        Fundação do front: produtos vindos do catálogo pelo gateway.
      </p>
      {error || !data ? (
        <p role="alert" className="mt-8">
          Não foi possível carregar os produtos agora.
        </p>
      ) : (
        <ul className="mt-8 divide-y" aria-label="Produtos">
          {data.data.map((p) => (
            <li key={p.id} className="flex justify-between gap-4 py-3">
              <span>{p.name}</span>
              <PriceTag price={p.price} effective={p.effective_price} />
            </li>
          ))}
        </ul>
      )}
    </main>
  );
}
