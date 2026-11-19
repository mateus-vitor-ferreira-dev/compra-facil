/**
 * O que as server actions usam do Next, para testar sem o servidor: um pote de cookies em memória e um
 * `redirect` que interrompe a ação (como o de verdade) dizendo para onde ia.
 */
export type SetCookie = { name: string; value: string; options?: Record<string, unknown> };

export class Redirected extends Error {
  constructor(readonly to: string) {
    super(`redirect ${to}`);
  }
}

export function cookieJar(initial: Record<string, string> = {}) {
  const values = new Map(Object.entries(initial));
  const set: SetCookie[] = [];
  const deleted: unknown[] = [];
  return {
    set,
    deleted,
    values,
    store: {
      get: (name: string) => (values.has(name) ? { name, value: values.get(name)! } : undefined),
      has: (name: string) => values.has(name),
      set: (name: string, value: string, options?: Record<string, unknown>) => {
        values.set(name, value);
        set.push({ name, value, options });
      },
      delete: (cookie: string | { name: string }) => {
        values.delete(typeof cookie === "string" ? cookie : cookie.name);
        deleted.push(cookie);
      },
    },
  };
}

/** Roda a ação e devolve para onde ela redirecionou (ou falha, se não redirecionou). */
export async function redirectOf(action: () => Promise<unknown>): Promise<string> {
  try {
    await action();
  } catch (e) {
    if (e instanceof Redirected) return e.to;
    throw e;
  }
  throw new Error("a ação não redirecionou");
}
