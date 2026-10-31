"use client";

import { useRef, useState } from "react";
import { TextField } from "@/components/ui/field";
import {
  formatCpf,
  formatPhone,
  formatZipCode,
  STATES,
  type CepAddress,
  type DeliveryErrors,
  type DeliveryField,
  type DeliveryInput,
} from "@/lib/delivery";
import { cn } from "@/lib/utils";

type Props = {
  delivery: DeliveryInput;
  errors: DeliveryErrors;
  /** veio do pedido anterior: o cliente só confere */
  remembered: boolean;
  onChange: (field: DeliveryField, value: string) => void;
  onFill: (values: Partial<DeliveryInput>) => void;
  register: (field: DeliveryField, el: HTMLElement | null) => void;
};

type Lookup = "idle" | "loading" | "found" | "missing" | "down";

const LOOKUP_TEXT: Record<Lookup, string> = {
  idle: "",
  loading: "Buscando o endereço do CEP…",
  found: "Endereço do CEP preenchido. Confira e digite o número.",
  missing: "Não achamos este CEP. Confira os dígitos ou digite o endereço.",
  down: "A busca do CEP não respondeu. Digite o endereço.",
};

/**
 * Quem recebe e onde (#221): o nome e o CPF vão na nota fiscal; o telefone e o endereço, ao fornecedor.
 * Com os 8 dígitos do CEP, a rua, o bairro, a cidade e o estado se preenchem, e o foco vai para o
 * número. Os campos têm `name` e autocomplete: sem JavaScript, o formulário funciona igual.
 */
export function DeliveryFields({ delivery, errors, remembered, onChange, onFill, register }: Props) {
  const [lookup, setLookup] = useState<Lookup>("idle");
  const asked = useRef("");

  async function findZipCode(value: string) {
    const cep = value.replace(/\D/g, "");
    if (cep.length !== 8 || cep === asked.current) return;
    asked.current = cep;
    setLookup("loading");
    try {
      const response = await fetch(`/api/cep/${cep}`);
      if (asked.current !== cep) return; // o cliente já digitou outro
      if (response.status === 404) return setLookup("missing");
      if (!response.ok) return setLookup("down");
      const found = (await response.json()) as CepAddress;
      // CEP de cidade pequena vem sem rua e bairro: só o que veio substitui o que está no campo
      onFill(Object.fromEntries(Object.entries(found).filter(([, v]) => v)) as Partial<DeliveryInput>);
      setLookup("found");
      document.getElementById("entrega-number")?.focus();
    } catch {
      setLookup("down");
    }
  }

  const field = (name: DeliveryField) => ({
    id: `entrega-${name}`,
    name,
    value: delivery[name],
    error: errors[name],
    ref: (el: HTMLInputElement | null) => register(name, el),
  });

  return (
    <section aria-labelledby="entrega" className="space-y-4">
      <div className="space-y-1">
        <h2 id="entrega" className="text-xl font-semibold tracking-tight">
          Entrega
        </h2>
        <p className="text-ink-soft max-w-prose text-sm">
          {remembered
            ? "Os dados do seu último pedido. Confira antes de pagar."
            : "O nome e o CPF vão na nota fiscal; o telefone é para a transportadora."}
        </p>
      </div>

      <div className="grid grid-cols-6 gap-4">
        <TextField
          {...field("name")}
          label="Nome de quem recebe"
          autoComplete="name"
          onChange={(e) => onChange("name", e.target.value)}
          className="col-span-6"
        />
        <TextField
          {...field("cpf")}
          label="CPF"
          inputMode="numeric"
          autoComplete="off"
          onChange={(e) => onChange("cpf", formatCpf(e.target.value))}
          className="col-span-3"
        />
        <TextField
          {...field("phone")}
          label="Celular"
          type="tel"
          autoComplete="tel-national"
          onChange={(e) => onChange("phone", formatPhone(e.target.value))}
          className="col-span-3"
        />
        <div className="col-span-3 sm:col-span-2">
          <TextField
            {...field("zip_code")}
            label="CEP"
            inputMode="numeric"
            autoComplete="postal-code"
            aria-describedby="entrega-cep-busca"
            onChange={(e) => {
              const next = formatZipCode(e.target.value);
              onChange("zip_code", next);
              void findZipCode(next);
            }}
          />
        </div>
        <p
          id="entrega-cep-busca"
          aria-live="polite"
          className={cn(
            "col-span-6 text-sm sm:col-span-4 sm:self-end sm:pb-3",
            lookup === "missing" || lookup === "down" ? "text-ink" : "text-ink-soft",
            lookup === "idle" && "hidden sm:block",
          )}
        >
          {LOOKUP_TEXT[lookup]}
        </p>
        <TextField
          {...field("street")}
          label="Rua"
          autoComplete="address-line1"
          onChange={(e) => onChange("street", e.target.value)}
          className="col-span-6 sm:col-span-4"
        />
        <TextField
          {...field("number")}
          label="Número"
          hint="Sem número: S/N"
          autoComplete="off"
          onChange={(e) => onChange("number", e.target.value)}
          className="col-span-2"
        />
        <TextField
          {...field("complement")}
          label="Complemento (opcional)"
          autoComplete="address-line2"
          onChange={(e) => onChange("complement", e.target.value)}
          className="col-span-4 sm:col-span-3"
        />
        <TextField
          {...field("district")}
          label="Bairro"
          autoComplete="address-level3"
          onChange={(e) => onChange("district", e.target.value)}
          className="col-span-6 sm:col-span-3"
        />
        <TextField
          {...field("city")}
          label="Cidade"
          autoComplete="address-level2"
          onChange={(e) => onChange("city", e.target.value)}
          className="col-span-4"
        />
        <div className="col-span-2 space-y-1.5">
          <label htmlFor="entrega-state" className="block font-medium">
            Estado
          </label>
          <select
            id="entrega-state"
            name="state"
            ref={(el) => register("state", el)}
            autoComplete="address-level1"
            value={delivery.state}
            onChange={(e) => onChange("state", e.target.value)}
            aria-invalid={errors.state ? true : undefined}
            aria-describedby={errors.state ? "entrega-state-erro" : undefined}
            className="border-field-line bg-surface text-ink aria-invalid:border-danger h-11 w-full cursor-pointer rounded-md border px-3 text-base outline-none focus-visible:outline-2"
          >
            <option value="">UF</option>
            {STATES.map((uf) => (
              <option key={uf} value={uf}>
                {uf}
              </option>
            ))}
          </select>
          {errors.state && (
            <p id="entrega-state-erro" className="text-danger text-sm font-medium">
              {errors.state}
            </p>
          )}
        </div>
      </div>
    </section>
  );
}
