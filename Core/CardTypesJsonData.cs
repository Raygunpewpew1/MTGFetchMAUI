using System.Text.Json;

namespace MTGFetchMAUI.Core;

/// <summary>
/// Embedded card type data from MTGJSON and parser.
/// Port of CardTypesJson.pas — the raw JSON is loaded from an embedded resource
/// or can be provided as a string.
/// </summary>
public static class CardTypesJsonData
{
    /// <summary>
    /// Parses the card types JSON (from MTGJSON CardTypes endpoint) into
    /// an MTGDataCollection.
    /// </summary>
    public static MTGDataCollection Parse(string json)
    {
        var result = new MTGDataCollection();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("meta", out var meta))
        {
            if (meta.TryGetProperty("version", out var ver))
                result.MetaVersion = ver.GetString() ?? "";
            if (meta.TryGetProperty("date", out var date) &&
                DateTime.TryParse(date.GetString(), out var dt))
                result.MetaDate = dt;
        }

        if (root.TryGetProperty("data", out var data))
        {
            foreach (var prop in data.EnumerateObject())
            {
                var info = new CardTypeInfo(prop.Name);

                if (prop.Value.TryGetProperty("subTypes", out var subs))
                {
                    foreach (var sub in subs.EnumerateArray())
                    {
                        var val = sub.GetString();
                        if (!string.IsNullOrEmpty(val))
                            info.SubTypes.Add(val);
                    }
                }

                if (prop.Value.TryGetProperty("superTypes", out var supers))
                {
                    foreach (var sup in supers.EnumerateArray())
                    {
                        var val = sup.GetString();
                        if (!string.IsNullOrEmpty(val))
                            info.SuperTypes.Add(val);
                    }
                }

                result.CardTypes.Add(info);
            }
        }

        return result;
    }
}
