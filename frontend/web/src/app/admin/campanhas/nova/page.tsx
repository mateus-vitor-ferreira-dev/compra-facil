import type { Metadata } from "next";
import { CampaignForm } from "@/components/admin/campaign-form";
import { Breadcrumbs } from "@/components/store/breadcrumbs";

export const metadata: Metadata = { title: "Nova campanha" };

export default function NewCampaign() {
  return (
    <div className="max-w-xl space-y-5">
      <Breadcrumbs items={[{ href: "/admin/campanhas", label: "Campanhas" }, { label: "Nova campanha" }]} />
      <h1 className="text-2xl font-semibold tracking-tight">Nova campanha</h1>
      <p className="text-ink-soft">Depois de criar, você escolhe os produtos que entram nela.</p>
      <CampaignForm initial={{ name: "", slug: "", discount: "", starts: "", ends: "" }} />
    </div>
  );
}
