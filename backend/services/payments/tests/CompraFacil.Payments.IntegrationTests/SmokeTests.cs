using CompraFacil.BuildingBlocks.Testing;

namespace CompraFacil.Payments.IntegrationTests;

/// <summary>Sobe o payments com um Postgres real: health, ready, docs e ProblemDetails.</summary>
public sealed class SmokeTests(PostgresFixture postgres) : ServiceSmokeTests<Program>(postgres)
{
    protected override string? ContractFile => "payments.v1.yaml";
}
