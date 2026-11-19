// uma origem que nunca existe: o caminho é resolvido contra ela, e só serve se continuar nela
const SELF = "https://loja.invalid";

/**
 * Para onde voltar depois do login (?voltar=). Só caminhos da própria loja. Conferir só o começo do texto não basta:
 * o navegador e o `new URL` descartam tab e quebra de linha em qualquer posição e tratam "\" como "/", então
 * "/\t/outro.site" vira "//outro.site" (open redirect). Por isso o caminho é resolvido de verdade, e só vale o que
 * continua na própria origem, sem caractere de controle nem barra invertida.
 */
export function safeReturnTo(value: unknown, fallback = "/conta"): string {
  if (typeof value !== "string" || value.length > 512) return fallback;
  if (!value.startsWith("/") || /[\u0000-\u001f\u007f\\]/.test(value)) return fallback;
  let url: URL;
  try {
    url = new URL(value, SELF);
  } catch {
    return fallback;
  }
  if (url.origin !== SELF) return fallback;
  const path = url.pathname + url.search + url.hash;
  // as próprias telas de entrada não são destino: voltariam para cá
  if (/^\/(entrar|cadastro)(\/|\?|#|$)/.test(path)) return fallback;
  return path;
}

/** O link de login que traz o cliente de volta para `path`. */
export const loginHref = (path: string) => `/entrar?voltar=${encodeURIComponent(path)}`;
