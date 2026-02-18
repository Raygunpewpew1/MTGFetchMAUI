namespace MTGFetchMAUI.Core;

/// <summary>
/// Tracks the legality of a card across all MTG formats.
/// Port of TCardLegalities from MTGCore.pas.
/// </summary>
public class CardLegalities
{
    private readonly LegalityStatus[] _status;

    public CardLegalities()
    {
        _status = new LegalityStatus[Enum.GetValues<DeckFormat>().Length];
        Clear();
    }

    public LegalityStatus this[DeckFormat format]
    {
        get => _status[(int)format];
        set => _status[(int)format] = value;
    }

    public void Clear()
    {
        Array.Fill(_status, LegalityStatus.NotLegal);
    }

    public bool IsLegalInFormat(DeckFormat format) =>
        _status[(int)format] == LegalityStatus.Legal;

    public DeckFormat[] GetLegalFormats() => GetFormatsByStatus(LegalityStatus.Legal);
    public DeckFormat[] GetBannedFormats() => GetFormatsByStatus(LegalityStatus.Banned);

    private DeckFormat[] GetFormatsByStatus(LegalityStatus status)
    {
        var result = new List<DeckFormat>();
        foreach (DeckFormat fmt in Enum.GetValues<DeckFormat>())
        {
            if (_status[(int)fmt] == status)
                result.Add(fmt);
        }
        return result.ToArray();
    }
}
