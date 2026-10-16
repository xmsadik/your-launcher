namespace YourLauncher.Core.Json;

/// <summary>
/// An earlier config name of an enum member, still accepted on read by <see cref="JsonCamelCaseEnumConverter{T}"/>
/// (writes always use the current name), so renaming a member never breaks a config written by an older version.
/// </summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
public sealed class JsonEnumAliasAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}
