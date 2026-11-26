using Embrulhado.BuildingBlocks.Testing;

namespace Embrulhado.Fulfillment.IntegrationTests;

/// <summary>Sobe o fulfillment com um Postgres real: health, ready, docs e ProblemDetails.</summary>
public sealed class SmokeTests(PostgresFixture postgres) : ServiceSmokeTests<Program>(postgres)
{
    protected override string? ContractFile => "fulfillment.v1.yaml";
}
