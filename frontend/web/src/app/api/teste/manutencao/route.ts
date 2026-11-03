import { NextResponse } from "next/server";
import { env } from "@/lib/env";
import { setDeploying } from "@/lib/maintenance";

/**
 * Só nos testes de navegador (MAINTENANCE_TEST_HOOKS): liga e desliga a marca do deploy (#370), que em produção
 * o deploy.yml liga pela API do Cloudflare. Sem a variável, não existe.
 */
export async function POST(request: Request) {
  if (!env.MAINTENANCE_TEST_HOOKS) return new NextResponse(null, { status: 404 });
  const { deploying } = (await request.json().catch(() => ({}))) as { deploying?: boolean };
  await setDeploying(deploying === true);
  return new NextResponse(null, { status: 204 });
}
