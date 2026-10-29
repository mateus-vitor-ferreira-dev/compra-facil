/**
 * Quem recebe e onde (#221): o que a nota fiscal e o fornecedor exigem. As mesmas regras no navegador
 * (para conferir antes de mandar o cartão ao provedor) e na server action (que vale sem JavaScript).
 */

export const STATES = [
  "AC", "AL", "AP", "AM", "BA", "CE", "DF", "ES", "GO", "MA", "MT", "MS", "MG", "PA",
  "PB", "PR", "PE", "PI", "RJ", "RN", "RS", "RO", "RR", "SC", "SP", "SE", "TO",
] as const; // prettier-ignore

export type DeliveryInput = {
  name: string;
  cpf: string;
  phone: string;
  zip_code: string;
  street: string;
  number: string;
  complement: string;
  district: string;
  city: string;
  state: string;
};
export type DeliveryField = keyof DeliveryInput;
/** O que a busca do CEP devolve (/api/cep/{cep}). */
export type CepAddress = { street: string; district: string; city: string; state: string };
export type DeliveryErrors = Partial<Record<DeliveryField, string>>;

export const DELIVERY_FIELDS: DeliveryField[] = [
  "name",
  "cpf",
  "phone",
  "zip_code",
  "street",
  "number",
  "complement",
  "district",
  "city",
  "state",
];

export const EMPTY_DELIVERY: DeliveryInput = {
  name: "",
  cpf: "",
  phone: "",
  zip_code: "",
  street: "",
  number: "",
  complement: "",
  district: "",
  city: "",
  state: "",
};

const digits = (value: string) => value.replace(/\D/g, "");
const clean = (value: string) => value.trim().replace(/\s+/g, " ");

/** CPF com os dois dígitos verificadores certos (e não todos iguais, como 111.111.111-11). */
export function isValidCpf(value: string): boolean {
  const d = digits(value);
  if (d.length !== 11 || /^(\d)\1+$/.test(d)) return false;
  const check = (length: number) => {
    let sum = 0;
    for (let i = 0; i < length; i++) sum += Number(d[i]) * (length + 1 - i);
    const rest = (sum * 10) % 11;
    return rest === 10 ? 0 : rest;
  };
  return check(9) === Number(d[9]) && check(10) === Number(d[10]);
}

/** "529.982.247-25" enquanto se digita. */
export function formatCpf(value: string): string {
  const d = digits(value).slice(0, 11);
  return d
    .replace(/^(\d{3})(\d)/, "$1.$2")
    .replace(/^(\d{3})\.(\d{3})(\d)/, "$1.$2.$3")
    .replace(/\.(\d{3})(\d{1,2})$/, ".$1-$2");
}

/** "01310-100". */
export function formatZipCode(value: string): string {
  const d = digits(value).slice(0, 8);
  return d.length > 5 ? `${d.slice(0, 5)}-${d.slice(5)}` : d;
}

/** "(11) 98765-4321", ou "(11) 3456-7890" no fixo. */
export function formatPhone(value: string): string {
  const d = digits(value).slice(0, 11);
  if (d.length <= 2) return d.length ? `(${d}` : "";
  const rest = d.slice(2);
  const split = d.length === 11 ? 5 : 4;
  return rest.length > split
    ? `(${d.slice(0, 2)}) ${rest.slice(0, split)}-${rest.slice(split)}`
    : `(${d.slice(0, 2)}) ${rest}`;
}

const MAX = 60; // o tamanho dos campos de nome e endereço na NF-e

/** Confere os dados de entrega: cada erro diz o que corrigir, no campo dele. */
export function checkDelivery(input: DeliveryInput): DeliveryErrors {
  const errors: DeliveryErrors = {};
  const name = clean(input.name);
  if (name.length < 3 || !name.includes(" ")) errors.name = "Digite o nome e o sobrenome de quem recebe.";
  else if (name.length > MAX) errors.name = `Use até ${MAX} caracteres.`;
  if (!isValidCpf(input.cpf))
    errors.cpf =
      digits(input.cpf).length === 11
        ? "Este CPF não existe: confira os dígitos."
        : "Digite os 11 dígitos do CPF.";
  if (!/^\d{10,11}$/.test(digits(input.phone))) errors.phone = "Digite o telefone com DDD.";
  if (digits(input.zip_code).length !== 8) errors.zip_code = "Digite os 8 dígitos do CEP.";
  for (const [field, label] of [
    ["street", "a rua"],
    ["number", "o número"],
    ["district", "o bairro"],
    ["city", "a cidade"],
  ] as const) {
    const value = clean(input[field]);
    if (!value) errors[field] = `Digite ${label}.`;
    else if (value.length > MAX) errors[field] = `Use até ${MAX} caracteres.`;
  }
  if (clean(input.complement).length > MAX) errors.complement = `Use até ${MAX} caracteres.`;
  if (!(STATES as readonly string[]).includes(input.state.toUpperCase())) errors.state = "Escolha o estado.";
  return errors;
}

/** O corpo do pedido: só os dígitos no CPF, no telefone e no CEP; espaços repetidos fora. */
export function toOrderDelivery(input: DeliveryInput) {
  return {
    recipient: { name: clean(input.name), cpf: digits(input.cpf), phone: digits(input.phone) },
    shipping_address: {
      zip_code: digits(input.zip_code),
      street: clean(input.street),
      number: clean(input.number),
      complement: clean(input.complement) || null,
      district: clean(input.district),
      city: clean(input.city),
      state: input.state.toUpperCase(),
    },
  };
}

/** O formulário a partir do pedido anterior (o que o cliente usou da última vez), já com as máscaras. */
export function fromOrder(order: {
  recipient?: { name: string; cpf: string; phone: string } | null;
  shipping_address?: {
    zip_code: string;
    street: string;
    number: string;
    complement?: string | null;
    district: string;
    city: string;
    state: string;
  } | null;
}): DeliveryInput | undefined {
  const r = order.recipient;
  const a = order.shipping_address;
  if (!r || !a) return undefined;
  return {
    name: r.name,
    cpf: formatCpf(r.cpf),
    phone: formatPhone(r.phone),
    zip_code: formatZipCode(a.zip_code),
    street: a.street,
    number: a.number,
    complement: a.complement ?? "",
    district: a.district,
    city: a.city,
    state: a.state,
  };
}

/** O endereço em três linhas: rua e número, bairro e cidade, CEP. */
export function addressLines(a: {
  zip_code: string;
  street: string;
  number: string;
  complement?: string | null;
  district: string;
  city: string;
  state: string;
}): string[] {
  return [
    [a.street, a.number, a.complement].filter(Boolean).join(", "),
    `${a.district}, ${a.city} - ${a.state}`,
    `CEP ${formatZipCode(a.zip_code)}`,
  ];
}
