import "server-only";
import { getCloudflareContext } from "@opennextjs/cloudflare";

/**
 * A última cópia boa das leituras do catálogo, para a vitrine continuar de pé quando a API cai: na troca dos
 * contêineres de um deploy ela fica fora por cerca de 40 s (#310, #373), e no Worker não há cache de dados do Next
 * (o OpenNext roda com o cache incremental padrão), então cada página busca na hora.
 *
 * - No Worker, a cópia fica no Cache API da Cloudflare (por data center, sem custo de escrita), por um dia, e a
 *   gravação sai depois da resposta (`waitUntil`), no máximo uma vez por minuto por chave em cada isolate.
 * - Fora dele (`next start`), num mapa limitado na memória do processo.
 *
 * Só leituras do catálogo: o preço de uma cópia antiga nunca é cobrado, porque o pedido confere o preço no
 * catalog na hora (ADR 0004) e o carrinho avisa o que mudou. Estoque e pedidos ficam de fora.
 */
const ORIGIN = "https://vitrine-reserva.compra-facil.invalid/";
const KEEP_SECONDS = 24 * 60 * 60;
const REFRESH_MS = 60_000;
const MEMORY_LIMIT = 300;

type Entry = { at: number; body: string };
const shared = globalThis as {
  __catalogReserve?: Map<string, Entry>;
  __catalogReserveWrites?: Map<string, number>;
};
const memory = (shared.__catalogReserve ??= new Map());
const lastWrite = (shared.__catalogReserveWrites ??= new Map());

type EdgeCache = {
  match(request: Request): Promise<Response | undefined>;
  put(request: Request, response: Response): Promise<void>;
};

function edge(): { cache: EdgeCache; waitUntil: (p: Promise<unknown>) => void } | undefined {
  try {
    const { ctx } = getCloudflareContext();
    const cache = (globalThis as unknown as { caches?: { default?: EdgeCache } }).caches?.default;
    return cache ? { cache, waitUntil: (p) => ctx.waitUntil(p) } : undefined;
  } catch {
    return undefined; // fora do Worker
  }
}

const requestFor = (key: string) => new Request(ORIGIN + encodeURIComponent(key));

/** Guarda a cópia boa; não espera a gravação, e não regrava a mesma chave antes de um minuto. */
export function remember(key: string, data: unknown): void {
  const now = Date.now();
  if (now - (lastWrite.get(key) ?? 0) < REFRESH_MS) return;
  lastWrite.set(key, now);
  const body = JSON.stringify(data);
  const worker = edge();
  if (worker) {
    const response = new Response(body, {
      headers: { "Content-Type": "application/json", "Cache-Control": `public, max-age=${KEEP_SECONDS}` },
    });
    worker.waitUntil(worker.cache.put(requestFor(key), response).catch(() => undefined));
    return;
  }
  memory.delete(key);
  memory.set(key, { at: now, body });
  if (memory.size > MEMORY_LIMIT) memory.delete(memory.keys().next().value!);
}

/** A última cópia boa, se houver e tiver menos de um dia. */
export async function recall<T>(key: string): Promise<T | undefined> {
  try {
    const worker = edge();
    if (worker) {
      const found = await worker.cache.match(requestFor(key));
      return found ? ((await found.json()) as T) : undefined;
    }
    const entry = memory.get(key);
    return entry && Date.now() - entry.at < KEEP_SECONDS * 1000 ? (JSON.parse(entry.body) as T) : undefined;
  } catch {
    return undefined;
  }
}

/** Para os testes de unidade: esquece tudo. */
export function forgetAll(): void {
  memory.clear();
  lastWrite.clear();
}
