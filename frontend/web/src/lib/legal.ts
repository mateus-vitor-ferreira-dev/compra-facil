import "server-only";
import { z } from "zod";

/**
 * Os dados da empresa e o estado das políticas (#226). Nada tem valor padrão: enquanto não forem
 * configurados, a loja não mostra dados inventados, e as políticas aparecem como rascunho.
 */
const schema = z.object({
  LEGAL_NAME: z.string().min(1).optional(),
  LEGAL_CNPJ: z.string().min(1).optional(),
  LEGAL_ADDRESS: z.string().min(1).optional(),
  LEGAL_EMAIL: z.email().optional(),
  LEGAL_PHONE: z.string().min(1).optional(),
  // só depois da revisão do advogado: tira o aviso de rascunho e libera a indexação
  POLICIES_REVIEWED: z.stringbool().default(false),
});

const parsed = schema.parse({
  LEGAL_NAME: process.env.LEGAL_NAME || undefined,
  LEGAL_CNPJ: process.env.LEGAL_CNPJ || undefined,
  LEGAL_ADDRESS: process.env.LEGAL_ADDRESS || undefined,
  LEGAL_EMAIL: process.env.LEGAL_EMAIL || undefined,
  LEGAL_PHONE: process.env.LEGAL_PHONE || undefined,
  POLICIES_REVIEWED: process.env.POLICIES_REVIEWED || undefined,
});

export const company = {
  name: parsed.LEGAL_NAME,
  cnpj: parsed.LEGAL_CNPJ,
  address: parsed.LEGAL_ADDRESS,
  email: parsed.LEGAL_EMAIL,
  phone: parsed.LEGAL_PHONE,
};

export type Company = typeof company;

export const policiesReviewed = parsed.POLICIES_REVIEWED;
