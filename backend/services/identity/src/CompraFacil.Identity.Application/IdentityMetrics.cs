using System.Diagnostics.Metrics;

namespace CompraFacil.Identity.Application;

/// <summary>Métricas do identity (meter CompraFacil.Identity, exportado pelo ServiceDefaults).</summary>
public static class IdentityMetrics
{
    public const string MeterName = "CompraFacil.Identity";
    private static readonly Meter s_meter = new(MeterName);

    /// <summary>
    /// Logins por e-mail e senha, pelo resultado: succeeded, invalid_credentials (o 401, exista a conta ou não),
    /// locked_out (o 423) e not_confirmed (a senha certa de uma conta sem o e-mail confirmado). Sem o e-mail nem a
    /// conta nas tags: rajada de falhas é tentativa de senha em massa, e de quem fica nos logs.
    /// <para>
    /// A senha certa de uma conta com segundo fator não abre sessão: conta como mfa_required (falta o código) ou
    /// mfa_setup_required (operador sem 2FA), e não como succeeded. Assim succeeded continua sendo "sessão aberta
    /// só com a senha", e o fim do login do operador aparece em <see cref="MfaVerification"/>.
    /// </para>
    /// </summary>
    private static readonly Counter<long> s_logins = s_meter.CreateCounter<long>(
        "compra_facil.identity.logins", unit: "{login}", description: "Logins por e-mail e senha, por resultado.");

    public static void Login(string result) => s_logins.Add(1, new KeyValuePair<string, object?>("result", result));

    /// <summary>
    /// O segundo passo do login (ameaça T8), pelo método (totp, recovery_code, setup) e pelo resultado: succeeded
    /// (sessão aberta), invalid_code (o 422, que conta para o bloqueio), locked_out (o 423) e invalid_ticket (o
    /// 401: tíquete vencido, gasto ou de antes da troca de senha). Rajada de invalid_code é alguém com a senha
    /// do operador tentando adivinhar o código.
    /// </summary>
    private static readonly Counter<long> s_mfa = s_meter.CreateCounter<long>(
        "compra_facil.identity.mfa_verifications", unit: "{verification}",
        description: "Segundo passo do login (código do autenticador ou de recuperação), por método e resultado.");

    public static void MfaVerification(string method, string result) => s_mfa.Add(1,
        new KeyValuePair<string, object?>("method", method), new KeyValuePair<string, object?>("result", result));
}
