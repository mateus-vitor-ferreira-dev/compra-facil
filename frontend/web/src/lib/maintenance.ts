import "server-only";
import { getCloudflareContext } from "@opennextjs/cloudflare";
import { cookies } from "next/headers";
import { cache } from "react";
import { z } from "zod";
import { env } from "@/lib/env";

/**
 * A manutenção da loja (#370), em dois momentos:
 *
 * - **deploy:** o `deploy.yml` liga a marca no Cloudflare KV antes de publicar a API e a desliga quando ela volta
 *   a responder. A API é justamente o que sai do ar, então a marca mora no KV, que o Worker lê sem ela. Com a
 *   marca, a loja troca o erro por "estamos atualizando a loja" e segura o pedido.
 * - **agendada:** o painel grava uma janela (início, fim, aviso). A loja mostra a faixa antes e durante ela.
 *
 * O KV é eventualmente consistente: a marca pode levar alguns segundos para valer em todo lugar, e a leitura fica
 * em cache por {@link KV_CACHE_SECONDS}. Fora do Worker (`next start`, os testes), o estado fica na memória do
 * processo.
 */
export const KV_CACHE_SECONDS = 30;
export const DEPLOY_KEY = "deploy";
export const SCHEDULE_KEY = "schedule";

const deploySchema = z.object({ since: z.iso.datetime({ offset: true }) });
const scheduleSchema = z.object({
  starts_at: z.iso.datetime({ offset: true }),
  ends_at: z.iso.datetime({ offset: true }),
  message: z.string().max(200).optional(),
});

export type Schedule = z.infer<typeof scheduleSchema>;
export type Maintenance = {
  /** a API está sendo publicada agora */
  deploying: boolean;
  /** a janela agendada, se ainda não acabou */
  schedule?: Schedule & { phase: "upcoming" | "now" };
};

// no globalThis: o Next separa as rotas de API e as páginas em pacotes, e cada um teria o próprio Map
const memory: Map<string, string> = ((globalThis as { __maintenance?: Map<string, string> }).__maintenance ??=
  new Map());

/**
 * Nos testes de navegador, o estado (na memória ou no KV simulado do wrangler dev) fica separado por um cookie de espaço: os testes rodam em
 * paralelo contra o mesmo servidor, e a manutenção de um não pode segurar o checkout dos outros.
 */
export const TEST_NAMESPACE_COOKIE = "cf_teste_manutencao";

async function scoped(key: string): Promise<string> {
  if (!env.MAINTENANCE_TEST_HOOKS) return key;
  return `${(await cookies()).get(TEST_NAMESPACE_COOKIE)?.value ?? ""}:${key}`;
}

// o modo síncrono só lê o contexto do Worker; o assíncrono, fora dele, subiria um workerd para simular os bindings
async function kv(): Promise<MaintenanceKV | undefined> {
  try {
    return getCloudflareContext().env.MAINTENANCE;
  } catch {
    return undefined; // fora do Worker (next start, os testes)
  }
}

async function readKey(name: string): Promise<unknown> {
  const [store, key] = await Promise.all([kv(), scoped(name)]);
  try {
    // nos testes, sem o cache da borda: a marca vale na hora
    if (store)
      return await store.get(
        key,
        env.MAINTENANCE_TEST_HOOKS ? { type: "json" } : { type: "json", cacheTtl: KV_CACHE_SECONDS },
      );
    const raw = memory.get(key);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null; // o aviso nunca derruba a página
  }
}

/** O estado da manutenção, uma vez por requisição. */
export const readMaintenance = cache(async (now = Date.now()): Promise<Maintenance> => {
  const [deploy, schedule] = await Promise.all([readKey(DEPLOY_KEY), readKey(SCHEDULE_KEY)]);
  const s = scheduleSchema.safeParse(schedule);
  const window =
    s.success && Date.parse(s.data.ends_at) > now
      ? { ...s.data, phase: Date.parse(s.data.starts_at) <= now ? ("now" as const) : ("upcoming" as const) }
      : undefined;
  return { deploying: deploySchema.safeParse(deploy).success, schedule: window };
});

async function write(name: string, value: unknown | undefined, ttlSeconds?: number): Promise<void> {
  const [store, key] = await Promise.all([kv(), scoped(name)]);
  if (value === undefined) {
    if (store) await store.delete(key);
    else memory.delete(key);
    return;
  }
  const raw = JSON.stringify(value);
  if (store) await store.put(key, raw, ttlSeconds ? { expirationTtl: ttlSeconds } : undefined);
  else memory.set(key, raw);
}

/** Grava a janela agendada (o painel); sem ela, apaga. Some sozinha depois do fim. */
export async function saveSchedule(schedule: Schedule | undefined): Promise<void> {
  const ttl = schedule
    ? Math.max(60, Math.ceil((Date.parse(schedule.ends_at) - Date.now()) / 1000))
    : undefined;
  await write(SCHEDULE_KEY, schedule && scheduleSchema.parse(schedule), ttl);
}

/** Liga ou desliga a marca do deploy. Só os testes chamam daqui: em produção, quem liga é o deploy.yml. */
export async function setDeploying(on: boolean): Promise<void> {
  await write(DEPLOY_KEY, on ? { since: new Date().toISOString() } : undefined, 600);
}
