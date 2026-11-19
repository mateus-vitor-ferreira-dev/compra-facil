# Artes de impressão (personalização)

O "Baixar arte" do painel gera, no navegador, o PNG de impressão do texto do cliente: 300 DPI (bloco pHYs, `png.ts`),
fundo transparente, no tamanho da Montink para a peça (`PRINT_SIZES` em `templates.ts`).

O produto aponta para a arte pelo id em `personalization.template` (catalog), no formato `base` ou `base:variante`:

- **Tipográficas** (`templates.ts`, `render.ts`): `nome-classico`, `familia-sobrenome`, `desde-ano`, `caneca-nome`,
  com as variantes de kicker, rodapé e gorro de Natal (ex.: `nome-classico:mae`). A peça é adivinhada pelo nome do
  item e dá para trocar.
- **Ilustradas** (`ilustradas.json`, `illustrated.ts`, `render-illustrated.ts`): `colecao:nome` (no json,
  `colecao/nome`). A ilustração fica em `public/arte` (PNG de 2000 px: menos que isso sai borrado no peito da
  camiseta adulto, onde ela ocupa uns 2200 px), com a versão `-escura` de contorno de adesivo. A versão vem da cor do
  item: Branco e as claras usam a clara; Preto, Vinho, Roxo e Marinho, a escura; Vermelho, a vermelha. A linha
  `editavel` recebe o texto do cliente. O desenho é o do motor das estampas: ilustração centralizada em cima, kicker
  com espaçamento e ornamentos, texto grande ajustado a 86% da largura, rodapé; a caneca repete a arte nas duas
  metades.

As fontes são OFL, em `public/fonts` com as licenças: DM Serif Display, Dancing Script, Pacifico, Anton, Baloo 2,
Inter e Inter Display. Fontes e ilustrações só carregam quando alguém baixa uma arte.

Para incluir uma arte ilustrada: a ilustração clara e a escura em `public/arte` e a entrada em `ilustradas.json`; o
teste `illustrated.test.ts` confere o id, os arquivos e o máximo da linha editável.
