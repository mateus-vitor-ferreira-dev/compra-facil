using CompraFacil.BuildingBlocks.Testing;
using Xunit.Sdk;

namespace CompraFacil.BuildingBlocks.Tests;

/// <summary>O helper de contrato precisa reprovar de verdade, inclusive através de $ref entre arquivos.</summary>
public sealed class ContractsTests
{
    private const string Id = "7d5c9a4e-6a0b-4b8f-9f0e-2b1c3d4e5f60";

    private static string PaymentApproved(string amount) => $$$"""
        {"event_id":"{{{Id}}}","event_type":"PaymentApproved","event_version":1,"occurred_at":"2026-11-28T03:00:02Z",
         "producer":"payments","correlation_id":"req-1","causation_id":null,
         "data":{"order_id":"{{{Id}}}","payment_id":"{{{Id}}}","amount":{{{amount}}},"provider":"mercadopago","provider_reference":null,"method":"card","installments":1,"captured":false}}
        """;

    [Fact]
    public void ValidEvent_Passes() =>
        ContractAssert.AssertEvent("payments.v1.yaml", "PaymentApproved", PaymentApproved("""{"amount_cents":100,"currency":"BRL"}"""));

    [Theory]
    // Money vem da OpenAPI (../openapi/common/v1/common.yaml): $ref relativo para outro arquivo
    [InlineData("""{"amount_cents":-1,"currency":"BRL"}""")]
    [InlineData("""{"amount_cents":100,"currency":"brl"}""")]
    [InlineData("""{"amount_cents":100}""")]
    public void InvalidMoney_ThroughCrossFileRef_Fails(string amount) =>
        Assert.Throws<FailException>(() => ContractAssert.AssertEvent("payments.v1.yaml", "PaymentApproved", PaymentApproved(amount)));

    [Fact]
    public void WrongEnvelope_Fails() =>
        Assert.Throws<FailException>(() => ContractAssert.AssertEvent("payments.v1.yaml", "PaymentApproved",
            PaymentApproved("""{"amount_cents":100,"currency":"BRL"}""").Replace(Id + "\",\"event_type", "nao-e-uuid\",\"event_type", StringComparison.Ordinal)));

    [Fact]
    public void OpenApiSchema_IsChecked()
    {
        ContractAssert.AssertSchema("common/v1/common.yaml", "/components/schemas/Money", """{"amount_cents":1,"currency":"BRL"}""");
        Assert.Throws<FailException>(() =>
            ContractAssert.AssertSchema("common/v1/common.yaml", "/components/schemas/Money", """{"amount_cents":"1","currency":"BRL"}"""));
    }
}
