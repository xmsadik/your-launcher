using System.Text;
using System.Text.Json;

namespace YourLauncher.Core.Plugins;

/// <summary>
/// Parses a plugin's stdout. Never throws and never returns partial garbage: a plugin that prints invalid
/// JSON, too much, or a wrong shape yields zero items, while a single bad row (no title, unknown action
/// type) is skipped and the good rows are kept.
/// </summary>
public static class PluginOutputParser
{
    public static IReadOnlyList<PluginItem> Parse(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout) || Encoding.UTF8.GetByteCount(stdout) > PluginProtocol.MaxOutputBytes)
        {
            return [];
        }

        PluginOutputDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize(stdout.TrimStart('﻿'), PluginJsonContext.Default.PluginOutputDto);
        }
        catch (JsonException)
        {
            return [];
        }

        if (dto?.Items is null)
        {
            return [];
        }

        var items = new List<PluginItem>();
        foreach (var raw in dto.Items)
        {
            if (items.Count >= PluginProtocol.MaxItems)
            {
                break;
            }

            if (ToItem(raw) is { } item)
            {
                items.Add(item);
            }
        }

        return items;
    }

    private static PluginItem? ToItem(PluginItemDto? raw)
    {
        if (raw is null || string.IsNullOrWhiteSpace(raw.Title) || raw.Action is not { } action)
        {
            return null;
        }

        var parsed = ToAction(action);
        return parsed is null
            ? null
            : new PluginItem(
                raw.Title.Trim(),
                raw.Subtitle?.Trim() ?? "",
                string.IsNullOrWhiteSpace(raw.Icon) ? null : raw.Icon.Trim(),
                parsed);
    }

    private static PluginAction? ToAction(PluginActionDto action)
    {
        IReadOnlyList<string> args = action.Args?.Where(a => a is not null).ToArray() ?? [];
        switch (action.Type?.Trim().ToLowerInvariant())
        {
            case "open" when !string.IsNullOrWhiteSpace(action.Target):
                return new PluginAction(PluginActionKind.Open, action.Target.Trim(), "", "", args);
            case "copy" when !string.IsNullOrEmpty(action.Text):
                return new PluginAction(PluginActionKind.Copy, "", action.Text, "", args);
            case "run" when !string.IsNullOrWhiteSpace(action.Command):
                return new PluginAction(PluginActionKind.Run, "", "", action.Command.Trim(), args);
            default:
                return null;
        }
    }
}
