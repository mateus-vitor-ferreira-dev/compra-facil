/**
 * OpenTelemetry no servidor do Next (#155): cada página, server action e rota do BFF vira um span, e
 * as chamadas ao gateway levam o traceparent, então o trace segue do Next ao gateway, aos serviços,
 * ao Kafka e ao e-mail. Exporta por OTLP quando OTEL_EXPORTER_OTLP_ENDPOINT está definido (o
 * Collector no compose). O contexto só vai para o gateway, nunca a terceiros.
 *
 * Sem o endpoint, nada carrega: no Cloudflare Workers (ADR 0006) o @vercel/otel não sobe, e a
 * telemetria do Worker vem da observabilidade da própria Cloudflare (wrangler.jsonc).
 */
export async function register() {
  if (!process.env.OTEL_EXPORTER_OTLP_ENDPOINT) return;
  const { registerOTel } = await import("@vercel/otel");
  const gateway = `${(process.env.API_URL ?? "http://localhost").replace(/\/$/, "")}/`;

  // métricas (os Web Vitals que o navegador manda) só no runtime Node: o Edge não guarda estado
  const metricReaders =
    process.env.NEXT_RUNTIME === "nodejs"
      ? [
          new (await import("@opentelemetry/sdk-metrics")).PeriodicExportingMetricReader({
            exporter: new (await import("@opentelemetry/exporter-metrics-otlp-http")).OTLPMetricExporter(),
            exportIntervalMillis: 15_000,
          }),
        ]
      : [];

  registerOTel({
    serviceName: process.env.OTEL_SERVICE_NAME ?? "web",
    instrumentationConfig: { fetch: { propagateContextUrls: [gateway] } },
    metricReaders,
  });
}
