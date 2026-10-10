using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;

namespace FocusGuard.Desktop;

/// <summary>
/// The fourteen Catppuccin accents, paired across flavours.
/// <para>
/// A profile stores a single accent hex, but the same hex cannot serve both flavours:
/// Mocha's accents are pastels tuned to sit on a near-black base, and on Latte's paper
/// they wash out to nothing. So the stored hex is treated as a <em>name</em> — the
/// nearest accent in either flavour — and the flavour currently in use supplies the
/// value that actually gets painted. A profile keeps its identity ("this one is the
/// mauve profile") across a theme switch instead of keeping a literal color that only
/// works in one of them.
/// </para>
/// </summary>
internal static class CatppuccinAccents
{
    internal readonly record struct Accent(string Name, string Mocha, string Latte);

    /// <summary>Ordered around the wheel so a swatch grid reads as a gradient.</summary>
    internal static readonly IReadOnlyList<Accent> All =
    [
        new("rosewater", "#F5E0DC", "#DC8A78"),
        new("flamingo", "#F2CDCD", "#DD7878"),
        new("pink", "#F5C2E7", "#EA76CB"),
        new("mauve", "#CBA6F7", "#8839EF"),
        new("red", "#F38BA8", "#D20F39"),
        new("maroon", "#EBA0AC", "#E64553"),
        new("peach", "#FAB387", "#FE640B"),
        new("yellow", "#F9E2AF", "#DF8E1D"),
        new("green", "#A6E3A1", "#40A02B"),
        new("teal", "#94E2D5", "#179299"),
        new("sky", "#89DCEB", "#04A5E5"),
        new("sapphire", "#74C7EC", "#209FB5"),
        new("blue", "#89B4FA", "#1E66F5"),
        new("lavender", "#B4BEFE", "#7287FD")
    ];

    /// <summary>The flavour-neutral name a profile is really choosing.</summary>
    internal const string DefaultName = "mauve";

    /// <summary>Hexes offered in the profile colour picker. Mocha is the canonical form.</summary>
    internal static IReadOnlyList<string> PickerHexes { get; } = All.Select(accent => accent.Mocha).ToArray();

    internal static string ResolveHex(string? storedHex, bool isLightTheme)
    {
        var accent = Match(storedHex);
        return isLightTheme ? accent.Latte : accent.Mocha;
    }

    /// <summary>
    /// Resolves a stored hex to an accent. Exact matches in either flavour win; anything
    /// else — a colour from the palette this app shipped before Catppuccin, or a hand
    /// edited settings file — falls back to the nearest accent by RGB distance, so an
    /// existing profile keeps a recognisably similar colour rather than snapping to mauve.
    /// </summary>
    internal static Accent Match(string? storedHex)
    {
        if (string.IsNullOrWhiteSpace(storedHex))
        {
            return All.Single(accent => accent.Name == DefaultName);
        }

        foreach (var accent in All)
        {
            if (string.Equals(accent.Mocha, storedHex, StringComparison.OrdinalIgnoreCase)
                || string.Equals(accent.Latte, storedHex, StringComparison.OrdinalIgnoreCase))
            {
                return accent;
            }
        }

        if (!Color.TryParse(storedHex, out var stored))
        {
            return All.Single(accent => accent.Name == DefaultName);
        }

        return All.MinBy(accent => Distance(stored, Color.Parse(accent.Mocha)));
    }

    private static double Distance(Color left, Color right)
    {
        // Squared distance is enough for a nearest-neighbour pick; skipping the square
        // root keeps the ordering identical.
        double red = left.R - right.R;
        double green = left.G - right.G;
        double blue = left.B - right.B;
        return (red * red) + (green * green) + (blue * blue);
    }
}
