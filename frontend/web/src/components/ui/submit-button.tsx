"use client";

import { useFormStatus } from "react-dom";
import { Button, type ButtonProps } from "./button";

/** Botão de envio que se desliga enquanto o formulário dele está indo (evita clique duplo). */
export function SubmitButton({
  pendingLabel,
  children,
  disabled,
  ...props
}: ButtonProps & { pendingLabel?: string }) {
  const { pending } = useFormStatus();
  return (
    <Button type="submit" disabled={pending || disabled} aria-disabled={pending || undefined} {...props}>
      {pending && pendingLabel ? pendingLabel : children}
    </Button>
  );
}
