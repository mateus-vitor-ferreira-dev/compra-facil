import { NextResponse } from "next/server";
import type { CepAddress } from "@/lib/delivery";
import { env } from "@/lib/env";

type ViaCep = {
  erro?: boolean | string;
  logradouro?: string;
  bairro?: string;
  localidade?: string;
  uf?: string;
};

/**
 * O endereço de um CEP, para o checkout preencher a rua, o bairro, a cidade e o estado (#221). Consulta
 * o ViaCEP pelo servidor, com cache de um dia: o CEP de uma rua não muda. 404: o CEP não existe; 503: o
 * serviço não respondeu, e o cliente digita o endereço.
 */
export async function GET(_: Request, { params }: { params: Promise<{ cep: string }> }) {
  const { cep } = await params;
  if (!/^\d{8}$/.test(cep)) return NextResponse.json({ error: "CEP com 8 dígitos" }, { status: 400 });
  try {
    const response = await fetch(`${env.CEP_LOOKUP_URL}/${cep}/json/`, {
      next: { revalidate: 86_400 },
      signal: AbortSignal.timeout(3_000),
    });
    if (response.status === 400) return NextResponse.json({ error: "CEP não encontrado" }, { status: 404 });
    if (!response.ok) return NextResponse.json({ error: "consulta indisponível" }, { status: 503 });
    const body = (await response.json()) as ViaCep;
    if (body.erro) return NextResponse.json({ error: "CEP não encontrado" }, { status: 404 });
    const data: CepAddress = {
      street: body.logradouro ?? "",
      district: body.bairro ?? "",
      city: body.localidade ?? "",
      state: body.uf ?? "",
    };
    return NextResponse.json(data, { headers: { "Cache-Control": "public, max-age=86400" } });
  } catch {
    return NextResponse.json({ error: "consulta indisponível" }, { status: 503 });
  }
}
