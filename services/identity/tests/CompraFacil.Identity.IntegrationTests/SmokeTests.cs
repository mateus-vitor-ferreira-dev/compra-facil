using CompraFacil.BuildingBlocks.Testing;

namespace CompraFacil.Identity.IntegrationTests;

/// <summary>Sobe o identity com um Postgres real: health, ready, docs e ProblemDetails.</summary>
public sealed class SmokeTests(PostgresFixture postgres) : ServiceSmokeTests<Program>(postgres)
{
    protected override string? ContractFile => "identity.v1.yaml";

    protected override bool UsesMessaging => false; // ainda não publica eventos
}
