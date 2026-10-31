import { connection } from "next/server";
import { ContactChannel, PolicyPage, policyMetadata } from "@/components/store/policy-page";
import { company } from "@/lib/legal";

export const metadata = policyMetadata(
  "Privacidade",
  "Quais dados a loja coleta, para que usa, com quem compartilha e quais são os seus direitos.",
);

export default async function PrivacyPolicyPage() {
  await connection();
  return (
    <PolicyPage title="Privacidade" updated="31 de outubro de 2026">
      <p>Este texto explica como tratamos os seus dados, conforme a Lei Geral de Proteção de Dados (LGPD).</p>
      <h2>Quem cuida dos dados</h2>
      <p>
        {company.name ?? (
          <mark className="bg-danger/12 text-danger-ink rounded-sm px-1">[nome empresarial a definir]</mark>
        )}
        {company.cnpj ? `, CNPJ ${company.cnpj}` : ""}. Canal para assuntos de dados pessoais:{" "}
        <ContactChannel />.
      </p>
      <h2>O que coletamos e por quê</h2>
      <ul>
        <li>
          <strong>Conta ou compra sem conta:</strong> nome e e-mail, para identificar você e enviar recibo,
          avisos do pedido e o link de acesso. Com conta, guardamos a senha só em forma protegida (hash).
        </li>
        <li>
          <strong>Entrega e nota fiscal:</strong> nome de quem recebe, CPF, celular e endereço, para emitir a
          nota, enviar o pedido e permitir o contato da transportadora.
        </li>
        <li>
          <strong>Pagamento:</strong> a forma de pagamento e o resultado. Os dados do cartão vão direto para o
          provedor de pagamento; a loja recebe só um código no lugar deles.
        </li>
        <li>
          <strong>Segurança e funcionamento:</strong> endereço IP, para limitar abusos, e registros técnicos,
          para investigar falhas e medir o desempenho do site.
        </li>
      </ul>
      <h2>Com quem compartilhamos</h2>
      <p>Só com quem precisa dos dados para atender o pedido ou operar a loja:</p>
      <ul>
        <li>fornecedores e transportadoras, para separar e entregar;</li>
        <li>o provedor de pagamento, para cobrar e estornar;</li>
        <li>o emissor de nota fiscal e os órgãos públicos, quando a lei exige;</li>
        <li>
          os serviços de e-mail, de hospedagem e de monitoramento, alguns deles com servidores fora do Brasil;
        </li>
        <li>o Google, só se você escolher entrar com a conta dele;</li>
        <li>o serviço de consulta de CEP, que recebe apenas o CEP digitado.</li>
      </ul>
      <h2>Cookies e armazenamento no navegador</h2>
      <p>
        Usamos apenas o necessário para a loja funcionar: a sessão de quem entrou, o carrinho e o último
        pedido feito. O CEP digitado fica no seu navegador, para não precisar digitar de novo. Não usamos
        cookies de publicidade.
      </p>
      <h2>Por quanto tempo guardamos</h2>
      <p>
        <mark className="bg-danger/12 text-danger-ink rounded-sm px-1">
          [Prazos a definir com o advogado e o contador, inclusive os da guarda fiscal.]
        </mark>
      </p>
      <h2>Seus direitos</h2>
      <p>
        Você pode pedir confirmação do tratamento, acesso, correção, eliminação quando cabível, informações
        sobre o compartilhamento e outros direitos do art. 18 da LGPD, em <ContactChannel />. Também pode
        procurar a Autoridade Nacional de Proteção de Dados (ANPD).
      </p>
    </PolicyPage>
  );
}
