import "server-only";
import { listCategories } from "@/lib/admin-api";

/** As categorias para o formulário: as raízes e as filhas de cada uma. */
export async function categoryOptions() {
  const result = await listCategories();
  if (!result.ok) return [];
  const all = result.data.data;
  return all
    .filter((c) => !c.parent_id)
    .map((root) => ({
      id: root.id,
      name: root.name,
      children: all.filter((c) => c.parent_id === root.id).map(({ id, name }) => ({ id, name })),
    }));
}
