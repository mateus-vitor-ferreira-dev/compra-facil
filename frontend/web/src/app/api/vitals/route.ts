import { metrics } from "@opentelemetry/api";
import { z } from "zod";
import { routePattern } from "@/lib/route-pattern";

/**
 * Web Vitals do navegador (#155) como histogramas OpenTelemetry, que o Collector leva ao Prometheus e o
 * Grafana mostra no p75 (o percentil que o Google usa). Um por métrica, com a página como padrão de rota
 * e a nota (good, needs-improvement, poor). Sem estado nem dado pessoal: só o valor, a página e a nota.
 */
const meter = metrics.getMeter("compra-facil-web");

// fronteiras dos histogramas em torno dos limites do Google (bom/ruim)
const ms = (good: number, poor: number) => [
  good / 4,
  good / 2,
  good,
  (good + poor) / 2,
  poor,
  poor * 2,
  poor * 4,
];
const HISTOGRAMS = {
  LCP: meter.createHistogram("web_vitals_lcp", {
    unit: "ms",
    advice: { explicitBucketBoundaries: ms(2500, 4000) },
  }),
  INP: meter.createHistogram("web_vitals_inp", {
    unit: "ms",
    advice: { explicitBucketBoundaries: ms(200, 500) },
  }),
  FCP: meter.createHistogram("web_vitals_fcp", {
    unit: "ms",
    advice: { explicitBucketBoundaries: ms(1800, 3000) },
  }),
  TTFB: meter.createHistogram("web_vitals_ttfb", {
    unit: "ms",
    advice: { explicitBucketBoundaries: ms(800, 1800) },
  }),
  CLS: meter.createHistogram("web_vitals_cls", {
    unit: "1",
    advice: { explicitBucketBoundaries: [0.025, 0.05, 0.1, 0.175, 0.25, 0.5, 1] },
  }),
};

const schema = z.object({
  name: z.enum(["LCP", "INP", "FCP", "TTFB", "CLS"]),
  value: z.number().nonnegative().max(600_000),
  rating: z.enum(["good", "needs-improvement", "poor"]),
  path: z.string().max(512),
});

export async function POST(request: Request): Promise<Response> {
  // de outra origem, não: o beacon do navegador sempre vem da própria loja
  const origin = request.headers.get("origin");
  if (origin && origin !== new URL(request.url).origin) return new Response(null, { status: 403 });
  const raw = await request.text();
  if (raw.length > 1024) return new Response(null, { status: 413 });
  let body: unknown;
  try {
    body = JSON.parse(raw);
  } catch {
    return new Response(null, { status: 400 });
  }
  const parsed = schema.safeParse(body);
  if (!parsed.success) return new Response(null, { status: 400 });
  const { name, value, rating, path } = parsed.data;
  HISTOGRAMS[name].record(value, { "page.route": routePattern(path), "web_vitals.rating": rating });
  return new Response(null, { status: 204 });
}
