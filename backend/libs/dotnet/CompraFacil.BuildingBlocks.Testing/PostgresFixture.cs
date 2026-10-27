using Testcontainers.PostgreSql;
using Xunit;

namespace CompraFacil.BuildingBlocks.Testing;

/// <summary>Um Postgres real (mesma imagem do compose) por classe de teste.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string Image = "postgres:17.11-alpine";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
