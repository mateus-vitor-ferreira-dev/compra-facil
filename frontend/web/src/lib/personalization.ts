/**
 * O texto que o cliente escreve na compra (ex.: o nome na estampa), com as mesmas regras do catalog
 * (backend/services/catalog/internal/domain/personalization.go): normalizado (sem espaços nas pontas nem repetidos),
 * até o máximo do produto, só letras do alfabeto latino (com acento), algarismos, espaço e . , ' - & ! ? /.
 * A loja confere antes de pôr no carrinho; o pedido confere de novo no catalog.
 */
export const MAX_PERSONALIZATION = 40;

export type PersonalizationRule = { label: string; max_length: number; required: boolean; template: string };

export const normalizePersonalization = (text: string) => text.trim().split(/\s+/u).filter(Boolean).join(" ");

const PRINTABLE = /^[\p{Script=Latin}0-9 .,'\-&!?/]*$/u;

/** O motivo da recusa, para mostrar ao cliente; undefined se o texto (já normalizado) vale. */
export function personalizationError(
  text: string,
  rule: PersonalizationRule | null | undefined,
): string | undefined {
  if (!rule) return text ? "Este produto não tem personalização." : undefined;
  if (!text) return rule.required ? `Escreva o ${rule.label.toLowerCase()}.` : undefined;
  if ([...text].length > rule.max_length) return `${rule.label}: no máximo ${rule.max_length} caracteres.`;
  if (!PRINTABLE.test(text)) {
    return `${rule.label}: use letras, números, espaço e . , ' - & ! ? / (sem emoji).`;
  }
  return undefined;
}
