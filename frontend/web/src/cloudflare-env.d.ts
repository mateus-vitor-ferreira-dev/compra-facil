// Os bindings do Worker que a loja usa além dos do OpenNext (wrangler.jsonc). O KV é descrito só no que a loja
// chama, para não depender dos tipos inteiros do Workers.
interface MaintenanceKV {
  get(key: string, options: { type: "json"; cacheTtl?: number }): Promise<unknown>;
  put(key: string, value: string, options?: { expirationTtl?: number }): Promise<void>;
  delete(key: string): Promise<void>;
}

interface CloudflareEnv {
  /** A manutenção (#370): o deploy em andamento e a janela agendada pelo painel. */
  MAINTENANCE?: MaintenanceKV;
}
