using System.Text.Json;
using System.Text.Json.Serialization;

namespace YourLauncher.Core.Json;

/// <summary>
/// Serializes enums as camelCase strings (e.g. <c>ShellKind.Pwsh</c> -> "pwsh") and reads them back
/// case-insensitively. A small hand-rolled converter is used instead of the built-in
/// <see cref="JsonStringEnumConverter"/> because the built-in converter's naming-policy constructor
/// argument cannot be supplied through a <see cref="JsonConverterAttribute"/> (which only accepts a
/// parameterless converter type), and this needs to work with the source-generated serializer context.
/// </summary>
public sealed class JsonCamelCaseEnumConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var text = reader.GetString();
        if (text is null)
        {
            throw new JsonException($"Expected a string value for enum {typeof(T).Name}.");
        }

        foreach (var value in Enum.GetValues<T>())
        {
            if (string.Equals(ToCamelCase(value.ToString()), text, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        throw new JsonException($"Unknown value '{text}' for enum {typeof(T).Name}.");
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(ToCamelCase(value.ToString()));
    }

    private static string ToCamelCase(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name[1..];
}
