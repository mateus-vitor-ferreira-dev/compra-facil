"use client";

import { useActionState } from "react";
import { addCampaignProducts } from "@/app/admin/campanhas/actions";
import type { FormState } from "@/app/admin/produtos/actions";
import { SubmitButton } from "@/components/ui/submit-button";
import { FormError } from "./form-error";

type Candidate = { id: string; name: string; sku: string; price: string; status: string };

/** Os produtos achados pela busca, para marcar e incluir de uma vez na campanha. */
export function AddProductsForm({ campaignId, candidates }: { campaignId: string; candidates: Candidate[] }) {
  const [state, action] = useActionState<FormState, FormData>(addCampaignProducts, {});
  return (
    <form action={action} className="space-y-3">
      <input type="hidden" name="id" value={campaignId} />
      <FormError error={state.error} />
      <ul className="divide-line border-line divide-y rounded-md border">
        {candidates.map((c) => (
          <li key={c.id}>
            <label className="hover:bg-ink/4 has-[:focus-visible]:outline-focus flex min-h-11 cursor-pointer items-center gap-3 px-3 py-2 has-[:focus-visible]:outline-2">
              <input
                type="checkbox"
                name="product_id"
                value={c.id}
                className="accent-ink size-4 focus-visible:outline-none"
              />
              <span className="min-w-0 flex-1">
                <span className="block truncate font-medium">{c.name}</span>
                <span className="text-ink-soft block text-xs">
                  {c.sku}, {c.status}
                </span>
              </span>
              <span className="font-condensed font-bold whitespace-nowrap">{c.price}</span>
            </label>
          </li>
        ))}
      </ul>
      <SubmitButton variant="secondary" pendingLabel="Incluindo…">
        Incluir os marcados
      </SubmitButton>
    </form>
  );
}
