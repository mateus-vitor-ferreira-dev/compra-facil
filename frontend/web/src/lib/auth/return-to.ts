/**
 * Para onde voltar depois do login (?voltar=). Só caminhos da própria loja: "//outro.site" e
 * "/\outro.site" viram endereço de outro site no navegador (open redirect), então caem no padrão.
 */
export function safeReturnTo(value: unknown, fallback = "/conta"): string {
  if (typeof value !== "string" || value.length > 512) return fallback;
  if (!value.startsWith("/") || value.startsWith("//") || value.startsWith("/\\")) return fallback;
  // as próprias telas de entrada não são destino: voltariam para cá
  if (/^\/(entrar|cadastro)(\/|\?|$)/.test(value)) return fallback;
  return value;
}

/** O link de login que traz o cliente de volta para `path`. */
export const loginHref = (path: string) => `/entrar?voltar=${encodeURIComponent(path)}`;
