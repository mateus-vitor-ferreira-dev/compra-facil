namespace CompraFacil.BuildingBlocks.Web;

/// <summary>
/// <c>&lt;api&gt; healthcheck</c>: consulta o /ready local e sai com 0 se o serviço está pronto. É o
/// HEALTHCHECK do container: a imagem chiseled não tem shell nem curl (como o catalog distroless).
/// </summary>
public static class Healthcheck
{
    public static bool Requested(string[] args) => args is ["healthcheck"];

    public static async Task<int> RunAsync()
    {
        // ASPNETCORE_HTTP_PORTS pode ter vários (8002;8080): o primeiro basta
        var ports = Environment.GetEnvironmentVariable("ASPNETCORE_HTTP_PORTS") ?? "8080";
        var port = ports.Split(';', ',')[0].Trim();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        try
        {
            using var response = await http.GetAsync(new Uri($"http://127.0.0.1:{port}/ready"));
            if (response.IsSuccessStatusCode)
            {
                return 0;
            }
            await Console.Error.WriteLineAsync($"healthcheck: /ready respondeu {(int)response.StatusCode}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            await Console.Error.WriteLineAsync($"healthcheck: {e.Message}");
        }
        return 1;
    }
}
