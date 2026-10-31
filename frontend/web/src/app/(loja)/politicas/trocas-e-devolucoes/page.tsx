import { connection } from "next/server";
import { ContactChannel, PolicyPage, policyMetadata } from "@/components/store/policy-page";

export const metadata = policyMetadata(
  "Trocas e devoluções",
  "Como desistir da compra em até 7 dias e o que fazer com um produto com defeito.",
);

export default async function ReturnsPolicyPage() {
  await connection();
  return (
    <PolicyPage title="Trocas e devoluções" updated="31 de outubro de 2026">
      <h2>Desistir em até 7 dias</h2>
      <p>
        Em compras pela internet, você pode desistir em até 7 dias depois de receber o produto, sem explicar o
        motivo (Código de Defesa do Consumidor, art. 49).
      </p>
      <ul>
        <li>
          <strong>Como pedir:</strong> abra o pedido e use &quot;Desistir da compra&quot;. O botão fica
          disponível na página do pedido até o fim do prazo, com a sua conta ou pelo link enviado por e-mail.
        </li>
        <li>
          <strong>O que você recebe de volta:</strong> o valor total pago, inclusive o frete da entrega.
        </li>
        <li>
          <strong>Como é o estorno:</strong> pelo mesmo meio do pagamento. O prazo para aparecer na fatura do
          cartão depende do banco emissor.
        </li>
        <li>
          <strong>Como devolver o produto:</strong> depois do pedido, enviamos por e-mail o código e o
          endereço para a devolução.
        </li>
      </ul>
      <p>
        <mark className="bg-danger/12 text-danger-ink rounded-sm px-1">
          [A definir com o advogado: como a loja arca com o frete da devolução.]
        </mark>
      </p>
      <h2>Produto com defeito</h2>
      <p>
        Avise em até 30 dias para produtos não duráveis e 90 dias para duráveis (art. 26), em{" "}
        <ContactChannel />, com o número do pedido e fotos ou vídeo do problema. Se o defeito não for
        resolvido em até 30 dias, você pode escolher a troca do produto, o dinheiro de volta ou o abatimento
        do preço (art. 18).
      </p>
      <h2>Produto errado ou danificado na entrega</h2>
      <p>
        Fale com a gente em <ContactChannel />, com o número do pedido e uma foto do pacote e do produto.
      </p>
    </PolicyPage>
  );
}
