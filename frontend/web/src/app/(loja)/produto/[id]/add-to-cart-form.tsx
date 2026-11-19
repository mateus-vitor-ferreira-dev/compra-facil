"use client";

import { ShoppingCart } from "lucide-react";
import Link from "next/link";
import { useActionState, useEffect, useRef, useState } from "react";
import { QuantityStepper } from "@/components/commerce/quantity-stepper";
import { Button, buttonVariants } from "@/components/ui/button";
import { TextField } from "@/components/ui/field";
import { useToast } from "@/components/ui/toast";
import { colorHex } from "@/lib/colors";
import { normalizePersonalization, type PersonalizationRule } from "@/lib/personalization";
import { addToCart, type AddState } from "../../carrinho/actions";

/**
 * Quantidade e "Adicionar ao carrinho". Funciona sem JavaScript (form com server action); com ele,
 * mostra o andamento no botão, confirma num aviso e oferece ir ao carrinho.
 */
export function AddToCartForm({
  productId,
  seenPriceCents,
  max,
  sizes = [],
  colors = [],
  personalization,
}: {
  productId: string;
  /** o preço que esta página mostrou: se mudar até o pedido, o carrinho avisa */
  seenPriceCents: number;
  max?: number;
  /** os tamanhos à escolha; vazio, o produto não tem tamanho */
  sizes?: string[];
  /** as cores à escolha; vazio, o produto não tem cor à escolha */
  colors?: string[];
  /** o campo de texto do cliente (ex.: o nome na estampa); nulo, o produto não tem */
  personalization?: PersonalizationRule | null;
}) {
  const [state, action, pending] = useActionState<AddState, FormData>(addToCart, { status: "idle" });
  const { show } = useToast();
  const last = useRef<AddState>(state);

  useEffect(() => {
    if (state === last.current) return;
    last.current = state;
    if (state.status === "added") {
      show(
        "success",
        state.limited
          ? `Adicionado ao carrinho até o limite disponível: ${state.qty} no total${sizeNote(state.size)}${colorNote(state.color)}${textNote(state.text)}.`
          : `Adicionado ao carrinho: ${state.qty} ${state.qty === 1 ? "unidade" : "unidades"} no total${sizeNote(state.size)}${colorNote(state.color)}${textNote(state.text)}.`,
      );
    }
  }, [state, show]);

  return (
    <form action={action} className="space-y-3">
      <input type="hidden" name="product_id" value={productId} />
      <input type="hidden" name="seen_price_cents" value={seenPriceCents} />
      {sizes.length > 0 && <SizePicker productId={productId} sizes={sizes} />}
      {colors.length > 0 && <ColorPicker productId={productId} colors={colors} />}
      {personalization && <PersonalizationField productId={productId} rule={personalization} />}
      <div className="flex flex-wrap items-end gap-3">
        <QuantityStepper id={`qtd-${productId}`} max={max} />
        <Button type="submit" disabled={pending} className="min-w-0 flex-1 sm:flex-none sm:px-8">
          <ShoppingCart aria-hidden="true" />
          {pending ? "Adicionando…" : "Adicionar ao carrinho"}
        </Button>
      </div>
      {state.status === "error" && (
        <p role="alert" className="text-danger text-sm font-medium">
          {state.message}
        </p>
      )}
      {state.status === "added" && (
        <Link
          href="/carrinho"
          className={buttonVariants({ variant: "secondary", className: "w-full sm:w-auto" })}
        >
          Ver o carrinho
        </Link>
      )}
    </form>
  );
}

const sizeNote = (size?: string) => (size ? ` no tamanho ${size}` : "");
const colorNote = (color?: string) => (color ? `, cor ${color}` : "");
const textNote = (text?: string) => (text ? `, com "${text}"` : "");

/**
 * O texto do cliente para a arte (ex.: o nome na estampa), com o contador. O contador conta como a arte (sem os
 * espaços das pontas nem os repetidos); o campo deixa digitar um pouco além, para os espaços não cortarem letras, e
 * o servidor confere o máximo e o que a fonte imprime.
 */
function PersonalizationField({ productId, rule }: { productId: string; rule: PersonalizationRule }) {
  const [text, setText] = useState("");
  const used = [...normalizePersonalization(text)].length;
  const over = used > rule.max_length;
  return (
    <div className="relative">
      <TextField
        id={`perso-${productId}`}
        name="personalization"
        label={rule.required ? rule.label : `${rule.label} (opcional)`}
        hint={`Até ${rule.max_length} caracteres: letras, números, espaço e . , ' - & ! ? /. Sai na arte do jeito que você escrever.`}
        maxLength={rule.max_length * 2}
        required={rule.required}
        autoComplete="off"
        value={text}
        onChange={(e) => setText(e.target.value)}
      />
      <span
        aria-hidden="true"
        className={`absolute top-0 right-0 text-sm tabular-nums ${over ? "text-danger font-semibold" : "text-ink-soft"}`}
      >
        {used}/{rule.max_length}
      </span>
    </div>
  );
}

/**
 * Os tamanhos como opções de rádio em botões (44 px no toque): um grupo nativo, que o teclado percorre com as
 * setas e o leitor de tela anuncia como "Tamanho". Nenhum vem marcado: o cliente escolhe, e o navegador não
 * envia o formulário sem a escolha (o servidor confere de novo).
 */
function SizePicker({ productId, sizes }: { productId: string; sizes: string[] }) {
  return (
    <fieldset className="space-y-1">
      <legend className="text-ink-soft text-sm">Tamanho</legend>
      <div className="flex flex-wrap gap-2">
        {sizes.map((size) => (
          <label key={size} className="relative">
            <input
              type="radio"
              name="size"
              value={size}
              required
              id={`tam-${productId}-${size}`}
              className="peer sr-only"
            />
            <span className="border-field-line bg-surface text-ink peer-checked:border-ink peer-checked:bg-ink peer-checked:text-surface peer-focus-visible:outline-focus grid h-11 min-w-11 cursor-pointer place-items-center rounded-md border px-3 font-semibold peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2">
              {size}
            </span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

/**
 * As cores como bolinhas (44 px no toque), num grupo de rádio nativo: o nome de cada uma é o rótulo acessível, e o
 * da escolhida aparece por escrito ao lado do título. Nenhuma vem marcada: o cliente escolhe.
 */
function ColorPicker({ productId, colors }: { productId: string; colors: string[] }) {
  const [chosen, setChosen] = useState<string>();
  return (
    <fieldset className="space-y-1">
      <legend className="text-ink-soft text-sm">
        Cor{chosen && <span className="text-ink font-medium">: {chosen}</span>}
      </legend>
      <div className="flex flex-wrap gap-2">
        {colors.map((color) => {
          const hex = colorHex(color);
          return (
            <label key={color} className="relative" title={color}>
              <input
                type="radio"
                name="color"
                value={color}
                required
                id={`cor-${productId}-${color}`}
                onChange={() => setChosen(color)}
                className="peer sr-only"
              />
              <span className="sr-only">{color}</span>
              <span
                aria-hidden="true"
                className="border-field-line peer-checked:ring-ink peer-focus-visible:outline-focus grid size-11 cursor-pointer place-items-center rounded-full border p-1 peer-checked:ring-2 peer-checked:ring-offset-2 peer-focus-visible:outline-2 peer-focus-visible:outline-offset-4"
              >
                <span
                  className="border-line block size-full rounded-full border"
                  style={{ background: hex ?? "repeating-linear-gradient(45deg,#ddd 0 4px,#f5f5f5 4px 8px)" }}
                />
              </span>
            </label>
          );
        })}
      </div>
    </fieldset>
  );
}
