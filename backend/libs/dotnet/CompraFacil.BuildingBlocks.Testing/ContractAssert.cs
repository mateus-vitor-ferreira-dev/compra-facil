using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using Xunit;
using Yaml2JsonNode;

namespace CompraFacil.BuildingBlocks.Testing;

/// <summary>
/// Teste de contrato: valida um JSON contra um schema dos contratos (contracts/openapi e
/// contracts/asyncapi), seguindo os $ref entre arquivos — o mesmo que o catalog faz em Go.
/// Mudou o contrato ou o código sem mudar o outro, o teste quebra.
/// </summary>
public static class ContractAssert
{
    private static readonly Lazy<(SchemaRegistry Registry, string Root)> s_contracts = new(Load);

    // os contratos têm palavras que não são JSON Schema (asyncapi, openapi, info, channels...)
    private static readonly Dialect s_dialect = Dialect.Draft202012.With([], allowUnknownKeywords: true);

    private static readonly EvaluationOptions s_evaluation = new()
    {
        RequireFormatValidation = true, // uuid, date-time, email
        OutputFormat = OutputFormat.List,
    };

    /// <summary>Payload de um evento contra a mensagem do AsyncAPI (ex.: orders.v1.yaml, OrderPlaced).</summary>
    public static void AssertEvent(string asyncApiFile, string message, string json) =>
        AssertMatches($"asyncapi/{asyncApiFile}", $"/components/messages/{message}/payload", json);

    /// <summary>Corpo HTTP contra um schema do OpenAPI (ex.: orders.v1.yaml, /components/schemas/Order).</summary>
    public static void AssertSchema(string openApiFile, string jsonPointer, string json) =>
        AssertMatches($"openapi/{openApiFile}", jsonPointer, json);

    private static void AssertMatches(string file, string jsonPointer, string json)
    {
        var (registry, root) = s_contracts.Value;
        var target = new Uri(Path.Combine(root, file)).AbsoluteUri + "#" + jsonPointer;
        using var reference = JsonDocument.Parse(JsonSerializer.Serialize(new Dictionary<string, string> { ["$ref"] = target }));
        var schema = JsonSchema.Build(reference.RootElement,
            new BuildOptions { SchemaRegistry = registry, Dialect = s_dialect }, new Uri($"urn:contract:{Guid.NewGuid()}"));

        using var instance = JsonDocument.Parse(json);
        var result = schema.Evaluate(instance.RootElement, s_evaluation);
        if (!result.IsValid)
        {
            var errors = (result.Details ?? [])
                .Where(d => d.Errors is { Count: > 0 })
                .SelectMany(d => d.Errors!.Select(e => $"  {d.InstanceLocation} {e.Key}: {e.Value}"));
            Assert.Fail($"não segue {file}#{jsonPointer}:\n{string.Join('\n', errors)}\n{json}");
        }
    }

    private static (SchemaRegistry, string) Load()
    {
        var root = FindContractsDir();
        var registry = new SchemaRegistry();
        foreach (var file in Directory.EnumerateFiles(root, "*.yaml", SearchOption.AllDirectories))
        {
            var uri = new Uri(file);
            var node = YamlSerializer.Parse(File.ReadAllText(file)).ToJsonNode().First()!;
            AbsolutizeRefs(node, uri);
            registry.Register(uri, new JsonElementBaseDocument(JsonSerializer.SerializeToElement(node), uri));
        }
        return (registry, root);
    }

    // O JsonSchema.Net não aplica $ref relativo para outro arquivo (../openapi/...) a partir de um
    // documento registrado: o subschema passa a aceitar tudo, sem erro. Reescrever cada $ref como
    // URI absoluta resolve. O .NET codifica '#' em URIs file: (%23), por isso o fragmento é à parte.
    private static void AbsolutizeRefs(JsonNode node, Uri document)
    {
        switch (node)
        {
            case JsonObject obj:
                if (obj["$ref"] is JsonValue value && value.TryGetValue<string>(out var reference))
                {
                    var hash = reference.IndexOf('#', StringComparison.Ordinal);
                    var path = hash < 0 ? reference : reference[..hash];
                    var fragment = hash < 0 ? "" : reference[hash..];
                    obj["$ref"] = (path.Length == 0 ? document.AbsoluteUri : new Uri(document, path).AbsoluteUri) + fragment;
                }
                foreach (var (_, child) in obj.ToList())
                {
                    if (child is not null)
                    {
                        AbsolutizeRefs(child, document);
                    }
                }
                break;
            case JsonArray array:
                foreach (var child in array)
                {
                    if (child is not null)
                    {
                        AbsolutizeRefs(child, document);
                    }
                }
                break;
        }
    }

    private static string FindContractsDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "contracts");
            if (Directory.Exists(Path.Combine(candidate, "asyncapi")))
            {
                return candidate;
            }
        }
        throw new DirectoryNotFoundException("pasta contracts/ não encontrada acima de " + AppContext.BaseDirectory);
    }
}
