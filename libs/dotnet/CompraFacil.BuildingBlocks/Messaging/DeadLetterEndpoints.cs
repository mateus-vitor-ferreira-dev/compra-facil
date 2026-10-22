using CompraFacil.BuildingBlocks.Web;
using Confluent.Kafka;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace CompraFacil.BuildingBlocks.Messaging;

public sealed record DeadLetterList(IReadOnlyList<DeadLetter> Items);

public sealed record DeadLetterReplay(int Replayed);

public static class DeadLetterEndpoints
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 500;

    /// <summary>
    /// GET /v1/admin/dead-letters (pendentes) e POST /v1/admin/dead-letters/replay (reprocessa),
    /// com o JWT de admin (precisa do AddAdminAuth). Contrato em contracts/openapi/common/v1/dead-letters.yaml.
    /// </summary>
    public static IEndpointRouteBuilder MapDeadLetters(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/admin/dead-letters").RequireAuthorization(AdminAuth.Policy).WithTags("admin");

        group.MapGet("", async Task<Results<Ok<DeadLetterList>, ProblemHttpResult>> (DeadLetterStore dlt, int? limit, CancellationToken ct) =>
        {
            if (limit is < 1 or > MaxLimit)
            {
                return TypedResults.Problem(statusCode: StatusCodes.Status400BadRequest,
                    title: "Parâmetro inválido", detail: $"limit precisa estar entre 1 e {MaxLimit}");
            }
            try
            {
                return TypedResults.Ok(new DeadLetterList(await dlt.PendingAsync(limit ?? DefaultLimit, ct)));
            }
            catch (KafkaException e)
            {
                return KafkaUnavailable(e);
            }
        });

        group.MapPost("/replay", async Task<Results<Ok<DeadLetterReplay>, ProblemHttpResult>> (DeadLetterStore dlt, CancellationToken ct) =>
        {
            try
            {
                return TypedResults.Ok(new DeadLetterReplay(await dlt.ReplayAsync(ct)));
            }
            catch (KafkaException e)
            {
                return KafkaUnavailable(e);
            }
        });
        return app;
    }

    private static ProblemHttpResult KafkaUnavailable(KafkaException e) =>
        TypedResults.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
            title: "Kafka indisponível", detail: e.Error.Reason);
}
