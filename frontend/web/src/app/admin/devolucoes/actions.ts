"use server";

import { redirect } from "next/navigation";
import { z } from "zod";
import type { FormState } from "@/app/admin/produtos/actions";
import { instructReturnCustomer } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";

const PAGE = "/admin/devolucoes";

/**
 * Depois da recusa do fornecedor na abertura, registra o que a equipe combinou com o cliente (#281): ele fica com
 * o produto, ou manda para a loja com o código e o endereço daqui. O cliente recebe por e-mail.
 */
export async function instructCustomer(_: FormState, form: FormData): Promise<FormState> {
  const orderId = z.uuid().parse(form.get("order_id"));
  const { token } = await requireAdmin(PAGE);
  const values = {
    instruction: String(form.get("instruction") ?? ""),
    return_code: String(form.get("return_code") ?? "").trim(),
    return_address: String(form.get("return_address") ?? "").trim(),
  };
  const fields: Record<string, string> = {};
  if (values.instruction !== "keep" && values.instruction !== "send")
    fields.instruction = "Escolha o que o cliente faz com o produto.";
  if (values.instruction === "send") {
    if (!values.return_code) fields.return_code = "Escreva o código que o cliente põe no pacote.";
    else if (values.return_code.length > 64) fields.return_code = "Use até 64 caracteres.";
    if (!values.return_address) fields.return_address = "Escreva para onde o cliente manda o produto.";
    else if (values.return_address.length > 500) fields.return_address = "Use até 500 caracteres.";
  }
  if (Object.keys(fields).length > 0)
    return { values, fields, error: { message: "Confira os campos marcados." } };

  const result = await instructReturnCustomer(
    token,
    orderId,
    values.instruction === "send"
      ? { instruction: "send", return_code: values.return_code, return_address: values.return_address }
      : { instruction: "keep" },
  );
  if (!result.ok) {
    return {
      values,
      error: {
        message:
          result.error.status === 409
            ? "O cliente já recebeu outra instrução, ou a devolução não foi recusada na abertura. Recarregue a página."
            : `A instrução não foi registrada (${result.error.status}). Nada foi mandado ao cliente; tente de novo.`,
        traceId: result.error.traceId,
      },
    };
  }
  redirect(`${PAGE}?instruido=${orderId}`);
}
