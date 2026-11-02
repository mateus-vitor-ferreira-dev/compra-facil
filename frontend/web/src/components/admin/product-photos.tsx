"use client";

import { ArrowDown, ArrowUp, Plus, Trash2 } from "lucide-react";
import { useEffect, useRef, useState } from "react";

/** A primeira foto mais as nove da galeria (more_image_urls no contrato). */
const MAX_PHOTOS = 10;

const action =
  "text-ink-soft hover:text-ink aria-disabled:text-ink/25 aria-disabled:hover:text-ink/25 grid size-11 shrink-0 cursor-pointer place-items-center rounded-md aria-disabled:cursor-default";

/**
 * As fotos do produto, por URL: acrescentar, reordenar e remover (#207). A primeira é a dos cards da vitrine.
 * Cada linha mantém a chave ao mudar de lugar, e o botão indisponível continua focável (aria-disabled): quem
 * reordena pelo teclado não perde o foco. Remover leva o foco à foto que ficou no lugar, e adicionar, ao campo
 * novo; a miniatura ao lado de cada endereço mostra se a URL é a foto certa (#273).
 */
export function ProductPhotos({ initial, error }: { initial: string[]; error?: string }) {
  const nextId = useRef(initial.length + 1);
  // a linha que recebe o foco depois de remover ou adicionar (a que tinha o foco pode ter saído da tela)
  const focusRow = useRef<number | null>(null);
  const list = useRef<HTMLOListElement>(null);
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
    const rest = rows.filter((_, i) => i !== index);
    const next = rest.length > 0 ? rest : [{ id: nextId.current++, url: "" }];
    focusRow.current = next[Math.min(index, next.length - 1)]?.id ?? null;
    setRows(next);
  };
  const add = () => {
    const id = nextId.current++;
    focusRow.current = id;
    setRows([...rows, { id, url: "" }]);
  };
  useEffect(() => {
    if (focusRow.current === null) return;
    list.current?.querySelector<HTMLInputElement>(`input[data-row="${focusRow.current}"]`)?.focus();
    focusRow.current = null;
  }, [rows]);

  return (
    <fieldset className="space-y-2" aria-describedby={error ? "fotos-dica fotos-erro" : "fotos-dica"}>
      <legend className="font-medium">Fotos</legend>
      <p id="fotos-dica" className="text-ink-soft text-sm">
        Endereço https de cada foto. A primeira aparece nos cards da vitrine; sem nenhuma, o produto usa o
        ícone da categoria.
      </p>
      <ol ref={list} className="space-y-2">
        {rows.map((row, i) => (
          <li key={row.id} className="flex items-center gap-1">
            <span aria-hidden="true" className="bg-photo mr-1 size-11 shrink-0 overflow-hidden rounded-md">
              {/^https:\/\/\S+$/.test(row.url) && (
                // a prévia do endereço digitado, de qualquer domínio: some se não for uma imagem
                // eslint-disable-next-line @next/next/no-img-element -- URL livre do operador, fora do next/image
                <img
                  key={row.url}
                  src={row.url}
                  alt=""
                  width={44}
                  height={44}
                  loading="lazy"
                  onError={(e) => (e.currentTarget.style.visibility = "hidden")}
                  className="size-full object-contain"
                />
              )}
            </span>
            <input
              data-row={row.id}
              type="url"
              name="image_urls"
              aria-label={`Endereço da foto ${i + 1}`}
              aria-invalid={error ? true : undefined}
              aria-describedby={error ? "fotos-erro" : undefined}
              value={row.url}
              onChange={(e) =>
                setRows(rows.map((r) => (r.id === row.id ? { ...r, url: e.target.value } : r)))
              }
              className="bg-surface border-field-line aria-invalid:border-danger mr-1 h-11 w-full min-w-0 rounded-md border px-3 outline-none focus-visible:outline-2"
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
          onClick={add}
          className="inline-flex min-h-11 cursor-pointer items-center gap-1.5 font-semibold underline underline-offset-4"
        >
          <Plus aria-hidden="true" className="size-4" />
          Adicionar foto
        </button>
      )}
    </fieldset>
  );
}
