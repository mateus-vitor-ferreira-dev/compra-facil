"use client";

import { Eye, EyeOff } from "lucide-react";
import { useId, useState, type ComponentProps } from "react";
import { cn } from "@/lib/utils";

type FieldProps = ComponentProps<"input"> & {
  label: string;
  /** a regra do campo, sempre visível (não some quando se digita, como o placeholder) */
  hint?: string;
  /** o erro deste campo, logo abaixo dele */
  error?: string;
};

const input =
  "bg-surface text-ink h-11 w-full rounded-md border px-3 text-base outline-none focus-visible:outline-2 focus-visible:outline-offset-0 aria-invalid:border-danger";

/**
 * Campo com rótulo em cima, dica e erro ligados ao input (aria-describedby / aria-invalid). Um
 * `aria-describedby` passado soma-se à dica e ao erro (a bandeira do cartão, ao lado do número).
 */
export function TextField({
  label,
  hint,
  error,
  className,
  id: fixedId,
  "aria-describedby": extra,
  ...props
}: FieldProps) {
  const generated = useId();
  const id = fixedId ?? generated;
  return (
    <div className={cn("space-y-1.5", className)}>
      <label htmlFor={id} className="block font-medium">
        {label}
      </label>
      <input
        id={id}
        aria-invalid={error ? true : undefined}
        aria-describedby={[describedBy(id, hint, error), extra].filter(Boolean).join(" ") || undefined}
        className={cn(input, "border-field-line")}
        {...props}
      />
      <FieldText id={id} hint={hint} error={error} />
    </div>
  );
}

/** Senha com "Mostrar": quem digita no celular confere o que digitou em vez de redigitar. */
export function PasswordField({ label, hint, error, className, id: fixedId, ...props }: FieldProps) {
  const generated = useId();
  const id = fixedId ?? generated;
  const [visible, setVisible] = useState(false);
  return (
    <div className={cn("space-y-1.5", className)}>
      <label htmlFor={id} className="block font-medium">
        {label}
      </label>
      <div className="relative">
        <input
          id={id}
          type={visible ? "text" : "password"}
          aria-invalid={error ? true : undefined}
          aria-describedby={describedBy(id, hint, error)}
          className={cn(input, "border-field-line pr-12")}
          {...props}
        />
        <button
          type="button"
          onClick={() => setVisible((v) => !v)}
          aria-pressed={visible}
          aria-controls={id}
          className="text-ink-soft hover:text-ink absolute top-0 right-0 grid size-11 cursor-pointer place-items-center rounded-md"
        >
          {visible ? (
            <EyeOff aria-hidden="true" className="size-5" />
          ) : (
            <Eye aria-hidden="true" className="size-5" />
          )}
          <span className="sr-only">Mostrar a senha</span>
        </button>
      </div>
      <FieldText id={id} hint={hint} error={error} />
    </div>
  );
}

function describedBy(id: string, hint?: string, error?: string) {
  return [hint && `${id}-dica`, error && `${id}-erro`].filter(Boolean).join(" ") || undefined;
}

function FieldText({ id, hint, error }: { id: string; hint?: string; error?: string }) {
  return (
    <>
      {hint && (
        <p id={`${id}-dica`} className="text-ink-soft text-sm">
          {hint}
        </p>
      )}
      {error && (
        <p id={`${id}-erro`} className="text-danger text-sm font-medium">
          {error}
        </p>
      )}
    </>
  );
}
