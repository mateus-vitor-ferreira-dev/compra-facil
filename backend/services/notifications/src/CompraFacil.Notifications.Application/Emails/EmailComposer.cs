using System.Net;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using CompraFacil.BuildingBlocks.Messaging;
using Fluid;

namespace CompraFacil.Notifications.Application.Emails;

public sealed record ComposedEmail(string Subject, string HtmlBody, string TextBody);

/// <summary>Monta os e-mails a partir dos templates Liquid (Templates/*.liquid, embutidos).</summary>
public static partial class EmailComposer
{
    private static readonly FluidParser s_parser = new();

    // escapa <, >, &, aspas; acentos ficam legíveis (não viram &#xE9;)
    private static readonly HtmlEncoder s_html = HtmlEncoder.Create(UnicodeRanges.All);
    private static readonly IFluidTemplate s_confirmed = Load("order-confirmed");
    private static readonly IFluidTemplate s_cancelled = Load("order-cancelled");
    private static readonly IFluidTemplate s_confirmEmail = Load("confirm-email");
    private static readonly IFluidTemplate s_invoiceIssued = Load("invoice-issued");
    private static readonly IFluidTemplate s_returned = Load("order-returned");
    private static readonly IFluidTemplate s_refunded = Load("order-refunded");
    private static readonly IFluidTemplate s_shipped = Load("order-shipped");
    private static readonly IFluidTemplate s_delivered = Load("order-delivered");

    /// <summary>O pedido saiu (#224): o código de rastreio, a transportadora, o link dela e o do pedido.</summary>
    public static async Task<ComposedEmail> ShippedAsync(OrderShippedData order, Uri storeUrl)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(storeUrl);
        var number = OrderNumber(order.OrderId);
        var orderUrl = OrderUrl(storeUrl, order.OrderId);
        var trackingUrl = SafeLink(order.Tracking.Url);
        var html = await s_shipped.RenderAsync(Context(new
        {
            order_number = number,
            tracking_number = order.Tracking.Number,
            provider = order.Tracking.Provider,
            tracking_url = trackingUrl,
            order_url = orderUrl,
        }), s_html);
        var text = ToText(html) + (trackingUrl is null ? "" : "\n" + trackingUrl) + "\n" + orderUrl;
        return new ComposedEmail($"Pedido {number} enviado", html, text);
    }

    /// <summary>Chegou (#224): o aviso, com o prazo de 7 dias para desistir e o link do pedido, onde se desiste.</summary>
    public static async Task<ComposedEmail> DeliveredAsync(OrderDeliveredData order, Uri storeUrl)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(storeUrl);
        var number = OrderNumber(order.OrderId);
        var orderUrl = OrderUrl(storeUrl, order.OrderId);
        var html = await s_delivered.RenderAsync(Context(new { order_number = number, order_url = orderUrl }), s_html);
        return new ComposedEmail($"Pedido {number} entregue", html, ToText(html) + "\n" + orderUrl);
    }

    private static string OrderUrl(Uri storeUrl, Guid orderId) => new Uri(storeUrl, $"conta/pedidos/{orderId}").AbsoluteUri;

    /// <summary>Só link http(s) absoluto vira link no e-mail: o resto (um javascript: num evento adulterado) some.</summary>
    internal static string? SafeLink(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;

    /// <summary>A devolução registrada (#223): o estorno já pedido, sem esperar o fornecedor.</summary>
    public static async Task<ComposedEmail> ReturnedAsync(OrderReturnedData order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var number = OrderNumber(order.OrderId);
        var html = await s_returned.RenderAsync(Context(new { order_number = number, total = Brl(order.Total) }), s_html);
        return new ComposedEmail($"Devolução do pedido {number} registrada", html, ToText(html));
    }

    /// <summary>O estorno feito: do pedido devolvido ou do cancelado depois de pago.</summary>
    public static async Task<ComposedEmail> RefundedAsync(OrderRefundedData refund)
    {
        ArgumentNullException.ThrowIfNull(refund);
        var number = OrderNumber(refund.OrderId);
        var reason = refund.Reason switch
        {
            "returned" => "é a devolução que você pediu.",
            "invoice_failed" => "não conseguimos emitir a nota fiscal do pedido.",
            _ => "o fornecedor não conseguiu atender o pedido.",
        };
        var html = await s_refunded.RenderAsync(Context(new { order_number = number, amount = Brl(refund.Amount), reason }), s_html);
        return new ComposedEmail($"Estorno do pedido {number} feito", html, ToText(html));
    }

    public static async Task<ComposedEmail> ConfirmedAsync(OrderConfirmedData order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var number = OrderNumber(order.OrderId);
        var html = await s_confirmed.RenderAsync(Context(new
        {
            order_number = number,
            items = order.Items.Select(i => new
            {
                name = i.Name,
                quantity = i.Quantity,
                unit_price = Brl(i.UnitPrice),
                line_total = Brl(i.LineTotal),
            }).ToList(),
            total = Brl(order.Total),
        }), s_html); // dados de produto/cliente nunca entram crus no HTML
        return new ComposedEmail($"Pedido {number} confirmado", html, ToText(html));
    }

    public static async Task<ComposedEmail> CancelledAsync(OrderCancelledData order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var number = OrderNumber(order.OrderId);
        var (reason, advice) = Explain(order.Reason, order.PaymentFailureReason);
        var html = await s_cancelled.RenderAsync(Context(new { order_number = number, reason, advice }), s_html);
        return new ComposedEmail($"Pedido {number} cancelado", html, ToText(html));
    }

    /// <summary>A nota fiscal do pedido (#221): o número, a chave e o link do pedido, onde fica o DANFE.</summary>
    public static async Task<ComposedEmail> InvoiceIssuedAsync(InvoiceIssuedData invoice, Uri storeUrl)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(storeUrl);
        var number = OrderNumber(invoice.OrderId);
        var orderUrl = OrderUrl(storeUrl, invoice.OrderId);
        var html = await s_invoiceIssued.RenderAsync(Context(new
        {
            order_number = number,
            number = invoice.Number,
            series = invoice.Series,
            total = Brl(new Money(invoice.TotalCents, "BRL")),
            access_key = string.Join(' ', invoice.AccessKey.Chunk(4).Select(c => new string(c))),
            order_url = orderUrl,
        }), s_html);
        return new ComposedEmail($"Nota fiscal do pedido {number}", html, ToText(html) + "\n" + orderUrl);
    }

    public static async Task<ComposedEmail> ConfirmEmailAsync(EmailConfirmationRequestedData request, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(request);
        // só links http(s): o template não pode virar um javascript: vindo de um evento adulterado
        if (request.ConfirmationUrl.Scheme is not ("http" or "https"))
        {
            throw new PoisonMessageException($"confirmation_url com esquema {request.ConfirmationUrl.Scheme}");
        }
        var html = await s_confirmEmail.RenderAsync(Context(new
        {
            name = FirstName(request.Name),
            url = request.ConfirmationUrl.AbsoluteUri,
            valid_for = ValidFor(request.ExpiresAt - now),
        }), s_html);
        return new ComposedEmail("Confirme o seu e-mail", html, ToText(html) + "\n" + request.ConfirmationUrl.AbsoluteUri);
    }

    /// <summary>"24 horas", "2 dias": a validade que o identity deu ao link.</summary>
    internal static string ValidFor(TimeSpan left) => left.TotalHours switch
    {
        < 1.5 => "1 hora",
        < 48 => $"{Math.Round(left.TotalHours):0} horas",
        _ => $"{Math.Round(left.TotalDays):0} dias",
    };

    internal static string FirstName(string name) => name.Trim().Split(' ', 2)[0] is { Length: > 0 } first ? first : "olá";

    /// <summary>Motivo em linguagem de cliente (sem detalhes internos como fraude).</summary>
    internal static (string Reason, string Advice) Explain(string reason, string? paymentFailure) => (reason, paymentFailure) switch
    {
        ("out_of_stock", _) => ("um dos produtos ficou sem estoque.",
            "Nada foi cobrado. Confira a disponibilidade e faça um novo pedido."),
        ("payment_failed", "insufficient_funds") => ("o pagamento foi recusado por limite insuficiente.",
            "Tente outro cartão ou um valor menor."),
        ("payment_failed", "card_declined") => ("o cartão foi recusado pelo emissor.",
            "Confira os dados do cartão ou tente outro."),
        ("payment_failed", "pix_expired") => ("o PIX não foi pago até a validade.",
            "Nada foi cobrado. Se ainda quiser os produtos, faça o pedido de novo e pague o PIX dentro do prazo."),
        ("payment_failed", "gateway_error") => ("houve uma falha ao processar o pagamento.",
            "Nada foi cobrado. Tente de novo em alguns minutos."),
        ("payment_failed", _) => ("o pagamento não foi aprovado.",
            "Entre em contato com o seu banco ou tente outra forma de pagamento."),
        ("supplier_failed", _) => ("o fornecedor não conseguiu atender o pedido.",
            "Nada fica cobrado: o pagamento foi cancelado ou estornado por inteiro, pelo mesmo meio."),
        ("invoice_failed", _) => ("não conseguimos emitir a nota fiscal do pedido.",
            "Nada fica cobrado: o pagamento foi cancelado ou estornado por inteiro, pelo mesmo meio. Confira o CPF e o endereço e faça o pedido de novo."),
        _ => ("o pedido não pôde ser concluído.", "Nada foi cobrado."),
    };

    /// <summary>Número curto para o cliente (o id completo fica nos links e no suporte).</summary>
    internal static string OrderNumber(Guid orderId) => orderId.ToString("N")[^8..].ToUpperInvariant();

    /// <summary>R$ 1.234,56 (sem depender da cultura: o build usa globalização invariante).</summary>
    internal static string Brl(Money money)
    {
        var abs = Math.Abs(money.AmountCents);
        var reais = (abs / 100).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.');
        var sign = money.AmountCents < 0 ? "-" : "";
        var symbol = money.Currency == "BRL" ? "R$" : money.Currency;
        return $"{sign}{symbol} {reais},{abs % 100:00}";
    }

    private static TemplateContext Context(object model)
    {
        var options = new TemplateOptions { MemberAccessStrategy = new UnsafeMemberAccessStrategy() };
        return new TemplateContext(model, options);
    }

    private static IFluidTemplate Load(string name)
    {
        var assembly = typeof(EmailComposer).Assembly;
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith($".Templates.{name}.liquid", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);
        return s_parser.TryParse(reader.ReadToEnd(), out var template, out var error)
            ? template
            : throw new InvalidOperationException($"template {name}: {error}");
    }

    /// <summary>Versão texto do HTML: blocos viram linhas, células viram colunas, o resto das tags some.</summary>
    internal static string ToText(string html)
    {
        var text = BlockEnd().Replace(html, "\n");
        text = CellEnd().Replace(text, "\t");
        text = Tags().Replace(text, "");
        var lines = WebUtility.HtmlDecode(text).Split('\n')
            .Select(l => Spaces().Replace(l, " ").Trim(' ', '\t'))
            .Where(l => l.Length > 0);
        return string.Join('\n', lines);
    }

    [GeneratedRegex("</(p|h1|tr|table)>|<br\\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex("</t[dh]>", RegexOptions.IgnoreCase)]
    private static partial Regex CellEnd();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tags();

    [GeneratedRegex("[ \\r]+")]
    private static partial Regex Spaces();
}
