/** O e-mail de quem compra sem conta: a mesma conferência no navegador e no servidor. */
export const GUEST_EMAIL_MAX = 254;

export function checkGuestEmail(email: string): string | undefined {
  const value = email.trim();
  if (!value) return "Digite o seu e-mail: é para ele que vão o recibo e o link do pedido.";
  if (value.length > GUEST_EMAIL_MAX || !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value)) {
    return "Confira o e-mail: ele precisa ter o formato nome@exemplo.com.";
  }
  return undefined;
}

/**
 * O e-mail mascarado, para mostrar de quem é uma compra sem expô-lo inteiro: as duas primeiras letras e o
 * domínio (`co***@exemplo.com`). O tamanho do resto não aparece.
 */
export function maskEmail(email: string): string {
  const at = email.lastIndexOf("@");
  if (at <= 0) return "***";
  const local = email.slice(0, at);
  return `${local.slice(0, local.length > 2 ? 2 : 1)}***${email.slice(at)}`;
}

/** O formato do {@link maskEmail}: a página só mostra o e-mail da URL se ele já vier mascarado. */
export const MASKED_EMAIL = /^[^\s@*]{1,2}\*\*\*@[^\s@]+$/;
