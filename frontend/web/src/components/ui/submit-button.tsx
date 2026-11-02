"use client";

import { useFormStatus } from "react-dom";
import { cn } from "@/lib/utils";
import { Button, type ButtonProps } from "./button";

/**
 * Botão de envio que barra o segundo envio enquanto o formulário dele está indo (evita clique duplo).
 * Durante o envio fica `aria-disabled`, e não `disabled`: o botão desligado perderia o foco, e quem usa teclado
 * ou leitor de tela cairia no começo da página quando o erro voltasse (#273). O clique (e o Enter num campo,
 * que o navegador transforma em clique no botão) é barrado à mão.
 */
export function SubmitButton({
  pendingLabel,
  children,
  disabled,
  className,
  onClick,
  ...props
}: ButtonProps & { pendingLabel?: string }) {
  const { pending } = useFormStatus();
  return (
    <Button
      type="submit"
      disabled={disabled}
      aria-disabled={pending || undefined}
      className={cn(pending && "cursor-progress opacity-50", className)}
      onClick={(event) => {
        if (pending) {
          event.preventDefault();
          return;
        }
        onClick?.(event);
      }}
      {...props}
    >
      {pending && pendingLabel ? pendingLabel : children}
    </Button>
  );
}
