"use client";

import { useActionState } from "react";
import { saveProduct, type FormState } from "@/app/admin/produtos/actions";
import { TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { cn } from "@/lib/utils";
import { FormError } from "./form-error";
import { STATUS_LABEL } from "./product-status";

export type ProductFormValues = {
  id?: string;
  etag?: string;
  name: string;
  sku: string;
  brand: string;
  description: string;
  category_id: string;
  image_url: string;
  price: string;
  status: "draft" | "active" | "inactive";
};

type CategoryOption = { id: string; name: string; children: { id: string; name: string }[] };

const STATUS_HINT: Record<ProductFormValues["status"], string> = {
  active: "aparece na vitrine",
  draft: "ainda em cadastro, fora da vitrine",
  inactive: "saiu de venda, fora da vitrine",
};

const control =
  "bg-surface border-ink/25 w-full rounded-md border px-3 outline-none focus-visible:outline-2 aria-invalid:border-danger";

/**
 * Criar e editar produto. Na edição vai o ETag da leitura (If-Match): se outra pessoa gravou antes, o
 * catálogo recusa e o formulário explica, em vez de apagar a mudança dela.
 */
export function ProductForm({
  initial,
  categories,
}: {
  initial: ProductFormValues;
  categories: CategoryOption[];
}) {
  const [state, action] = useActionState<FormState, FormData>(saveProduct, {});
  const v = { ...initial, ...state.values };
  const err = state.fields ?? {};
  const described = (name: string) => (err[name] ? `${name}-erro` : undefined);

  return (
    <form action={action} className="space-y-5" noValidate>
      {initial.id && <input type="hidden" name="id" value={initial.id} />}
      {initial.etag && <input type="hidden" name="etag" value={initial.etag} />}
      <FormError error={state.error} />

      <TextField label="Nome" name="name" defaultValue={v.name} error={err.name} required maxLength={200} />
      <div className="grid gap-5 sm:grid-cols-2">
        <TextField
          label="SKU"
          name="sku"
          defaultValue={v.sku}
          error={err.sku}
          hint="Letras maiúsculas, dígitos e hífen, de 3 a 32."
          required
          autoCapitalize="characters"
          className="[&_input]:uppercase"
        />
        <TextField label="Marca" name="brand" defaultValue={v.brand} error={err.brand} maxLength={80} />
      </div>
      <div className="grid gap-5 sm:grid-cols-2">
        <div className="space-y-1.5">
          <label htmlFor="category_id" className="block font-medium">
            Categoria
          </label>
          <select
            id="category_id"
            name="category_id"
            defaultValue={v.category_id}
            required
            aria-invalid={err.category_id ? true : undefined}
            aria-describedby={described("category_id")}
            className={cn(control, "h-11")}
          >
            <option value="" disabled>
              Escolha a categoria
            </option>
            {categories.map((root) => (
              <optgroup key={root.id} label={root.name}>
                <option value={root.id}>{root.name} (geral)</option>
                {root.children.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </optgroup>
            ))}
          </select>
          {err.category_id && (
            <p id="category_id-erro" className="text-danger text-sm">
              {err.category_id}
            </p>
          )}
        </div>
        <TextField
          label="Preço de tabela"
          name="price"
          defaultValue={v.price}
          error={err.price}
          hint="Em reais, sem o desconto de campanha (ex.: 1.299,90)."
          inputMode="decimal"
          required
        />
      </div>
      <div className="space-y-1.5">
        <label htmlFor="description" className="block font-medium">
          Descrição
        </label>
        <textarea
          id="description"
          name="description"
          defaultValue={v.description}
          rows={5}
          maxLength={5000}
          aria-invalid={err.description ? true : undefined}
          aria-describedby={described("description")}
          className={cn(control, "py-2")}
        />
        {err.description && (
          <p id="description-erro" className="text-danger text-sm">
            {err.description}
          </p>
        )}
      </div>
      <TextField
        label="Foto (URL)"
        name="image_url"
        type="url"
        defaultValue={v.image_url}
        error={err.image_url}
        hint="Endereço https da foto. Vazio usa o ícone da categoria."
      />

      <div role="radiogroup" aria-labelledby="situacao" className="space-y-2">
        <p id="situacao" className="font-medium">
          Situação
        </p>
        <div className="grid gap-2 sm:grid-cols-3">
          {(Object.keys(STATUS_LABEL) as ProductFormValues["status"][]).map((s) => (
            <label
              key={s}
              className="border-ink/25 has-[:checked]:border-ink has-[:checked]:bg-ink/5 has-[:focus-visible]:outline-focus flex min-h-11 cursor-pointer gap-3 rounded-md border px-3 py-2 has-[:focus-visible]:outline-2"
            >
              <input
                type="radio"
                name="status"
                value={s}
                defaultChecked={v.status === s}
                className="accent-ink mt-1 size-4 focus-visible:outline-none"
              />
              <span>
                <span className="block font-medium">{STATUS_LABEL[s]}</span>
                <span className="text-ink-soft block text-sm">{STATUS_HINT[s]}</span>
              </span>
            </label>
          ))}
        </div>
      </div>

      <SubmitButton pendingLabel="Salvando…">
        {initial.id ? "Salvar alterações" : "Cadastrar produto"}
      </SubmitButton>
    </form>
  );
}
