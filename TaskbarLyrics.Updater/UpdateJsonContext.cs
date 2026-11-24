using System.Text.Json.Serialization;

namespace TaskbarLyrics.Updates;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(UpdateRequest))]
[JsonSerializable(typeof(UpdateTransaction.Journal))]
public sealed partial class UpdateJsonContext : JsonSerializerContext;
