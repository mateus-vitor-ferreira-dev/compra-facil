using CompraFacil.BuildingBlocks.Data;
using CompraFacil.Notifications.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CompraFacil.Notifications.Infrastructure;

/// <summary>
/// Retenção da demonstração (#280): apaga o corpo dos e-mails antigos já resolvidos (enviados ou desistidos), que
/// podem ter o nome, o endereço e o código de devolução de quem comprou. Ficam o destinatário, o assunto e o status,
/// para o painel das mensagens com falha e a deduplicação pelo evento.
/// </summary>
internal sealed class NotificationsDemoAnonymizer(NotificationsDbContext db) : IPersonalDataAnonymizer
{
    private const string Removed = "(removido pela retenção da demonstração)";

    public Task<int> AnonymizeAsync(DateTimeOffset createdBefore, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE notifications SET html_body = {Removed}, text_body = {Removed}
            WHERE created_at < {createdBefore} AND status IN ('Sent', 'Failed') AND text_body <> {Removed}
            """, ct);
}
