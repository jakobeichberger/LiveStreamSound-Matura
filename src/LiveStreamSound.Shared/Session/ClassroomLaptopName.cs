using System.Text.RegularExpressions;

namespace LiveStreamSound.Shared.Session;

public enum LaptopCategory
{
    /// <summary>Standard classroom laptop (e.g. HP-KB-017, LEN-KB-065-2).</summary>
    Klassenraum,
    /// <summary>Workshop machine (e.g. HP-WERK-038).</summary>
    Werkstatt,
    /// <summary>Room-code style hostname (e.g. R072).</summary>
    Raum,
    /// <summary>Any hostname that does not match the school schema.</summary>
    Sonstige,
}

/// <summary>
/// Parses the school's laptop naming convention into a room number + category + optional device index.
///
/// Real-world examples from the deployment:
///   HP-KB-017         → Klassenraum, room "017"
///   HP-KB-018-2       → Klassenraum, room "018", device 2
///   LEN-KB-065-2      → Klassenraum, room "065", device 2
///   HP-WERK-038       → Werkstatt, room "038"
///   R072              → Raum, room "072"
///   DESKTOP-A1B2C3    → Sonstige (not parseable)
/// </summary>
public static class ClassroomLaptopName
{
    private static readonly Regex FullPattern = new(
        @"^(?<vendor>[A-Za-z]+)-(?<category>[A-Za-z]+)-(?<room>\d{2,4})(?:-(?<device>\d+))?$",
        RegexOptions.Compiled);

    private static readonly Regex ShortPattern = new(
        @"^R(?<room>\d{2,4})$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public sealed record Parsed(
        LaptopCategory Category,
        string Room,
        int? DeviceIndex,
        string? Vendor,
        string OriginalHostName);

    public static Parsed Classify(string hostName)
    {
        if (string.IsNullOrWhiteSpace(hostName))
            return new Parsed(LaptopCategory.Sonstige, "", null, null, hostName ?? "");

        var full = FullPattern.Match(hostName);
        if (full.Success)
        {
            var categoryToken = full.Groups["category"].Value.ToUpperInvariant();
            var cat = categoryToken switch
            {
                "KB" => LaptopCategory.Klassenraum,
                "WERK" or "WERKSTATT" => LaptopCategory.Werkstatt,
                _ => LaptopCategory.Sonstige,
            };
            if (cat == LaptopCategory.Sonstige)
                return new Parsed(LaptopCategory.Sonstige, "", null,
                    full.Groups["vendor"].Value, hostName);

            int? device = full.Groups["device"].Success ? int.Parse(full.Groups["device"].Value) : null;
            return new Parsed(cat, full.Groups["room"].Value, device,
                full.Groups["vendor"].Value, hostName);
        }

        var shortMatch = ShortPattern.Match(hostName);
        if (shortMatch.Success)
        {
            return new Parsed(LaptopCategory.Raum, shortMatch.Groups["room"].Value,
                null, null, hostName);
        }

        return new Parsed(LaptopCategory.Sonstige, "", null, null, hostName);
    }

    public static bool TryParse(string hostName, out Parsed parsed)
    {
        parsed = Classify(hostName);
        return parsed.Category != LaptopCategory.Sonstige;
    }

    /// <summary>
    /// Returns a German friendly display name. Unrecognized hostnames are returned unchanged.
    /// </summary>
    public static string FriendlyName(string hostName)
    {
        var p = Classify(hostName);
        return p.Category switch
        {
            LaptopCategory.Klassenraum when p.DeviceIndex.HasValue => $"Raum {p.Room} (Gerät {p.DeviceIndex})",
            LaptopCategory.Klassenraum => $"Raum {p.Room}",
            LaptopCategory.Werkstatt => $"Werkstatt {p.Room}",
            LaptopCategory.Raum => $"Raum {p.Room}",
            _ => hostName,
        };
    }

    /// <summary>
    /// Returns an English friendly display name.
    /// </summary>
    public static string FriendlyNameEnglish(string hostName)
    {
        var p = Classify(hostName);
        return p.Category switch
        {
            LaptopCategory.Klassenraum when p.DeviceIndex.HasValue => $"Room {p.Room} (device {p.DeviceIndex})",
            LaptopCategory.Klassenraum => $"Room {p.Room}",
            LaptopCategory.Werkstatt => $"Workshop {p.Room}",
            LaptopCategory.Raum => $"Room {p.Room}",
            _ => hostName,
        };
    }

    /// <summary>
    /// Localized display label for a <see cref="LaptopCategory"/> group header.
    /// </summary>
    public static string CategoryLabel(LaptopCategory category, bool german) =>
        (category, german) switch
        {
            (LaptopCategory.Klassenraum, true) => "Klassenräume",
            (LaptopCategory.Klassenraum, false) => "Classrooms",
            (LaptopCategory.Werkstatt, true) => "Werkstatt",
            (LaptopCategory.Werkstatt, false) => "Workshop",
            (LaptopCategory.Raum, true) => "Räume",
            (LaptopCategory.Raum, false) => "Rooms",
            (LaptopCategory.Sonstige, true) => "Sonstige Geräte",
            _ => "Other devices",
        };
}
