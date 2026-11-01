import type { Metadata } from "next";
import type { ReactNode } from "react";
import { Breadcrumbs } from "@/components/store/breadcrumbs";
import { company, policiesReviewed } from "@/lib/legal";

export const POLICIES = [
  { href: "/politicas/entrega", label: "Entrega" },
  { href: "/politicas/trocas-e-devolucoes", label: "Trocas e devoluções" },
  { href: "/politicas/privacidade", label: "Privacidade" },
  { href: "/politicas/termos", label: "Termos de uso" },
] as const;

/** O rascunho não entra nos buscadores; a revisão do advogado libera (POLICIES_REVIEWED). */
export function policyMetadata(title: string, description: string): Metadata {
  return { title, description, robots: { index: policiesReviewed } };
}

/** Como falar com a loja, dentro do texto; sem o canal configurado, o rascunho mostra o que falta. */
export function ContactChannel() {
  return company.email ? (
    <a href={`mailto:${company.email}`} className="text-ink underline underline-offset-4">
      {company.email}
    </a>
  ) : (
    <mark className="bg-danger/12 text-danger-ink rounded-sm px-1">[e-mail de contato a definir]</mark>
  );
}

/** A forma comum das políticas: título, aviso de rascunho, texto em largura de leitura. */
export function PolicyPage({
  title,
  updated,
  children,
}: {
  title: string;
  updated: string;
  children: ReactNode;
}) {
  return (
    <main id="conteudo" className="mx-auto max-w-6xl px-4 pt-1 sm:px-6 sm:pt-6">
      <Breadcrumbs items={[{ href: "/", label: "Início" }, { label: title }]} />
      <article className="mt-2 max-w-prose space-y-4 sm:mt-4 [&_h2]:mt-8 [&_h2]:text-xl [&_h2]:font-semibold [&_h2]:tracking-tight [&_li]:ml-5 [&_li]:list-disc [&_ul]:space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">{title}</h1>
        <p className="text-ink-soft text-sm">Atualizada em {updated}</p>
        {!policiesReviewed && (
          <p role="note" className="border-danger bg-surface rounded-md border-l-4 px-4 py-3">
            <strong>Rascunho em revisão.</strong> Este texto ainda não foi revisto por advogado e pode mudar.
          </p>
        )}
        {children}
      </article>
    </main>
  );
}
