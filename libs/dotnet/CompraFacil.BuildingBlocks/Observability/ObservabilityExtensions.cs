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
    /// Traces e métricas OpenTelemetry: HTTP de entrada e saída, Postgres (Npgsql), Kafka (publicação
    /// e consumo, com o traceparent atravessando o broker) e runtime. Exporta por OTLP quando
    /// OTEL_EXPORTER_OTLP_ENDPOINT está definido (Collector da #85); sem ele, só instrumenta.
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
                .AddMeter("Npgsql"));

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }
        return builder;
    }
}
