using Embrulhado.BuildingBlocks.Testing;

namespace Embrulhado.Orders.IntegrationTests;

/// <summary>Sobe o orders com um Postgres real: health, ready, docs e ProblemDetails.</summary>
public sealed class SmokeTests(PostgresFixture postgres) : ServiceSmokeTests<Program>(postgres)
{
    protected override string? ContractFile => "orders.v1.yaml";
}
