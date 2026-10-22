using System.Net.Http.Json;
using System.Text.Json;
using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Testing;
using CompraFacil.Notifications.Application.Emails;
using CompraFacil.Notifications.Domain;
using CompraFacil.Notifications.Infrastructure;
using CompraFacil.Notifications.Infrastructure.Email;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CompraFacil.Notifications.IntegrationTests;

/// <summary>Postgres + Mailpit reais (mesma imagem do compose); despachante chamado à mão.</summary>
public sealed class EmailFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private readonly IContainer _mailpit = new ContainerBuilder("axllent/mailpit:v1.31.6")
        .WithPortBinding(1025, assignRandomHostPort: true)
        .WithPortBinding(8025, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(8025).ForPath("/readyz")))
        .Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Mailpit { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await Task.WhenAll(_postgres.InitializeAsync().AsTask(), _mailpit.StartAsync());
        Mailpit = new HttpClient { BaseAddress = new Uri($"http://{_mailpit.Hostname}:{_mailpit.GetMappedPublicPort(8025)}") };
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b
            .UseSetting("ConnectionStrings:Default", _postgres.ConnectionString)
            .UseSetting("Kafka:Enabled", "false")
            .UseSetting("Smtp:Host", _mailpit.Hostname)
            .UseSetting("Smtp:Port", _mailpit.GetMappedPublicPort(1025).ToString(System.Globalization.CultureInfo.InvariantCulture))
            .UseSetting("Smtp:DispatcherEnabled", "false"));
        using var _ = Factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        Mailpit.Dispose();
        await Factory.DisposeAsync();
        await _mailpit.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

public sealed class EmailFlowTests(EmailFixture fx) : IClassFixture<EmailFixture>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task HandleAsync<TData, THandler>(TData data, Guid eventId) where THandler : IHandleEvent<TData>
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);
        await handler.HandleAsync(data, new EventContext(eventId, typeof(TData).Name, NotificationEvents.OrdersTopic, "c", DateTimeOffset.UtcNow), Ct);
        await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().SaveChangesAsync(Ct);
    }

    private Task<int> DispatchAsync(IEmailSender? sender = null, int batchSize = 20) =>
        ActivatorUtilities.CreateInstance<EmailDispatcher>(fx.Factory.Services,
            sender ?? fx.Factory.Services.GetRequiredService<IEmailSender>(),
            Microsoft.Extensions.Options.Options.Create(new SmtpOptions
            {
                Host = fx.Factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmtpOptions>>().Value.Host,
                Port = fx.Factory.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SmtpOptions>>().Value.Port,
                BatchSize = batchSize,
            })).DispatchOnceAsync(Ct);

    private async Task<Notification> NotificationAsync(Guid id)
    {
        await using var scope = fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Notifications.AsNoTracking().SingleAsync(n => n.Id == id, Ct);
    }

    /// <summary>Mensagens no Mailpit para o destinatário (API do Mailpit).</summary>
    private async Task<List<JsonElement>> InboxAsync(string to)
    {
        var search = await fx.Mailpit.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={Uri.EscapeDataString("to:" + to)}", Ct);
        return [.. search.GetProperty("messages").EnumerateArray()];
    }

    private async Task<JsonElement> MessageAsync(JsonElement summary) =>
        await fx.Mailpit.GetFromJsonAsync<JsonElement>($"/api/v1/message/{summary.GetProperty("ID").GetString()}", Ct);

    private static OrderConfirmedData Confirmed(string to) => new(Guid.NewGuid(), to,
        [new OrderItem("Lava e Seca Electrolux", 2, new Money(218390, "BRL"), new Money(436780, "BRL"))], new Money(436780, "BRL"));

    [Fact]
    public async Task OrderConfirmed_ArrivesInTheMailbox()
    {
        var to = $"ana-{Guid.NewGuid():N}@exemplo.com";
        var eventId = Guid.NewGuid();

        await HandleAsync<OrderConfirmedData, OnOrderConfirmed>(Confirmed(to), eventId);
        Assert.Equal(NotificationStatus.Pending, (await NotificationAsync(eventId)).Status); // o handler só registra
        await DispatchAsync();

        var summary = Assert.Single(await InboxAsync(to));
        var message = await MessageAsync(summary);
        Assert.EndsWith("confirmado", message.GetProperty("Subject").GetString(), StringComparison.Ordinal);
        Assert.Equal($"{eventId}@comprafacil.dev", message.GetProperty("MessageID").GetString());
        var text = message.GetProperty("Text").GetString()!;
        Assert.Contains("Lava e Seca Electrolux", text, StringComparison.Ordinal);
        Assert.Contains("R$ 4.367,80", text, StringComparison.Ordinal);
        Assert.Equal(NotificationStatus.Sent, (await NotificationAsync(eventId)).Status);
    }

    [Theory]
    [InlineData("out_of_stock", null, "sem estoque")]
    [InlineData("payment_failed", "insufficient_funds", "limite insuficiente")]
    public async Task OrderCancelled_ExplainsWhy(string reason, string? paymentFailure, string expected)
    {
        var to = $"bia-{Guid.NewGuid():N}@exemplo.com";

        await HandleAsync<OrderCancelledData, OnOrderCancelled>(new OrderCancelledData(Guid.NewGuid(), to, reason, paymentFailure), Guid.NewGuid());
        await DispatchAsync();

        var message = await MessageAsync(Assert.Single(await InboxAsync(to)));
        Assert.EndsWith("cancelado", message.GetProperty("Subject").GetString(), StringComparison.Ordinal);
        Assert.Contains(expected, message.GetProperty("Text").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SameEvent_SendsOneEmail()
    {
        var to = $"duplo-{Guid.NewGuid():N}@exemplo.com";
        var eventId = Guid.NewGuid();
        var data = Confirmed(to);

        await HandleAsync<OrderConfirmedData, OnOrderConfirmed>(data, eventId);
        await HandleAsync<OrderConfirmedData, OnOrderConfirmed>(data, eventId);
        await DispatchAsync();
        await DispatchAsync();

        Assert.Single(await InboxAsync(to));
    }

    [Fact]
    public async Task ConcurrentDispatchers_SendEachEmailOnce()
    {
        var to = $"lote-{Guid.NewGuid():N}@exemplo.com";
        for (var i = 0; i < 40; i++)
        {
            await HandleAsync<OrderConfirmedData, OnOrderConfirmed>(Confirmed(to), Guid.NewGuid());
        }

        // 4 réplicas do notifications despachando ao mesmo tempo, em lotes pequenos (se sobrepõem)
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            while (await DispatchAsync(batchSize: 3) > 0)
            {
            }
        })));

        var inbox = await InboxAsync(to);
        Assert.Equal(40, inbox.Count);
        Assert.Equal(40, inbox.Select(m => m.GetProperty("MessageID").GetString()).Distinct().Count());
    }

    [Fact]
    public async Task SmtpErrors_RetryAndThenGiveUp()
    {
        var eventId = Guid.NewGuid();
        await HandleAsync<OrderConfirmedData, OnOrderConfirmed>(Confirmed($"x-{Guid.NewGuid():N}@exemplo.com"), eventId);

        await DispatchAsync(new BrokenSmtp());
        var afterOne = await NotificationAsync(eventId);
        Assert.Equal((NotificationStatus.Pending, 1), (afterOne.Status, afterOne.Attempts)); // volta para a fila

        for (var i = 1; i < Notification.MaxAttempts; i++)
        {
            await DispatchAsync(new BrokenSmtp());
        }
        var gaveUp = await NotificationAsync(eventId);
        Assert.Equal(NotificationStatus.Failed, gaveUp.Status);
        Assert.Contains("servidor recusou", gaveUp.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClaimedButNotFinished_IsNotSentAgain()
    {
        // o processo reservou (Sending) e caiu antes de enviar: no máximo uma vez → não reenvia
        var to = $"queda-{Guid.NewGuid():N}@exemplo.com";
        var eventId = Guid.NewGuid();
        await HandleAsync<OrderConfirmedData, OnOrderConfirmed>(Confirmed(to), eventId);
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<NotificationsDbContext>().Notifications
                .Where(n => n.Id == eventId).ExecuteUpdateAsync(u => u.SetProperty(n => n.Status, NotificationStatus.Sending), Ct);
        }

        await DispatchAsync();

        Assert.Empty(await InboxAsync(to));
        Assert.Equal(NotificationStatus.Sending, (await NotificationAsync(eventId)).Status);
    }

    private sealed class BrokenSmtp : IEmailSender
    {
        public Task SendAsync(Notification notification, CancellationToken ct) =>
            throw new InvalidOperationException("servidor recusou a conexão");
    }
}
