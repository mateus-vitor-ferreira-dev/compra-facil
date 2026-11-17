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
    /// </summary>
    private static readonly Counter<long> s_logins = s_meter.CreateCounter<long>(
        "compra_facil.identity.logins", unit: "{login}", description: "Logins por e-mail e senha, por resultado.");

    public static void Login(string result) => s_logins.Add(1, new KeyValuePair<string, object?>("result", result));
}
