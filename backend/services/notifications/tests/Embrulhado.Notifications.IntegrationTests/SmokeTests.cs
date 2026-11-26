using Embrulhado.BuildingBlocks.Testing;

namespace Embrulhado.Notifications.IntegrationTests;

/// <summary>Sobe o notifications com um Postgres real: health, ready, docs e ProblemDetails.</summary>
public sealed class SmokeTests(PostgresFixture postgres) : ServiceSmokeTests<Program>(postgres)
{
    protected override string? ContractFile => null;
}
