using System.Globalization;

namespace AetherVault.Core;

/// <summary>
/// Set row for browse/search list: code, display name, counts, and optional parent set (MTGJSON <c>parentCode</c>).
/// </summary>
public sealed class SetBrowseRow
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? ReleaseDate { get; set; }
    public string? ParentCode { get; set; }
    public string? ParentSetName { get; set; }
    /// <summary>SQLite / MTGJSON: 1 when <c>isPartialPreview</c> is true.</summary>
    public int IsPartialPreview { get; set; }
    public int CardCount { get; set; }

    public bool IsPreviewSet => IsPartialPreview != 0;

    public string CardCountLabel => CardCount == 1 ? "1 card" : $"{CardCount} cards";

    /// <summary>Short release label for list rows (ISO date when parseable).</summary>
    public string ReleaseDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ReleaseDate))
                return "—";
            if (DateTime.TryParse(ReleaseDate, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
                return d.ToString("d", CultureInfo.CurrentCulture);
            return ReleaseDate;
        }
    }

    /// <summary>Optional second line when this row is a child printing (e.g. Commander subset).</summary>
    public string? ParentHint =>
        string.IsNullOrWhiteSpace(ParentCode)
            ? null
            : string.IsNullOrWhiteSpace(ParentSetName)
                ? $"Subset · {ParentCode}"
                : $"Subset · {ParentSetName}";
}
