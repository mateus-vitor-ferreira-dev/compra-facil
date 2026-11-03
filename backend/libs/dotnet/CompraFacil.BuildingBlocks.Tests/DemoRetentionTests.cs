using CompraFacil.BuildingBlocks.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CompraFacil.BuildingBlocks.Tests;

/// <summary>A retenção da demonstração (#280): só liga com Demo:AnonymizeAfter, e o corte é agora menos o prazo.</summary>
public sealed class DemoRetentionTests
{
    private sealed class RecordingAnonymizer : IPersonalDataAnonymizer
    {
        public static DateTimeOffset? LastCutoff { get; private set; }

        public Task<int> AnonymizeAsync(DateTimeOffset createdBefore, CancellationToken ct)
        {
            LastCutoff = createdBefore;
            return Task.FromResult(3);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static ServiceProvider Build(string? anonymizeAfter, TimeProvider? clock = null)
    {
        var builder = Host.CreateApplicationBuilder();
        if (anonymizeAfter is not null)
        {
            builder.Configuration["Demo:AnonymizeAfter"] = anonymizeAfter;
        }
        builder.Services.AddSingleton(clock ?? TimeProvider.System);
        builder.AddDemoRetention<RecordingAnonymizer>();
        return builder.Services.BuildServiceProvider();
    }

    [Fact]
    public void Sem_o_prazo_configurado_o_job_nao_existe()
    {
        using var services = Build(null);
        Assert.Null(services.GetService<DemoRetentionJob>());
        Assert.DoesNotContain(services.GetServices<IHostedService>(), s => s is DemoRetentionJob);
    }

    [Fact]
    public async Task Com_o_prazo_o_job_mascara_o_que_e_mais_antigo_que_agora_menos_o_prazo()
    {
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        using var services = Build("7.00:00:00", new FixedClock(now));

        var job = services.GetRequiredService<DemoRetentionJob>();
        Assert.Contains(services.GetServices<IHostedService>(), s => s is DemoRetentionJob);
        Assert.Equal(3, await job.RunOnceAsync(TestContext.Current.CancellationToken));
        Assert.Equal(now.AddDays(-7), RecordingAnonymizer.LastCutoff);
    }
}
