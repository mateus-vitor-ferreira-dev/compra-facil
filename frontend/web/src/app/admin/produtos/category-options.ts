import "server-only";
import { listCategories } from "@/lib/admin-api";

/** As categorias para o formulário: as raízes e, dentro de cada uma, as subcategorias e os tipos delas (#210). */
export async function categoryOptions() {
  const result = await listCategories();
  if (!result.ok) return [];
  const all = result.data.data;
  return all
    .filter((c) => !c.parent_id)
    .map((root) => ({
      id: root.id,
      name: root.name,
      children: all
        .filter((c) => c.parent_id === root.id)
        .flatMap((sub) => [
          { id: sub.id, name: sub.name },
          ...all
            .filter((c) => c.parent_id === sub.id)
            .map((t) => ({ id: t.id, name: `${sub.name} › ${t.name}` })),
        ]),
    }));
}
