import { connection } from "next/server";
import { ContactChannel, PolicyPage, policyMetadata } from "@/components/store/policy-page";

export const metadata = policyMetadata(
  "Entrega",
  "Como funcionam o frete, o prazo e o rastreio dos pedidos.",
);

export default async function DeliveryPolicyPage() {
  await connection();
  return (
    <PolicyPage title="Entrega" updated="31 de outubro de 2026">
      <h2>Quem envia</h2>
      <p>
        Os produtos são enviados por fornecedores parceiros, direto para o endereço informado no pedido. Quem
        responde pela sua compra é esta loja, mesmo quando o envio é feito pelo fornecedor.
      </p>
      <h2>Frete e prazo</h2>
      <p>
        O valor e o prazo aparecem pelo CEP, na página do produto, no carrinho e no checkout, antes de você
        concluir o pedido. O prazo prometido fica registrado na página do pedido.
      </p>
      <h2>Rastreio</h2>
      <p>
        Quando o pedido sai, enviamos por e-mail o código de rastreio e a transportadora. Você também
        acompanha tudo na página do pedido, com a sua conta ou pelo link enviado por e-mail, se comprou sem
        conta.
      </p>
      <h2>Se houver atraso</h2>
      <p>
        Se o prazo prometido não for cumprido, fale com a gente em <ContactChannel />. Você pode exigir o
        cumprimento da entrega, aceitar outro produto equivalente ou cancelar a compra e receber de volta o
        que pagou, com correção (Código de Defesa do Consumidor, art. 35).
      </p>
      <h2>Endereço e tentativa de entrega</h2>
      <p>
        Confira o endereço antes de concluir o pedido. Depois de enviado, a alteração depende da
        transportadora.
      </p>
    </PolicyPage>
  );
}
