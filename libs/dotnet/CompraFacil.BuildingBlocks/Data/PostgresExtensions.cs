using CompraFacil.BuildingBlocks.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CompraFacil.BuildingBlocks.Data;

public static partial class PostgresExtensions
{
    /// <summary>
    /// Registra o DbContext do serviço no Postgres dele (ConnectionStrings:Default), com tabelas e
    /// colunas em snake_case, e o check de conexão no /ready.
    /// </summary>
    public static IHostApplicationBuilder AddPostgres<TContext>(this IHostApplicationBuilder builder)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        var connectionString = builder.Configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada");

        builder.Services.AddDbContext<TContext>(o => o
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());
        builder.Services.AddHealthChecks().AddDbContextCheck<TContext>("postgres", tags: [ServiceDefaults.ReadyTag]);
        return builder;
    }

    /// <summary>
    /// Aplica as migrations pendentes na subida quando Database:MigrateOnStart é true (padrão em dev
    /// e no compose, como o catalog). Em produção a migração pode virar um passo separado do deploy.
    /// </summary>
    public static async Task MigrateDatabaseAsync<TContext>(this IHost host)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(host);
        var config = host.Services.GetRequiredService<IConfiguration>();
        if (!config.GetValue("Database:MigrateOnStart", defaultValue: true))
        {
            return;
        }

        await using var scope = host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();
        await db.Database.MigrateAsync();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(PostgresExtensions));
        LogMigrated(logger, typeof(TContext).Name);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "migrações aplicadas ({Context})")]
    private static partial void LogMigrated(ILogger logger, string context);
}
