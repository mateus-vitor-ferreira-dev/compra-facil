import { categoryPath } from "./category-path";

const all = [
  { id: "inf", parent_id: null, slug: "informatica" },
  { id: "per", parent_id: "inf", slug: "perifericos" },
  { id: "mou", parent_id: "per", slug: "mouses" },
  { id: "tv", parent_id: null, slug: "eletronicos" },
];

describe("categoryPath", () => {
  it("vai do departamento ao tipo", () => {
    expect(categoryPath(all, "mou").map((c) => c.slug)).toEqual(["informatica", "perifericos", "mouses"]);
  });

  it("um departamento é só ele", () => {
    expect(categoryPath(all, "tv").map((c) => c.slug)).toEqual(["eletronicos"]);
  });

  it("categoria que não existe: vazio", () => {
    expect(categoryPath(all, "x")).toEqual([]);
  });

  it("um ciclo nos dados não trava", () => {
    const loop = [
      { id: "a", parent_id: "b" },
      { id: "b", parent_id: "a" },
    ];
    expect(categoryPath(loop, "a").length).toBeLessThanOrEqual(8);
  });
});
