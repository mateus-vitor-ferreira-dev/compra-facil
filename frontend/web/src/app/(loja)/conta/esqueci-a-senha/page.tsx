import type { Metadata } from "next";
import { AuthShell } from "@/components/account/auth-shell";
import { ForgotForm } from "./forgot-form";

export const metadata: Metadata = { title: "Esqueci minha senha", robots: { index: false } };

/** Pede o link para criar uma senha nova (vai por e-mail, vale 1 hora). */
export default function ForgotPasswordPage() {
  return (
    <AuthShell
      title="Esqueci minha senha"
      intro="Escreva o e-mail da sua conta. Mandamos um link para você criar uma senha nova."
    >
      <ForgotForm />
    </AuthShell>
  );
}
