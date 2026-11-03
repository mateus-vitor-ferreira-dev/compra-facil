"use client";

import { useRouter } from "next/navigation";
import { useEffect } from "react";

/**
 * Busca a página de novo de tempos em tempos, sem recarregar: o que o cliente digitou fica. Usado enquanto a loja
 * está sendo atualizada (#370), para ela voltar sozinha. Sem JavaScript, fica o link "Tentar agora".
 */
export function AutoRefresh({ seconds = 10 }: { seconds?: number }) {
  const router = useRouter();
  useEffect(() => {
    const timer = window.setInterval(() => router.refresh(), seconds * 1000);
    return () => window.clearInterval(timer);
  }, [router, seconds]);
  return null;
}
