import {
  describeRemaining,
  featuredCampaign,
  formatEnd,
  readFilters,
  remaining,
  withFilter,
  withoutRefinements,
  withoutValue,
  activeFilterCount,
} from "./storefront";

const now = Date.parse("2026-10-26T12:00:00Z");

describe("featuredCampaign", () => {
  it("escolhe a de maior desconto e, no empate, a que acaba antes", () => {
    const campaigns = [
      { name: "a", discount_percent: 15, ends_at: "2026-10-28T00:00:00Z" },
      { name: "b", discount_percent: 30, ends_at: "2026-10-31T00:00:00Z" },
      { name: "c", discount_percent: 30, ends_at: "2026-10-29T00:00:00Z" },
    ];
    expect(featuredCampaign(campaigns, now)?.name).toBe("c");
  });

  it("ignora a que já acabou ou ainda não começou", () => {
    const campaigns = [
      { name: "acabou", discount_percent: 50, ends_at: "2026-10-26T11:00:00Z" },
      {
        name: "futura",
        discount_percent: 40,
        starts_at: "2026-10-27T00:00:00Z",
        ends_at: "2026-11-01T00:00:00Z",
      },
      { name: "vigente", discount_percent: 10, ends_at: "2026-11-01T00:00:00Z" },
    ];
    expect(featuredCampaign(campaigns, now)?.name).toBe("vigente");
    expect(featuredCampaign([], now)).toBeUndefined();
  });
});

describe("remaining", () => {
  it("quebra o tempo em dias, horas, minutos e segundos", () => {
    expect(remaining("2026-10-29T16:12:05Z", now)).toEqual({ days: 3, hours: 4, minutes: 12, seconds: 5 });
  });

  it("é nulo quando já passou", () => {
    expect(remaining("2026-10-26T12:00:00Z", now)).toBeNull();
  });

  it("vira frase para o leitor de tela, no singular quando é 1", () => {
    expect(describeRemaining({ days: 3, hours: 1, minutes: 12, seconds: 5 })).toBe(
      "3 dias, 1 hora e 12 minutos",
    );
    expect(describeRemaining({ days: 0, hours: 0, minutes: 1, seconds: 30 })).toBe("1 minuto");
  });
});

it("formatEnd mostra o término no horário de Brasília", () => {
  expect(formatEnd("2026-10-30T13:00:00Z")).toBe("30 de outubro às 10:00");
});

describe("filtros da URL", () => {
  it("lê q, categoria, promoção e a página", () => {
    expect(readFilters({ q: "  tv ", categoria: ["fones", "x"], promocao: "1" })).toMatchObject({
      q: "tv",
      category: "fones",
      onSale: true,
      page: 1,
    });
    expect(readFilters({ pagina: "3" }).page).toBe(3);
    expect(readFilters({ pagina: "0" }).page).toBe(1);
    expect(readFilters({ pagina: "abc" }).page).toBe(1);
    expect(readFilters({ pagina: "999" }).page).toBe(200);
  });

  it("lê marcas, preço em reais e a ordem; relevância só na busca", () => {
    const f = readFilters({
      marca: ["Dell", "Acer", "Dell"],
      "preco-min": "1.500",
      "preco-max": "3000,50",
      ordem: "menor-preco",
    });
    expect(f).toMatchObject({
      brands: ["Dell", "Acer"],
      minPriceCents: 150000,
      maxPriceCents: 300050,
      sortChosen: true,
    });
    expect(f.sort.api).toBe("price_asc");
    expect(readFilters({ "preco-min": "abc", ordem: "relevancia" })).toMatchObject({
      minPriceCents: undefined,
      sortChosen: false,
    });
    expect(readFilters({}).sort.value).toBe("novidades");
    expect(readFilters({ q: "tv" }).sort.value).toBe("relevancia");
  });

  it("lê os tipos da faceta (#212), sem repetir, e eles contam como filtro", () => {
    const f = readFilters({ tipo: ["mouses", "teclados", "mouses"], marca: "Logitech" });
    expect(f.types).toEqual(["mouses", "teclados"]);
    expect(activeFilterCount(f)).toBe(3);
    expect(withoutRefinements("/categoria/perifericos", { tipo: ["mouses"], ordem: "menor-preco" })).toBe(
      "/categoria/perifericos?ordem=menor-preco",
    );
    expect(withoutValue("/categoria/perifericos", { tipo: ["mouses", "teclados"] }, "tipo", "mouses")).toBe(
      "/categoria/perifericos?tipo=teclados",
    );
  });

  it("conta os filtros ligados (a ordem não conta; a promoção fixa das ofertas também não)", () => {
    const f = readFilters({
      marca: ["Dell", "Acer"],
      "preco-max": "3000",
      promocao: "1",
      ordem: "maior-preco",
    });
    expect(activeFilterCount(f)).toBe(4);
    expect(activeFilterCount(f, { onSaleFixed: true })).toBe(3);
  });

  it("tira uma marca só, ou todos os refinamentos mantendo busca e ordem", () => {
    const params = {
      q: "notebook",
      marca: ["Dell", "Acer"],
      "preco-max": "3000",
      ordem: "menor-preco",
      cursor: "x",
    };
    expect(withoutValue("/busca", params, "marca", "Dell")).toBe(
      "/busca?q=notebook&marca=Acer&preco-max=3000&ordem=menor-preco",
    );
    expect(withoutRefinements("/busca", params)).toBe("/busca?q=notebook&ordem=menor-preco");
  });

  it("trocar um filtro mantém os outros e volta à primeira página", () => {
    const params = { q: "tv", cursor: "abc", promocao: "1" };
    expect(withFilter("/busca", params, "promocao", null)).toBe("/busca?q=tv");
    expect(withFilter("/busca", { ...params, pagina: "4" }, "pagina", "5")).toBe(
      "/busca?q=tv&promocao=1&pagina=5",
    );
    // trocar um filtro volta à primeira página
    expect(withFilter("/busca", { ...params, pagina: "4" }, "promocao", null)).toBe("/busca?q=tv");
    expect(withFilter("/ofertas", {}, "categoria", null)).toBe("/ofertas");
  });
});
