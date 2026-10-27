import { registerOTel } from "@vercel/otel";

/**
 * OpenTelemetry no servidor do Next (#155): cada página, server action e rota do BFF vira um span, e
 * as chamadas ao gateway levam o traceparent, então o trace segue do Next ao gateway, aos serviços,
 * ao Kafka e ao e-mail. Exporta por OTLP quando OTEL_EXPORTER_OTLP_ENDPOINT está definido (o
 * Collector no compose; a integração na Vercel). O contexto só vai para o gateway, nunca a terceiros.
 */
export async function register() {
  const gateway = `${(process.env.API_URL ?? "http://localhost").replace(/\/$/, "")}/`;
  const exporting = Boolean(process.env.OTEL_EXPORTER_OTLP_ENDPOINT);

  // métricas (os Web Vitals que o navegador manda) só no runtime Node: o Edge não guarda estado
  const metricReaders =
    exporting && process.env.NEXT_RUNTIME === "nodejs"
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
