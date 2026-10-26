import type { components } from "@/lib/api/schema/catalog";

/** Tipos do catálogo usados nas telas (gerados de contracts/openapi/catalog.v1.yaml). */
export type Product = components["schemas"]["Product"];
export type ProductImage = components["schemas"]["ProductImage"];
export type Money = components["schemas"]["Money"];
