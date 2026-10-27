/** O erro geral de um formulário do painel, com o código para o suporte quando há. */
export function FormError({ error }: { error?: { message: string; traceId?: string } }) {
  if (!error) return null;
  return (
    <div role="alert" className="border-danger bg-surface space-y-1 rounded-md border-l-4 p-4">
      <p className="font-semibold">{error.message}</p>
      {error.traceId && (
        <p className="text-ink-soft text-sm">
          Código para o suporte:{" "}
          <code className="text-ink bg-ink/8 rounded-sm px-1 text-xs break-all select-all">
            {error.traceId}
          </code>
        </p>
      )}
    </div>
  );
}
