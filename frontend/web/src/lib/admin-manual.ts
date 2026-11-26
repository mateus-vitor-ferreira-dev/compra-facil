/**
 * O que cada tela do painel faz, nas palavras do manual (/admin/manual). A chave é o href do menu (admin-nav): o teste
 * falha se uma tela nova entra no menu sem a frase dela aqui, e o manual não fica para trás.
 */
export const SCREEN_PURPOSE: Record<string, string> = {
  "/admin": "As vendas, dia a dia.",
  "/admin/pedidos": "Todos os pedidos, com busca e o detalhe de cada um.",
  "/admin/margens":
    "Quanto sobrou em cada pedido: o que o cliente pagou menos a taxa do pagamento, o produto e o frete.",
  "/admin/produtos":
    "Criar e editar produtos (preço, tamanhos, cores, personalização, fotos e código na Montink) e o estoque de cada um.",
  "/admin/categorias":
    "As coleções da vitrine, com quantos produtos estão à venda em cada uma. A sem nenhum fica fora da loja.",
  "/admin/campanhas": "Promoções com desconto e prazo.",
  "/admin/fornecedor":
    "A planilha do fornecedor, com o custo de cada produto, e a margem que dá o preço de venda.",
  "/admin/pedidos-a-fazer": "O dia a dia: a fila, o pedido feito, o rastreio, a entrega e as correções.",
  "/admin/atrasos": "Pedidos que passaram do prazo do fornecedor ou não chegaram a ele.",
  "/admin/devolucoes": "Pedidos que o cliente devolveu.",
  "/admin/falhas": "Mensagens entre os sistemas que falharam, para reenviar.",
  "/admin/manutencao": "Fechar a loja para manutenção, com o aviso para o cliente.",
  "/admin/seguranca": "O app autenticador e os códigos de recuperação.",
  "/admin/manual": "Este manual.",
};
