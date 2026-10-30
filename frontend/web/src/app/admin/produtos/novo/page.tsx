import type { Metadata } from "next";
import { ProductForm } from "@/components/admin/product-form";
import { Breadcrumbs } from "@/components/store/breadcrumbs";
import { categoryOptions } from "../category-options";

export const metadata: Metadata = { title: "Novo produto" };

export default async function NewProduct() {
  const categories = await categoryOptions();
  return (
    <div className="max-w-3xl space-y-5">
      <Breadcrumbs items={[{ href: "/admin/produtos", label: "Produtos" }, { label: "Novo produto" }]} />
      <h1 className="text-2xl font-semibold tracking-tight">Novo produto</h1>
      <ProductForm
        categories={categories}
        initial={{
          name: "",
          sku: "",
          brand: "",
          description: "",
          category_id: "",
          image_urls: [],
          price: "",
          status: "draft",
        }}
      />
    </div>
  );
}
