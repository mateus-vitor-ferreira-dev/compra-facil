import type { Metadata } from "next";
import Link from "next/link";
import type { ReactNode } from "react";
import { ADMIN_GROUPS } from "@/components/admin/admin-sections";
import { SCREEN_PURPOSE } from "@/lib/admin-manual";
import { requireAdmin } from "@/lib/auth/current";
import { cn } from "@/lib/utils";

export const metadata: Metadata = { title: "Manual" };

/** As partes do manual, na ordem da página: o índice "Nesta página" sai daqui. */
const PARTS = [
  { id: "caminho", title: "O caminho de um pedido" },
  { id: "fora-do-roteiro", title: "Quando algo sai do roteiro" },
  { id: "a-venda", title: "Pôr um produto à venda" },
  { id: "montink-produto", title: "Criar o produto na Montink" },
  { id: "montink-pedido", title: "Fazer o pedido manual na Montink" },
  { id: "dinheiro", title: "O dinheiro de um pedido" },
  { id: "telas", title: "O que cada tela faz" },
  { id: "ainda-nao", title: "O que ainda não dá para fazer" },
] as const;

type Step = { title: string; body: ReactNode; screen?: { href: string; label: string } };

/** Toda venda passa por estas etapas, nesta ordem; cada uma leva à tela em que ela é feita. */
const ORDER_PATH: Step[] = [
  {
    title: "O cliente paga",
    body: (
      <>
        <p>
          Quando o PIX ou o cartão é aprovado, o pedido aparece sozinho em{" "}
          <strong>A fazer no fornecedor</strong>, os mais antigos primeiro. A borda escurece depois de 4 horas
          e fica vermelha depois de 24.
        </p>
        <p>
          Cada pedido vira uma ficha com tudo o que a Montink pede, na ordem dela: os itens com tamanho e cor
          em destaque, e o nome, o CPF, o telefone e o endereço do cliente, cada um com o botão de copiar.
        </p>
      </>
    ),
    screen: { href: "/admin/pedidos-a-fazer", label: "Abrir Pedidos a fazer" },
  },
  {
    title: "Veja o que fazer na Montink",
    body: (
      <>
        <p>
          Cada item da ficha tem a linha <strong>Na Montink</strong>, que diz o caminho:
        </p>
        <ul>
          <li>
            <strong>Produto 6167609</strong>: o produto já existe lá. Copie o número e peça esse produto.
          </li>
          <li>
            <strong>Crie um produto com a arte deste pedido</strong>: o item leva o texto do cliente. Use o
            botão <strong>Baixar arte</strong> da ficha, que gera o arquivo de impressão com o texto já na
            versão da cor da peça, e crie o produto na Montink com ele (veja{" "}
            <a href="#montink-produto">Criar o produto na Montink</a>).
          </li>
        </ul>
      </>
    ),
  },
  {
    title: "Faça o pedido na Montink",
    body: (
      <p>
        Em <strong>Pedidos › Criar pedido</strong>: o produto e a variante (tamanho e cor), o cliente da
        ficha, o frete, o saldo e <strong>Completar pedido</strong>. O passo a passo está em{" "}
        <a href="#montink-pedido">Fazer o pedido manual na Montink</a>.
      </p>
    ),
  },
  {
    title: "Registre o pedido feito",
    body: (
      <>
        <p>Volte à ficha e preencha o formulário do fim dela:</p>
        <ul>
          <li>
            <strong>Nº do pedido no fornecedor</strong>: o número do pedido na Montink.
          </li>
          <li>
            <strong>Pago nos produtos</strong> e <strong>Pago no frete</strong>: o que saiu da carteira, como
            aparece no extrato da Montink (<strong>Financeiro › Carteira › Transações</strong>). Não use o
            “Custo” da tela do pedido: ele inclui uma taxa que o pedido manual não cobra.
          </li>
        </ul>
        <p>
          Clique em <strong>Registrar o pedido feito</strong>. O cliente passa a ver “enviado ao fornecedor”,
          e a margem do pedido passa a usar esses valores.
        </p>
      </>
    ),
    screen: { href: "/admin/pedidos-a-fazer", label: "Abrir Pedidos a fazer" },
  },
  {
    title: "Lance o rastreio",
    body: (
      <p>
        Quando a Montink postar (ela produz em uns 4 dias úteis), o pedido estará em{" "}
        <strong>Esperando o rastreio</strong>. Preencha o código, a transportadora e, se tiver, o link, e
        clique em <strong>Enviar o rastreio ao cliente</strong>. O cliente recebe o e-mail na hora.
      </p>
    ),
    screen: { href: "/admin/pedidos-a-fazer#rastreio", label: "Abrir Esperando o rastreio" },
  },
  {
    title: "Marque a entrega",
    body: (
      <p>
        Em <strong>Em transporte</strong>, quando o rastreio mostrar a entrega, clique em{" "}
        <strong>Marcar como entregue</strong>. A partir daí a margem do pedido fecha e não muda mais.
      </p>
    ),
    screen: { href: "/admin/pedidos-a-fazer#transporte", label: "Abrir Em transporte" },
  },
];

const DETOURS: { title: string; body: ReactNode }[] = [
  {
    title: "Digitei o custo errado",
    body: (
      <>
        Em <strong>Esperando o rastreio</strong> ou <strong>Em transporte</strong>, abra{" "}
        <strong>Corrigir custo</strong> no pedido. Os valores atuais vêm preenchidos: troque pelos do extrato
        e salve. O valor anterior fica guardado. Depois de entregue, não dá mais.
      </>
    ),
  },
  {
    title: "A Montink não tem a peça",
    body: (
      <>
        Na ficha, <strong>O fornecedor não tem o produto</strong> ›{" "}
        <strong>Cancelar por falta de estoque</strong>. O pedido é cancelado e o cliente recebe o estorno
        inteiro, sozinho.
      </>
    ),
  },
  {
    title: "Preciso cancelar um pedido",
    body: (
      <>
        Na fila, <strong>Cancelar o pedido</strong>. Se ele já foi feito na Montink, cancele lá primeiro: o
        painel pede a confirmação antes de estornar o cliente.
      </>
    ),
  },
  {
    title: "Quero ver um pedido",
    body: (
      <>
        Em <Link href="/admin/pedidos">Pedidos</Link>: busca por número, e-mail ou nome, com os itens, a
        entrega e as datas de cada etapa.
      </>
    ),
  },
];

/**
 * O manual da equipe (#483 em diante), dentro do painel: como atender um pedido do começo ao fim, pelo painel e
 * pela Montink. O repositório é público: aqui não entram custos, margens nem dados de pedidos reais.
 */
export default async function ManualPage() {
  await requireAdmin("/admin/manual");

  return (
    <div className="xl:grid xl:grid-cols-[minmax(0,48rem)_14rem] xl:gap-12">
      {/* o texto corrido: listas com marcador, links sublinhados (menos os botões) e código em caixa */}
      <article className="[&_code]:bg-surface [&_code]:border-line min-w-0 space-y-12 [&_a:not([data-button])]:font-medium [&_a:not([data-button])]:underline [&_a:not([data-button])]:underline-offset-4 [&_code]:rounded [&_code]:border [&_code]:px-1 [&_code]:text-[0.9em] [&_code]:[overflow-wrap:anywhere] [&_ul]:list-disc [&_ul]:space-y-1 [&_ul]:pl-5">
        <header className="space-y-2">
          <h1 className="text-3xl font-semibold tracking-tight">Manual do painel</h1>
          <p className="text-ink-soft max-w-prose text-lg">
            Como atender um pedido do começo ao fim, pelo painel e pela Montink. Vale para o fornecedor no
            modo manual, que é como a loja funciona hoje.
          </p>
        </header>

        {/* no celular e no tablet, o índice abre aqui; nas telas largas, ele fica preso ao lado */}
        <details className="border-line rounded-md border xl:hidden">
          <summary className="flex min-h-11 cursor-pointer items-center px-4 font-semibold">
            Nesta página
          </summary>
          <ol className="border-line border-t px-2 py-1">
            {PARTS.map((p) => (
              <li key={p.id}>
                <a
                  href={`#${p.id}`}
                  data-button
                  className="hover:bg-ink/6 flex min-h-11 items-center rounded px-2"
                >
                  {p.title}
                </a>
              </li>
            ))}
          </ol>
        </details>

        <Part
          id="caminho"
          lede="Toda venda passa por estas etapas, nesta ordem. O painel mostra em qual delas cada pedido está."
        >
          <Rail steps={ORDER_PATH} />
        </Part>

        <Part id="fora-do-roteiro">
          <dl className="divide-line border-line divide-y border-y">
            {DETOURS.map((d) => (
              <div key={d.title} className="grid gap-1 py-4 sm:grid-cols-[13rem_minmax(0,1fr)] sm:gap-6">
                <dt className="font-semibold">{d.title}</dt>
                <dd className="text-ink-soft max-w-prose">{d.body}</dd>
              </div>
            ))}
          </dl>
        </Part>

        <Part
          id="a-venda"
          lede="A vitrine só vende o que a Montink consegue fazer: um produto só fica à venda com o código dele na Montink."
        >
          <Rail
            steps={[
              {
                title: "Crie o produto na Montink",
                body: (
                  <p>
                    Veja <a href="#montink-produto">Criar o produto na Montink</a> e anote o número dele: é o
                    número no fim do endereço da página do produto, ou o da lista em{" "}
                    <strong>Meus produtos</strong>.
                  </p>
                ),
              },
              {
                title: "Abra o produto no painel",
                body: (
                  <p>
                    Em <strong>Cores</strong>, deixe só as cores que existem na Montink.
                  </p>
                ),
                screen: { href: "/admin/produtos", label: "Abrir Produtos" },
              },
              {
                title: "Preencha o Código na Montink",
                body: (
                  <ul>
                    <li>Branco e preto no mesmo produto da Montink (o normal): só o número.</li>
                    <li>
                      Uma cor que é outro produto lá, como o vermelho: um por cor, como em{" "}
                      <code>Branco: 6167609, Preto: 6167609, Vermelho: 6170123</code>.
                    </li>
                    <li>Caneca e produto sem cor à escolha: só o número.</li>
                  </ul>
                ),
              },
              {
                title: "Marque À venda e salve",
                body: (
                  <p>
                    Se for o primeiro produto à venda da coleção, ela volta sozinha ao menu, ao rodapé, à home
                    e ao sitemap.
                  </p>
                ),
              },
            ]}
          />
          <Note title="Feito por pedido">
            O produto com o texto do cliente obrigatório (aniversário, mesversário, “Juntos desde”) fica à
            venda sem código: a arte dele só existe depois da compra, e a equipe cria o produto na Montink a
            cada pedido. Na lista de produtos ele aparece como “feito por pedido na Montink”.
          </Note>
          <Note title="Coleção sem produto à venda">
            Some da loja inteira, e a página dela dá “página não encontrada”, sem ficar vazia nos buscadores.
            Nada é apagado: os rascunhos continuam no painel.
          </Note>
        </Part>

        <Part
          id="montink-produto"
          lede="Uma vez por estampa e por versão de cor da arte (a clara serve para peça branca; a escura, para preta), ou uma vez por pedido no produto com o texto do cliente. Em Produtos › Criar produto:"
        >
          <Rail
            steps={[
              {
                title: "Escolha a peça",
                body: (
                  <p>
                    <strong>T-Shirt Classic</strong> (adulto), <strong>Classic Infantil</strong>,{" "}
                    <strong>Body Infantil</strong>, <strong>Camisa Pet Dog</strong> ou <strong>Caneca</strong>
                    , e clique em <strong>Personalizar</strong>.
                  </p>
                ),
              },
              {
                title: "Marque as cores",
                body: (
                  <p>
                    Em <strong>Configurações</strong>, deixe marcadas só as cores da versão da arte que você
                    vai enviar.
                  </p>
                ),
              },
              {
                title: "Envie a arte",
                body: (
                  <p>
                    Em <strong>Frente</strong>, deixe o tamanho <strong>Padrão</strong>, clique no quadradinho
                    da cor e envie o PNG. Ele fica na biblioteca de estampas para a próxima vez.{" "}
                    <strong>Costas</strong>: “Sem estampa”.
                  </p>
                ),
              },
              {
                title: "Finalize e publique",
                body: (
                  <p>
                    <strong>Finalizar</strong> › <strong>Salvar e configurar Venda</strong>. Dê um nome com a
                    cor, gere um mockup e clique em <strong>Publicar Produto</strong>. Um mockup basta: as
                    fotos que o cliente vê são as da loja.
                  </p>
                ),
              },
              {
                title: "Leve o número para o painel",
                body: (
                  <p>
                    Copie o número do produto publicado para o <strong>Código na Montink</strong> do produto
                    no painel (veja <a href="#a-venda">Pôr um produto à venda</a>). No produto feito por
                    pedido, não precisa: peça direto o produto que acabou de criar.
                  </p>
                ),
              },
            ]}
          />
        </Part>

        <Part id="montink-pedido" lede="Em Pedidos › Criar pedido:">
          <Rail
            steps={[
              {
                title: "Adicione o produto",
                body: (
                  <p>
                    <strong>Adicionar produto</strong>, marque a variante certa (tamanho e cor da ficha) e{" "}
                    <strong>Adicionar</strong>.
                  </p>
                ),
              },
              {
                title: "Preencha o cliente",
                body: (
                  <p>
                    Em <strong>Contato</strong>, “Pesquisar ou criar um cliente” com os dados da ficha (use os
                    botões de copiar do painel).
                  </p>
                ),
              },
              {
                title: "Escolha o frete",
                body: (
                  <p>
                    <strong>Calcular frete</strong> e escolha a opção que chega antes da data prometida ao
                    cliente, que aparece na ficha.
                  </p>
                ),
              },
              {
                title: "Salve o rascunho",
                body: (
                  <p>
                    <strong>Salvar rascunho</strong> antes de sair para pagar: se a página recarregar, o
                    rascunho não salvo se perde.
                  </p>
                ),
              },
              {
                title: "Pague e complete",
                body: (
                  <p>
                    <strong>Adicionar saldo</strong> com o valor do total (o “$” do campo é só o ícone; o
                    valor é em reais), pague o PIX e clique em <strong>Completar pedido</strong>.
                  </p>
                ),
              },
            ]}
          />
          <Note title="Se o link de pagamento não abrir">
            Algumas redes bloqueiam o endereço de pagamento da Montink. Pague pelo QR code ou pelo “copia e
            cola”, ou abra o link no celular com os dados móveis.
          </Note>
        </Part>

        <Part id="dinheiro" lede="A margem de cada pedido sai desta conta, e aparece em Margens:">
          <dl className="border-line divide-line max-w-prose divide-y border-y tabular-nums">
            {(
              [
                ["O cliente pagou", "o produto e o frete cobrados na loja"],
                ["− taxa do pagamento", "a da Efí, que entra sozinha"],
                ["− produto na Montink", "o “Pago nos produtos” que você registra"],
                ["− frete da Montink", "o “Pago no frete” que você registra"],
                ["= margem", "fecha quando o pedido é entregue"],
              ] as const
            ).map(([term, detail]) => (
              <div key={term} className="grid grid-cols-[11rem_minmax(0,1fr)] gap-4 py-2.5">
                <dt className={cn("font-medium", term.startsWith("=") && "font-bold")}>{term}</dt>
                <dd className="text-ink-soft">{detail}</dd>
              </div>
            ))}
          </dl>
          <p>
            A carteira da Montink só recebe depósitos para pagar pedidos. A conta bancária cadastrada lá serve
            só para sacar; na lista de bancos, a Efí aparece com o nome antigo,{" "}
            <strong>Gerencianet Pagamentos do Brasil</strong>.
          </p>
        </Part>

        <Part id="telas">
          <div className="space-y-6">
            {ADMIN_GROUPS.map((group) => (
              <section key={group.label} aria-labelledby={`telas-${group.label}`} className="space-y-2">
                <h3 id={`telas-${group.label}`} className="text-ink-soft font-semibold">
                  {group.label}
                </h3>
                <dl className="divide-line border-line divide-y border-y">
                  {group.sections.map((s) => (
                    <div
                      key={s.href}
                      className="grid gap-1 py-2.5 sm:grid-cols-[13rem_minmax(0,1fr)] sm:gap-6"
                    >
                      <dt>
                        <Link
                          href={s.href}
                          data-button
                          className="inline-flex items-center gap-2 font-medium hover:underline"
                        >
                          <s.Icon aria-hidden="true" className="text-ink-soft size-4" />
                          {s.label}
                        </Link>
                      </dt>
                      <dd className="text-ink-soft">{SCREEN_PURPOSE[s.href]}</dd>
                    </div>
                  ))}
                </dl>
              </section>
            ))}
          </div>
        </Part>

        <Part id="ainda-nao">
          <ul>
            <li>
              <strong>Enviar fotos de produto</strong>: o cadastro pede o endereço da foto, já publicada no
              servidor de mídia da loja.
            </li>
            <li>
              <strong>Cadastrar muitos produtos de uma vez</strong>: no painel é um por vez. Para muitos, use
              o importador do catálogo, com um código do app autenticador.
            </li>
            <li>
              <strong>Mandar o pedido para a Montink sozinho</strong>: a integração com a API da Montink
              tiraria as etapas 3, 4 e 5 do caminho do pedido (pedido, custo e rastreio automáticos). Está em
              estudo.
            </li>
          </ul>
        </Part>
      </article>

      <nav aria-label="Nesta página" className="hidden xl:block">
        <div className="sticky top-6 space-y-2">
          <p className="text-ink-soft text-sm font-semibold">Nesta página</p>
          <ol className="border-line space-y-1 border-l text-sm">
            {PARTS.map((p) => (
              <li key={p.id}>
                <a
                  href={`#${p.id}`}
                  className="text-ink-soft hover:text-ink hover:border-ink -ml-px block border-l-2 border-transparent py-1.5 pl-3"
                >
                  {p.title}
                </a>
              </li>
            ))}
          </ol>
        </div>
      </nav>
    </div>
  );
}

/** Uma parte do manual: o título vem de PARTS, para o índice e a página nunca divergirem. */
function Part({
  id,
  lede,
  children,
}: {
  id: (typeof PARTS)[number]["id"];
  lede?: string;
  children: ReactNode;
}) {
  const title = PARTS.find((p) => p.id === id)!.title;
  return (
    <section id={id} aria-labelledby={`${id}-titulo`} className="scroll-mt-6 space-y-4">
      <h2 id={`${id}-titulo`} className="text-2xl font-semibold tracking-tight">
        {title}
      </h2>
      {lede && <p className="text-ink-soft max-w-prose">{lede}</p>}
      {children}
    </section>
  );
}

/**
 * Uma sequência de verdade: os números ficam presos a uma linha vertical, que mostra a ordem. A etapa que acontece
 * numa tela do painel traz o link para ela.
 */
function Rail({ steps }: { steps: Step[] }) {
  return (
    <ol className="max-w-prose">
      {steps.map((step, i) => (
        <li key={step.title} className="relative grid grid-cols-[2.5rem_minmax(0,1fr)] gap-4 pb-8 last:pb-0">
          {i < steps.length - 1 && (
            <span
              aria-hidden="true"
              className="bg-line absolute top-10 bottom-0 left-5 w-px -translate-x-1/2"
            />
          )}
          <span
            aria-hidden="true"
            className="bg-ink text-paper font-condensed relative flex size-10 items-center justify-center rounded-full text-lg font-bold"
          >
            {i + 1}
          </span>
          <div className="min-w-0 space-y-2 pt-1.5">
            <h3 className="text-lg font-semibold">
              <span className="sr-only">Etapa {i + 1}: </span>
              {step.title}
            </h3>
            <div className="space-y-2">{step.body}</div>
            {step.screen && (
              <Link
                href={step.screen.href}
                data-button
                className="bg-tag text-tag-ink inline-flex min-h-11 items-center rounded-md px-3 text-sm font-semibold"
              >
                {step.screen.label}
              </Link>
            )}
          </div>
        </li>
      ))}
    </ol>
  );
}

function Note({ title, children }: { title: string; children: ReactNode }) {
  return (
    <aside className="border-ink max-w-prose border-l-4 py-1 pl-4">
      <p className="font-semibold">{title}</p>
      <p className="text-ink-soft">{children}</p>
    </aside>
  );
}
