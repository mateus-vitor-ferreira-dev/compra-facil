using CompraFacil.BuildingBlocks.Testing;

namespace CompraFacil.Inventory.IntegrationTests;

/// <summary>Sobe o inventory com um Postgres real: health, ready, docs e ProblemDetails.</summary>
public sealed class SmokeTests(PostgresFixture postgres) : ServiceSmokeTests<Program>(postgres)
{
    protected override string? ContractFile => "inventory.v1.yaml";
}
