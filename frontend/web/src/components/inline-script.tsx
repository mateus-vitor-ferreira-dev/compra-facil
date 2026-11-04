"use client";

/**
 * Um script inline que roda só no HTML do servidor, antes da hidratação (o do tema). No cliente ele vira
 * text/plain: o React avisa de <script> renderizado, e lá ele não rodaria mesmo. O suppressHydrationWarning
 * aceita a diferença de tipo (guia "preventing flash before hydration" do Next).
 */
export function InlineScript({ html }: { html: string }) {
  return (
    <script
      type={typeof window === "undefined" ? "text/javascript" : "text/plain"}
      suppressHydrationWarning
      dangerouslySetInnerHTML={{ __html: html }}
    />
  );
}
