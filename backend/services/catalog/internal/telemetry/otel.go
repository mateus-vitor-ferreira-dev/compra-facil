package telemetry

import (
	"context"
	"errors"
	"fmt"
	"os"
	"time"

	"go.opentelemetry.io/contrib/bridges/otelslog"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/exporters/otlp/otlplog/otlploggrpc"
	"go.opentelemetry.io/otel/exporters/otlp/otlpmetric/otlpmetricgrpc"
	"go.opentelemetry.io/otel/exporters/otlp/otlptrace/otlptracegrpc"
	"go.opentelemetry.io/otel/propagation"
	sdklog "go.opentelemetry.io/otel/sdk/log"
	sdkmetric "go.opentelemetry.io/otel/sdk/metric"
	"go.opentelemetry.io/otel/sdk/resource"
	sdktrace "go.opentelemetry.io/otel/sdk/trace"
	semconv "go.opentelemetry.io/otel/semconv/v1.43.0" // a mesma do resource.Default do SDK
)

// Shutdown descarrega e encerra os exporters (chamar no desligamento).
type Shutdown func(context.Context) error

// Enabled diz se há para onde exportar (OTEL_EXPORTER_OTLP_ENDPOINT, como nos serviços .NET).
func Enabled() bool { return os.Getenv("OTEL_EXPORTER_OTLP_ENDPOINT") != "" }

// Setup liga traces, métricas e logs OpenTelemetry com exportação OTLP (gRPC) para o Collector.
// O propagador W3C (traceparent) é registrado sempre: os eventos Kafka levam o trace mesmo sem
// exportação. Sem OTEL_EXPORTER_OTLP_ENDPOINT, os providers ficam noop e nada sai do processo.
func Setup(ctx context.Context, service, version, env string) (Shutdown, error) {
	otel.SetTextMapPropagator(propagation.NewCompositeTextMapPropagator(propagation.TraceContext{}, propagation.Baggage{}))
	if !Enabled() {
		return func(context.Context) error { return nil }, nil
	}

	res, err := resource.Merge(resource.Default(), resource.NewWithAttributes(semconv.SchemaURL,
		semconv.ServiceName(service),
		semconv.ServiceNamespace("compra-facil"),
		semconv.ServiceVersion(version),
		semconv.DeploymentEnvironmentNameKey.String(env),
	))
	if err != nil {
		return nil, fmt.Errorf("resource: %w", err)
	}

	// os exporters leem endpoint e TLS das variáveis OTEL_EXPORTER_OTLP_* (http:// = sem TLS)
	traceExp, err := otlptracegrpc.New(ctx)
	if err != nil {
		return nil, fmt.Errorf("exporter de traces: %w", err)
	}
	metricExp, err := otlpmetricgrpc.New(ctx)
	if err != nil {
		return nil, fmt.Errorf("exporter de métricas: %w", err)
	}
	logExp, err := otlploggrpc.New(ctx)
	if err != nil {
		return nil, fmt.Errorf("exporter de logs: %w", err)
	}

	tp := sdktrace.NewTracerProvider(sdktrace.WithResource(res), sdktrace.WithBatcher(traceExp))
	mp := sdkmetric.NewMeterProvider(sdkmetric.WithResource(res),
		sdkmetric.WithReader(sdkmetric.NewPeriodicReader(metricExp, sdkmetric.WithInterval(15*time.Second))))
	lp := sdklog.NewLoggerProvider(sdklog.WithResource(res), sdklog.WithProcessor(sdklog.NewBatchProcessor(logExp)))
	otel.SetTracerProvider(tp)
	otel.SetMeterProvider(mp)
	otel.SetLoggerProvider(lp)

	return func(ctx context.Context) error {
		return errors.Join(tp.Shutdown(ctx), mp.Shutdown(ctx), lp.Shutdown(ctx))
	}, nil
}

// otelHandler manda os logs também por OTLP (Loki), com trace_id/span_id do contexto.
func otelHandler(service string) *otelslog.Handler {
	return otelslog.NewHandler(service, otelslog.WithLoggerProvider(otel.GetLoggerProvider()))
}
