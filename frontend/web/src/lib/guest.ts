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
