using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CompraFacil.Payments.Application.Payments;
using CompraFacil.Payments.Domain;
using Microsoft.Extensions.Options;
using QRCoder;

namespace CompraFacil.Payments.Infrastructure.Provider;

/// <summary>
/// O provedor pela Efí (#379), validado contra o payment-sim e escrito pela documentação de dev.efipay.com.br
/// (2026-11). O cartão vai pela API Cobranças, em dois passos (POST /v1/charge e /pay), porque ela não tem chave de
/// idempotência: pagar uma cobrança que já tem forma de pagamento é recusado (3500030), e a nova tentativa só lê o
/// resultado. O PIX vai pela API Pix, com o txid saído do pedido (o PUT repetido é 409 e vira consulta). A
/// referência do cartão é o charge_id (só dígitos); a do PIX, o txid (começa com letras).
/// </summary>
internal sealed class EfiProvider(EfiApi api, IOptions<EfiOptions> options, TimeProvider clock) : IPaymentProvider
{
    public string Name => "efi";

    // ---------- PIX ----------

    public async Task<ProviderPayment> CreatePixAsync(PixRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var txid = TxidOf(request.OrderId);
        var seconds = (int)Math.Max(60, Math.Ceiling((request.ExpiresAt - clock.GetUtcNow()).TotalSeconds));
        var body = new CobRequest(new CobCalendar(seconds), DebtorOf(request.Payer), new CobValue(Reais(request.Amount.AmountCents)),
            options.Value.PixKey, $"Embrulhado, pedido {OrderNumber(request.OrderId)}");
        Cob cob;
        try
        {
            cob = await api.SendAsync<Cob>(EfiApiKind.Pix, HttpMethod.Put, $"v2/cob/{txid}", body, ct);
        }
        catch (EfiApiException e) when (e.Name == "txid_duplicado")
        {
            cob = await GetCobAsync(txid, ct); // a nova tentativa: o PIX já existe, o mesmo QR
        }
        string? ticket = null;
        if (cob.Loc is { } loc)
        {
            var qr = await api.SendAsync<QrCodeResponse>(EfiApiKind.Pix, HttpMethod.Get, $"v2/loc/{loc.Id}/qrcode", null, ct);
            ticket = qr.LinkVisualizacao;
        }
        return ToProvider(cob, ticket);
    }

    // ---------- cartão ----------

    public async Task<string> OpenCardChargeAsync(CardRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new ChargeRequest(
            [new ChargeItem($"Embrulhado, pedido {OrderNumber(request.OrderId)}", request.Amount.AmountCents, 1)],
            new ChargeMetadata(request.OrderId.ToString(), options.Value.ChargeNotificationUrl?.AbsoluteUri));
        try
        {
            var created = await api.SendAsync<Envelope<CreatedCharge>>(EfiApiKind.Charges, HttpMethod.Post, "v1/charge", body, ct);
            return created.Data.ChargeId.ToString(CultureInfo.InvariantCulture);
        }
        catch (EfiApiException e)
        {
            throw e.Rejected();
        }
    }

    public async Task<ProviderPayment> PayCardAsync(string reference, CardRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = new PayRequest(new PayPayment(new CreditCard(CustomerOf(request), request.Installments, request.CardToken,
            AddressOf(request.Payer?.Address))));
        try
        {
            await api.SendAsync<Envelope<object>>(EfiApiKind.Charges, HttpMethod.Post, $"v1/charge/{CardId(reference)}/pay", body, ct);
        }
        catch (EfiApiException e) when (e.Code is 3500030 or 3500044)
        {
            // a cobrança já tem forma de pagamento: a tentativa anterior pagou, e não se cobra de novo
        }
        catch (EfiApiException e)
        {
            throw e.Rejected();
        }
        return await GetAsync(reference, ct); // o resultado e a máscara do cartão vêm da consulta
    }

    // ---------- os dois ----------

    public async Task<ProviderPayment> GetAsync(string reference, CancellationToken ct)
    {
        if (!IsCard(reference))
        {
            return ToProvider(await GetCobAsync(reference, ct), ticket: null);
        }
        try
        {
            var charge = await api.SendAsync<Envelope<Charge>>(EfiApiKind.Charges, HttpMethod.Get, $"v1/charge/{CardId(reference)}", null, ct);
            return ToProvider(charge.Data);
        }
        catch (EfiApiException e)
        {
            throw e.Rejected();
        }
    }

    public async Task<ProviderPayment> CancelAsync(string reference, CancellationToken ct)
    {
        try
        {
            if (!IsCard(reference))
            {
                var cob = await api.SendAsync<Cob>(EfiApiKind.Pix, HttpMethod.Patch, $"v2/cob/{reference}",
                    new { status = "REMOVIDA_PELO_USUARIO_RECEBEDOR" }, ct);
                return ToProvider(cob, ticket: null);
            }
            await api.SendAsync<Envelope<object>>(EfiApiKind.Charges, HttpMethod.Put, $"v1/charge/{CardId(reference)}/cancel", null, ct);
        }
        catch (EfiApiException e)
        {
            throw e.Rejected(); // já pago, aprovado ou recusado: quem chamou consulta e decide
        }
        return await GetAsync(reference, ct);
    }

    public async Task<ProviderRefund> RefundAsync(string reference, long? amountCents, string idempotencyKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        return IsCard(reference)
            ? await RefundCardAsync(reference, amountCents, idempotencyKey, ct)
            : await RefundPixAsync(reference, amountCents, idempotencyKey, ct);
    }

    /// <summary>
    /// O estorno do cartão não tem id nem chave de idempotência na Efí (responde "Reembolso em processamento"). A
    /// referência local sai da chave; repetir depois de cair no meio pode ser recusado ("já existe um estorno em
    /// processamento"), e aí a equipe confere no painel da Efí.
    /// </summary>
    private async Task<ProviderRefund> RefundCardAsync(string reference, long? amountCents, string key, CancellationToken ct)
    {
        var amount = amountCents ?? (await api.SendAsync<Envelope<Charge>>(EfiApiKind.Charges, HttpMethod.Get, $"v1/charge/{CardId(reference)}", null, ct)).Data.Total;
        try
        {
            await api.SendAsync<Envelope<object>>(EfiApiKind.Charges, HttpMethod.Post, $"v1/charge/card/{CardId(reference)}/refund",
                new { amount }, ct);
        }
        catch (EfiApiException e)
        {
            throw e.Rejected();
        }
        return new ProviderRefund($"{reference}-{RefundId(key)[..12]}", amount);
    }

    /// <summary>A devolução do PIX tem o id da loja (saído da chave): repetida é 409 e vira consulta.</summary>
    private async Task<ProviderRefund> RefundPixAsync(string txid, long? amountCents, string key, CancellationToken ct)
    {
        var cob = await GetCobAsync(txid, ct);
        if (cob.Pix is not [var paid, ..])
        {
            throw new PaymentProviderRejectedException($"o PIX {txid} não foi pago: não há o que devolver");
        }
        var amount = amountCents ?? Cents(paid.Valor) - (paid.Devolucoes ?? []).Where(d => d.Status != "NAO_REALIZADO").Sum(d => Cents(d.Valor));
        var id = RefundId(key);
        var path = $"v2/pix/{paid.EndToEndId}/devolucao/{id}";
        Devolucao refund;
        try
        {
            refund = await api.SendAsync<Devolucao>(EfiApiKind.Pix, HttpMethod.Put, path, new { valor = Reais(amount) }, ct);
        }
        catch (EfiApiException e) when (e.Name == "devolucao_id_duplicado")
        {
            refund = await api.SendAsync<Devolucao>(EfiApiKind.Pix, HttpMethod.Get, path, null, ct);
        }
        catch (EfiApiException e)
        {
            throw e.Rejected();
        }
        if (refund.Status == "NAO_REALIZADO")
        {
            throw new PaymentProviderRejectedException($"a Efí não realizou a devolução do PIX {txid}: {refund.Motivo}");
        }
        return new ProviderRefund(refund.RtrId ?? refund.Id, Cents(refund.Valor));
    }

    public async Task<IReadOnlyList<string>> ChangedByNotificationAsync(string token, CancellationToken ct)
    {
        try
        {
            var events = await api.SendAsync<Envelope<List<NotificationEvent>>>(EfiApiKind.Charges, HttpMethod.Get,
                $"v1/notification/{Uri.EscapeDataString(token)}", null, ct);
            return [.. events.Data.Where(e => e.Type == "charge" && e.Identifiers?.ChargeId is not null)
                .Select(e => e.Identifiers!.ChargeId!.Value.ToString(CultureInfo.InvariantCulture)).Distinct()];
        }
        catch (EfiApiException e)
        {
            throw e.Rejected();
        }
    }

    // ---------- tradução ----------

    private async Task<Cob> GetCobAsync(string txid, CancellationToken ct)
    {
        try
        {
            return await api.SendAsync<Cob>(EfiApiKind.Pix, HttpMethod.Get, $"v2/cob/{txid}", null, ct);
        }
        catch (EfiApiException e)
        {
            throw e.Rejected();
        }
    }

    /// <summary>O PIX: ATIVA é pendente até a validade (a Efí não muda o status quando ela passa); pago é CONCLUIDA.</summary>
    private ProviderPayment ToProvider(Cob cob, string? ticket)
    {
        var expires = cob.Calendario.Criacao + TimeSpan.FromSeconds(cob.Calendario.Expiracao);
        var status = cob.Status switch
        {
            "ATIVA" => clock.GetUtcNow() >= expires ? ProviderStatus.Expired : ProviderStatus.Pending,
            "CONCLUIDA" => ProviderStatus.Approved,
            "REMOVIDA_PELO_USUARIO_RECEBEDOR" => ProviderStatus.Cancelled,
            "REMOVIDA_PELO_PSP" => ProviderStatus.Expired,
            _ => throw new PaymentProviderUnavailableException($"status desconhecido da Efí no PIX: {cob.Status}"),
        };
        var pix = cob.PixCopiaECola is { Length: > 0 } code ? new PixCharge(code, QrPng(code), ticket, expires) : null;
        // a devolução aceita e não realizada (sem saldo na conta, #396): a referência é a mesma que o estorno guardou
        var failed = (cob.Pix ?? []).SelectMany(p => p.Devolucoes ?? []).Where(d => d.Status == "NAO_REALIZADO")
            .Select(d => d.RtrId ?? d.Id).ToList();
        return new ProviderPayment(cob.Txid, status, Pix: pix, FailedRefunds: failed.Count > 0 ? failed : null);
    }

    /// <summary>O cartão: approved é aprovado pela operadora e não liquidado (Authorized); paid, liquidado.</summary>
    private static ProviderPayment ToProvider(Charge charge)
    {
        var status = charge.Status switch
        {
            "new" or "waiting" or "identified" => ProviderStatus.InProcess,
            "approved" => ProviderStatus.Authorized,
            "paid" or "settled" => ProviderStatus.Approved,
            "unpaid" => ProviderStatus.Rejected,
            "canceled" or "expired" => ProviderStatus.Cancelled,
            "refunded" => ProviderStatus.Refunded,
            "contested" => ProviderStatus.Disputed,
            _ => throw new PaymentProviderUnavailableException($"status desconhecido da Efí no cartão: {charge.Status}"),
        };
        FailureReason? reason = status == ProviderStatus.Rejected ? ReasonOf(charge.Reason) : null;
        var mask = charge.Payment?.CreditCard?.Mask;
        var lastFour = mask is { Length: >= 4 } ? mask[^4..] : null;
        return new ProviderPayment(charge.ChargeId.ToString(CultureInfo.InvariantCulture), status, reason, CardLastFour: lastFour);
    }

    /// <summary>A recusa vem em texto (padrão ABECS): segurança é suspeita de fraude; "51" ou saldo, saldo insuficiente.</summary>
    internal static FailureReason ReasonOf(string? reason) => reason switch
    {
        null => FailureReason.CardDeclined,
        _ when reason.Contains("segurança", StringComparison.OrdinalIgnoreCase) || reason.Contains("risco", StringComparison.OrdinalIgnoreCase)
            => FailureReason.FraudSuspected,
        _ when reason.StartsWith("51", StringComparison.Ordinal) || reason.Contains("saldo", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("limite", StringComparison.OrdinalIgnoreCase) => FailureReason.InsufficientFunds,
        _ => FailureReason.CardDeclined,
    };

    /// <summary>O txid do PIX: "cf" e o id do pedido sem hífens (34 alfanuméricos), o mesmo em toda tentativa.</summary>
    internal static string TxidOf(Guid orderId) => "cf" + orderId.ToString("N");

    private static bool IsCard(string reference) => reference.Length > 0 && reference.All(char.IsAsciiDigit);

    private static string CardId(string reference) => IsCard(reference)
        ? reference
        : throw new PaymentProviderRejectedException($"referência de cartão inválida: {reference}");

    /// <summary>O id da devolução na Efí (1 a 35 alfanuméricos), saído da chave: a mesma chave, a mesma devolução.</summary>
    internal static string RefundId(string key) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];

    private static string OrderNumber(Guid orderId) => orderId.ToString("N")[^8..].ToUpperInvariant();

    internal static string Reais(long cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static long Cents(string reais) => (long)Math.Round(decimal.Parse(reais, CultureInfo.InvariantCulture) * 100m);

    /// <summary>O QR em PNG (base64), desenhado aqui a partir do copia-e-cola: a Efí manda SVG, e a loja mostra PNG.</summary>
    private static string QrPng(string code)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(code, QRCodeGenerator.ECCLevel.M);
        return Convert.ToBase64String(new PngByteQRCode(data).GetGraphic(8));
    }

    private static CobDebtor? DebtorOf(Payer? payer) => payer is null ? null : new CobDebtor(payer.Cpf, Truncate(payer.Name, 200));

    /// <summary>
    /// Quem paga, como a Efí exige no cartão: nome e sobrenome só com letras, CPF, e-mail e telefone. Sem algum
    /// deles (o pedido de antes do #379), a cobrança não vai.
    /// </summary>
    private static ChargeCustomer CustomerOf(CardRequest request)
    {
        var payer = request.Payer;
        if (payer is null || string.IsNullOrWhiteSpace(payer.Phone) || string.IsNullOrWhiteSpace(request.PayerEmail))
        {
            throw new PaymentProviderRejectedException("o cartão exige nome, CPF, e-mail e telefone de quem paga");
        }
        return new ChargeCustomer(LettersOnly(payer.Name), payer.Cpf, request.PayerEmail, new string([.. payer.Phone.Where(char.IsAsciiDigit)]));
    }

    private static ChargeAddress? AddressOf(BillingAddress? a) => a is null
        ? null
        : new ChargeAddress(a.Street, a.Number, a.District, a.ZipCode, a.City, a.Complement, a.State.ToUpperInvariant());

    /// <summary>A Efí aceita no nome só letras (com acento) e espaços: "D'Ávila" vira "D Ávila".</summary>
    internal static string LettersOnly(string name) =>
        string.Join(' ', new string([.. name.Select(c => char.IsLetter(c) ? c : ' ')]).Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    // ---------- os formatos da Efí ----------

    internal sealed record Envelope<T>(int Code, T Data);

    private sealed record ChargeItem(string Name, long Value, int Amount);

    private sealed record ChargeMetadata(string CustomId, string? NotificationUrl);

    private sealed record ChargeRequest(IReadOnlyList<ChargeItem> Items, ChargeMetadata Metadata);

    private sealed record CreatedCharge(long ChargeId, string Status);

    private sealed record ChargeCustomer(string Name, string Cpf, string Email, string PhoneNumber);

    private sealed record ChargeAddress(string Street, string Number, string Neighborhood, string Zipcode, string City, string? Complement, string State);

    private sealed record CreditCard(ChargeCustomer Customer, int Installments, string PaymentToken, ChargeAddress? BillingAddress);

    private sealed record PayPayment(CreditCard CreditCard);

    private sealed record PayRequest(PayPayment Payment);

    internal sealed record CardInfo(string? Mask);

    internal sealed record ChargePayment(CardInfo? CreditCard);

    internal sealed record Charge(long ChargeId, long Total, string Status, string? Reason, ChargePayment? Payment);

    internal sealed record NotificationIdentifiers(long? ChargeId);

    internal sealed record NotificationEvent(int Id, string Type, NotificationIdentifiers? Identifiers);

    private sealed record CobCalendar(int Expiracao);

    private sealed record CobDebtor(string Cpf, string Nome);

    private sealed record CobValue(string Original);

    private sealed record CobRequest(CobCalendar Calendario, CobDebtor? Devedor, CobValue Valor, string Chave, string SolicitacaoPagador);

    internal sealed record CobCalendarRead(DateTimeOffset Criacao, int Expiracao);

    internal sealed record CobLoc(long Id);

    internal sealed record Devolucao(string Id, string? RtrId, string Valor, string Status, string? Motivo);

    internal sealed record PixReceived(string EndToEndId, string Valor, IReadOnlyList<Devolucao>? Devolucoes);

    internal sealed record Cob(string Txid, string Status, CobCalendarRead Calendario, CobLoc? Loc, string? PixCopiaECola, IReadOnlyList<PixReceived>? Pix);

    private sealed record QrCodeResponse(string? Qrcode, string? LinkVisualizacao);
}
