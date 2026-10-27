using System.Collections.Concurrent;
using CompraFacil.Payments.Application.Payments;
using CompraFacil.Payments.Domain;
using Microsoft.Extensions.Options;

namespace CompraFacil.Payments.Infrastructure.Gateway;

/// <summary>Regras do gateway falso (seção Payments:Fake): resultados previsíveis para demo e testes.</summary>
public sealed class FakeGatewayOptions
{
    /// <summary>Acima deste total, recusa com insufficient_funds (padrão: R$ 50.000,00).</summary>
    public long MaxApprovedAmountCents { get; set; } = 5_000_000;

    /// <summary>Clientes com cartão recusado (card_declined).</summary>
    public IList<Guid> DeclinedCustomers { get; } = [];

    /// <summary>Clientes barrados pelo antifraude (fraud_suspected).</summary>
    public IList<Guid> FraudCustomers { get; } = [];

    /// <summary>Latência simulada de cada cobrança.</summary>
    public TimeSpan Latency { get; set; } = TimeSpan.FromMilliseconds(150);
}

/// <summary>
/// Gateway falso. Idempotente pela chave (em memória, por processo — um gateway real guarda no lado
/// dele): a mesma chave devolve a mesma resposta, sem "cobrar" de novo.
/// </summary>
public sealed class FakePaymentGateway(IOptions<FakeGatewayOptions> options) : IPaymentGateway
{
    private readonly ConcurrentDictionary<string, ChargeResult> _charges = new();

    public string Provider => "fake";

    /// <summary>Quantas cobranças de fato aconteceram (para os testes de idempotência).</summary>
    public int ChargesMade => _charges.Count;

    public async Task<ChargeResult> ChargeAsync(ChargeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_charges.TryGetValue(request.IdempotencyKey, out var previous))
        {
            return previous;
        }
        await Task.Delay(options.Value.Latency, ct);
        return _charges.GetOrAdd(request.IdempotencyKey, _ => Decide(request));
    }

    private ChargeResult Decide(ChargeRequest request)
    {
        var o = options.Value;
        var reference = "fake_ch_" + Guid.NewGuid().ToString("N")[..16];
        if (o.FraudCustomers.Contains(request.CustomerId))
        {
            return new ChargeDeclined(FailureReason.FraudSuspected, reference);
        }
        if (o.DeclinedCustomers.Contains(request.CustomerId))
        {
            return new ChargeDeclined(FailureReason.CardDeclined, reference);
        }
        if (request.Amount.AmountCents > o.MaxApprovedAmountCents)
        {
            return new ChargeDeclined(FailureReason.InsufficientFunds, reference);
        }
        return new ChargeApproved(reference);
    }
}
