using CompraFacil.BuildingBlocks.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CompraFacil.BuildingBlocks.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Traces, métricas e logs OpenTelemetry: HTTP de entrada e saída (inclui o gRPC para o catalog),
    /// Postgres (Npgsql), Kafka (publicação e consumo, com o traceparent atravessando o broker), runtime
    /// e as métricas CompraFacil.*. Exporta por OTLP quando OTEL_EXPORTER_OTLP_ENDPOINT está definido
    /// (no compose, o Collector); sem ele, só instrumenta.
    /// </summary>
    public static IHostApplicationBuilder AddObservability(this IHostApplicationBuilder builder, string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceNamespace: "compra-facil"))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx =>
                    // health checks a cada poucos segundos só fariam ruído
                    ctx.Request.Path != "/health" && ctx.Request.Path != "/ready")
                .AddHttpClientInstrumentation()
                .AddNpgsql()
                .AddSource(MessagingTelemetry.SourceName))
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter("Npgsql")
                // retries, timeouts e circuit breaker (pipelines do Polly.Extensions)
                .AddMeter("Polly")
                // métricas próprias: mensageria (BuildingBlocks) e de negócio de cada serviço
                .AddMeter("CompraFacil.*"))
            // logs também vão por OTLP (Loki), com trace_id/span_id em cada registro
            .WithLogging(logging => { }, options =>
            {
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
            });

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }
        return builder;
    }
}
