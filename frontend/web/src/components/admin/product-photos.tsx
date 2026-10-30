"use client";

import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import { useRef, useState } from "react";

/** A primeira foto mais as nove da galeria (more_image_urls no contrato). */
const MAX_PHOTOS = 10;

const action =
  "text-ink-soft hover:text-ink aria-disabled:text-ink/25 grid size-11 shrink-0 cursor-pointer place-items-center rounded-md aria-disabled:cursor-default";

/**
 * As fotos do produto, por URL: acrescentar, reordenar e remover (#207). A primeira é a dos cards da vitrine.
 * Cada linha mantém a chave ao mudar de lugar, e o botão indisponível continua focável (aria-disabled): quem
 * reordena pelo teclado não perde o foco.
 */
export function ProductPhotos({ initial, error }: { initial: string[]; error?: string }) {
  const nextId = useRef(initial.length + 1);
  const [rows, setRows] = useState(() =>
    (initial.length > 0 ? initial : [""]).map((url, id) => ({ id, url })),
  );

  const move = (index: number, to: number) => {
    if (to < 0 || to >= rows.length) return;
    const next = [...rows];
    next.splice(to, 0, ...next.splice(index, 1));
    setRows(next);
  };
  const remove = (index: number) => {
    const next = rows.filter((_, i) => i !== index);
    setRows(next.length > 0 ? next : [{ id: nextId.current++, url: "" }]);
  };

  return (
    <fieldset className="space-y-2" aria-describedby={error ? "fotos-erro" : "fotos-dica"}>
      <legend className="font-medium">Fotos</legend>
      <p id="fotos-dica" className="text-ink-soft text-sm">
        Endereço https de cada foto. A primeira aparece nos cards da vitrine; sem nenhuma, o produto usa o
        ícone da categoria.
      </p>
      <ol className="space-y-2">
        {rows.map((row, i) => (
          <li key={row.id} className="flex items-center gap-1">
            <input
              type="url"
              name="image_urls"
              aria-label={`Endereço da foto ${i + 1}`}
              aria-invalid={error ? true : undefined}
              value={row.url}
              onChange={(e) =>
                setRows(rows.map((r) => (r.id === row.id ? { ...r, url: e.target.value } : r)))
              }
              className="bg-surface border-ink/25 aria-invalid:border-danger mr-1 h-11 w-full min-w-0 rounded-md border px-3 outline-none focus-visible:outline-2"
            />
            <button
              type="button"
              aria-label={`Subir a foto ${i + 1}`}
              aria-disabled={i === 0}
              onClick={() => move(i, i - 1)}
              className={action}
            >
              <ArrowUp aria-hidden="true" className="size-4" />
            </button>
            <button
              type="button"
              aria-label={`Descer a foto ${i + 1}`}
              aria-disabled={i === rows.length - 1}
              onClick={() => move(i, i + 1)}
              className={action}
            >
              <ArrowDown aria-hidden="true" className="size-4" />
            </button>
            <button
              type="button"
              aria-label={`Remover a foto ${i + 1}`}
              onClick={() => remove(i)}
              className={action}
            >
              <Trash2 aria-hidden="true" className="size-4" />
            </button>
          </li>
        ))}
      </ol>
      {error && (
        <p id="fotos-erro" className="text-danger text-sm">
          {error}
        </p>
      )}
      {rows.length < MAX_PHOTOS && (
        <button
          type="button"
          onClick={() => setRows([...rows, { id: nextId.current++, url: "" }])}
          className="inline-flex min-h-11 cursor-pointer items-center gap-1.5 font-semibold underline underline-offset-4"
        >
          <Plus aria-hidden="true" className="size-4" />
          Adicionar foto
        </button>
      )}
    </fieldset>
  );
}
