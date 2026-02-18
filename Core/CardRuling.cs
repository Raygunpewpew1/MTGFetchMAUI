namespace MTGFetchMAUI.Core;

/// <summary>
/// Individual ruling for a card.
/// Port of TCardRuling from MTGCore.pas.
/// </summary>
public record CardRuling(DateTime Date, string Text)
{
    public string GetFormattedDate() => Date.ToString("yyyy-MM-dd");
}
