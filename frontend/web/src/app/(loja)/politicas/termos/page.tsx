import Link from "next/link";
import { connection } from "next/server";
import { CompanyData } from "@/components/store/company-data";
import { ContactChannel, PolicyPage, policyMetadata } from "@/components/store/policy-page";
import { MAX_INSTALLMENTS } from "@/lib/card";
import { company } from "@/lib/legal";

export const metadata = policyMetadata("Termos de uso", "As condições para comprar nesta loja.");

export default async function TermsPage() {
  await connection();
  return (
    <PolicyPage title="Termos de uso" updated="31 de outubro de 2026">
      <h2>Quem vende</h2>
      <CompanyData {...company} />
      {!company.name && (
        <p>
          <mark className="bg-danger/12 text-danger-ink rounded-sm px-1">
            [Nome empresarial, CNPJ e endereço a definir.]
          </mark>
        </p>
      )}
      <h2>Como a compra funciona</h2>
      <p>
        Antes de concluir o pedido, você vê os produtos, o preço, o frete, o prazo e o total, e pode corrigir
        o que precisar. Enviamos a confirmação por e-mail. Se um preço mudar durante a compra, o pedido não é
        feito e o carrinho mostra a diferença.
      </p>
      <p>
        Os produtos são enviados por fornecedores parceiros. Se o fornecedor não puder atender o pedido, ele é
        cancelado e o valor pago é estornado.
      </p>
      <h2>Preço e pagamento</h2>
      <p>
        Aceitamos PIX e cartão de crédito, em até {MAX_INSTALLMENTS}x sem juros, respeitada a parcela mínima
        mostrada no checkout. O preço é o mesmo nas duas formas de pagamento.
      </p>
      <h2>Compra sem conta</h2>
      <p>
        Você pode comprar só com o e-mail e volta ao pedido pelo link que enviamos para ele. Quem tiver esse
        link vê os dados da compra: não o encaminhe. Em computador compartilhado, clique em Sair ao terminar.
      </p>
      <h2>Entrega, trocas e dados pessoais</h2>
      <ul>
        <li>
          <Link href="/politicas/entrega" className="underline underline-offset-4">
            Entrega
          </Link>
        </li>
        <li>
          <Link href="/politicas/trocas-e-devolucoes" className="underline underline-offset-4">
            Trocas e devoluções, com a desistência em até 7 dias
          </Link>
        </li>
        <li>
          <Link href="/politicas/privacidade" className="underline underline-offset-4">
            Privacidade
          </Link>
        </li>
      </ul>
      <h2>Atendimento</h2>
      <p>
        Fale com a gente em <ContactChannel />. Respondemos em até 5 dias (Decreto 7.962/2013, art. 4º).
      </p>
    </PolicyPage>
  );
}
