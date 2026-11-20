using System;
using System.Collections.Generic;
using System.Linq;

namespace SimpleWindowsTools.Screenshot;

/// <summary>
/// Order of the drawing tools on the editor toolbar. The user drags them around (in the editor or in the
/// settings card); key 1 picks the first tool, 2 the second and so on. Stored by name in
/// <see cref="Core.AppSettings.ScreenshotTools"/>; tools added in a later version are appended at the end.
/// </summary>
public static class ToolOrder
{
    public static readonly Tool[] Default = { Tool.Arrow, Tool.Rectangle, Tool.Line, Tool.Text, Tool.Blur, Tool.Pencil };

    /// <summary>Saved names → full order: unknown and repeated names dropped, missing tools appended.</summary>
    public static Tool[] Normalize(IEnumerable<string>? saved)
    {
        var list = new List<Tool>();
        foreach (var name in saved ?? Enumerable.Empty<string>())
            if (Enum.TryParse<Tool>(name, ignoreCase: true, out var t) && Enum.IsDefined(t) && !list.Contains(t))
                list.Add(t);
        foreach (var t in Default)
            if (!list.Contains(t)) list.Add(t);
        return list.ToArray();
    }

    public static List<string> ToNames(IEnumerable<Tool> order) => order.Select(t => t.ToString()).ToList();

    /// <summary>Tool for a number key 1–9 (1-based), or null.</summary>
    public static Tool? ForKey(Tool[] order, int number) =>
        number >= 1 && number <= order.Length ? order[number - 1] : null;

    /// <summary>Moves one tool to a new position and returns the new order.</summary>
    public static Tool[] Move(Tool[] order, Tool tool, int newIndex)
    {
        var list = order.ToList();
        list.Remove(tool);
        list.Insert(Math.Clamp(newIndex, 0, list.Count), tool);
        return list.ToArray();
    }

    /// <summary>Toolbar icon (24×24 path) and the tooltip string key ("{0}" = its number key).</summary>
    public static (string Icon, double Thickness, string HintKey) Look(Tool tool) => tool switch
    {
        Tool.Rectangle => ("M4,6 L20,6 L20,18 L4,18 Z", 1.8, "Sh.Rect"),
        Tool.Line => ("M5,19 L19,5", 1.8, "Sh.Line"),
        Tool.Text => ("M6,6 L18,6 M12,6 L12,19", 1.8, "Sh.Text"),
        Tool.Blur => ("M4,5 L20,5 L20,19 L4,19 Z M8.5,9.5 L8.5,9.5 M12,9.5 L12,9.5 M15.5,9.5 L15.5,9.5 M8.5,14.5 L8.5,14.5 M12,14.5 L12,14.5 M15.5,14.5 L15.5,14.5", 2.4, "Sh.Blur"),
        Tool.Pencil => ("M4,20 L5,15.5 L15.5,5 L19,8.5 L8.5,19 Z M13.5,7 L17,10.5", 1.8, "Sh.Pencil"),
        _ => ("M5,19 L19,5 M10,5 L19,5 L19,14", 1.8, "Sh.Arrow"),
    };
}
