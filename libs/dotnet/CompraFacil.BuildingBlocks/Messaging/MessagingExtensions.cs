using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CompraFacil.BuildingBlocks.Messaging;

/// <summary>O que o serviço publica e consome.</summary>
public sealed class MessagingBuilder
{
    internal MessagingBuilder(MessagingOptions options, IServiceCollection services)
    {
        Options = options;
        Services = services;
    }

    internal MessagingOptions Options { get; }
    internal IServiceCollection Services { get; }

    /// <summary>Tópicos de que o serviço é dono (contrato AsyncAPI): o relay os cria.</summary>
    public MessagingBuilder Produces(params string[] topics)
    {
        foreach (var topic in topics)
        {
            Options.ProducedTopics.Add(topic);
        }
        return this;
    }

    /// <summary>Registra o handler de um event_type de um tópico de outro serviço.</summary>
    public MessagingBuilder Handle<TData, THandler>(string topic, string eventType)
        where THandler : class, IHandleEvent<TData>
    {
        Services.AddScoped<THandler>();
        Options.Handlers.Add(new HandlerRegistration(topic, eventType,
            static (sp, envelope, context, ct) =>
            {
                TData data;
                try
                {
                    data = envelope.DataAs<TData>();
                }
                catch (JsonException e)
                {
                    throw new PoisonMessageException($"data de {envelope.EventType} fora do contrato: {e.Message}", e);
                }
                return sp.GetRequiredService<THandler>().HandleAsync(data, context, ct);
            }));
        return this;
    }
}

public static class MessagingExtensions
{
    /// <summary>
    /// Outbox (IOutbox), relay para o Kafka e consumer com inbox, sobre o DbContext do serviço (que
    /// precisa chamar <c>AddMessagingTables()</c> no OnModelCreating). Configuração na seção Kafka
    /// (Kafka:BootstrapServers etc.). Kafka fora não afeta o /ready: os eventos esperam no outbox.
    /// </summary>
    public static IHostApplicationBuilder AddMessaging<TContext>(
        this IHostApplicationBuilder builder, string serviceName, Action<MessagingBuilder> configure)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MessagingOptions { ServiceName = serviceName };
        builder.Configuration.GetSection("Kafka").Bind(options);
        configure(new MessagingBuilder(options, builder.Services));

        builder.Services.AddSingleton(options);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddScoped<IOutbox, Outbox<TContext>>();
        if (options.Enabled)
        {
            if (options.ProducedTopics.Count > 0)
            {
                builder.Services.AddHostedService<OutboxRelay<TContext>>();
            }
            builder.Services.AddHostedService<EventConsumer<TContext>>();
        }
        return builder;
    }
}
