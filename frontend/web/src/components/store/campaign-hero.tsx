import Link from "next/link";
import { buttonVariants } from "@/components/ui/button";
import type { Campaign } from "@/lib/catalog-api";
import { cn } from "@/lib/utils";
import { Countdown } from "./countdown";

const count = new Intl.NumberFormat("pt-BR");

/**
 * O encarte da campanha vigente: o bloco em tinta, o desconto numa etiqueta grande (o mesmo desenho
 * do preço) e a contagem regressiva. É o único momento barulhento da home.
 */
export function CampaignHero({ campaign, others }: { campaign: Campaign; others: Campaign[] }) {
  return (
    <section aria-labelledby="campanha" className="bg-hero text-on-hero">
      <div className="mx-auto grid max-w-6xl gap-3 px-4 py-5 sm:px-6 md:grid-cols-[1fr_auto] md:items-center md:gap-8 md:py-14">
        <div className="space-y-3 md:space-y-5">
          <h2 id="campanha" className="font-condensed text-4xl leading-none font-extrabold sm:text-[4rem]">
            {campaign.name}
          </h2>
          <p className="max-w-md md:text-lg">
            Até {campaign.discount_percent}% de desconto
            {campaign.product_count ? ` em ${count.format(campaign.product_count)} produtos` : ""}, com o
            preço já baixado na etiqueta.
          </p>
          <Countdown endsAt={campaign.ends_at} className="text-on-hero/85" />
          <Link
            href="/ofertas"
            className={cn(
              buttonVariants(),
              "bg-on-hero text-hero hover:bg-on-hero/85 focus-visible:outline-on-hero",
            )}
          >
            Ver as ofertas
          </Link>
        </div>

        {/* a etiqueta do encarte: decorativa, o desconto já está no texto */}
        <div aria-hidden="true" className="order-first md:order-none">
          <div
            className="bg-tag text-tag-ink relative flex w-fit items-center py-1.5 pr-4 pl-9 md:py-6 md:pr-10 md:pl-16"
            style={{ clipPath: "polygon(1.25rem 0, 100% 0, 100% 100%, 0 100%, 0 1.25rem)" }}
          >
            <span className="bg-hero absolute top-1/2 left-4 size-3 -translate-y-1/2 rounded-full md:left-5 md:size-4" />
            <span className="font-condensed text-3xl leading-none font-extrabold md:text-[7rem]">
              -{campaign.discount_percent}%
            </span>
          </div>
        </div>

        {others.length > 0 && (
          // sem link e abaixo do botão: no celular, cederia a primeira tela aos produtos
          <div className="border-on-hero/20 hidden border-t pt-5 md:col-span-2 md:block">
            <h3 className="text-on-hero/85 text-sm">Também acontecendo</h3>
            <ul className="mt-2 flex flex-wrap gap-x-6 gap-y-2">
              {others.map((c) => (
                <li key={c.id}>
                  <span className="font-semibold">{c.name}</span>{" "}
                  <span className="text-on-hero/85">até {c.discount_percent}% de desconto</span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </section>
  );
}
