namespace MTGFetchMAUI.Services;

/// <summary>
/// Singleton service that tracks the ordered list of card UUIDs from the current search
/// or collection view, enabling swipe-to-navigate between cards in CardDetailPage.
/// </summary>
public class CardGalleryContext
{
    private List<string> _uuids = [];
    private int _currentIndex = -1;

    /// <summary>True when a gallery context is set with more than one card.</summary>
    public bool HasContext => _uuids.Count > 1;

    /// <summary>Total number of cards in the current context.</summary>
    public int TotalCount => _uuids.Count;

    /// <summary>
    /// Sets the gallery context from the provided ordered UUID list and marks
    /// the card at <paramref name="currentUuid"/> as the current position.
    /// </summary>
    public void SetContext(IReadOnlyList<string> uuids, string currentUuid)
    {
        _uuids = [.. uuids];
        _currentIndex = _uuids.IndexOf(currentUuid);
    }

    /// <returns>UUID of the previous card, or null if already at the start.</returns>
    public string? GetPreviousUuid() => _currentIndex > 0 ? _uuids[_currentIndex - 1] : null;

    /// <returns>UUID of the next card, or null if already at the end.</returns>
    public string? GetNextUuid() => _currentIndex < _uuids.Count - 1 ? _uuids[_currentIndex + 1] : null;

    /// <summary>Moves the current position one step backward.</summary>
    public void MovePrevious() { if (_currentIndex > 0) _currentIndex--; }

    /// <summary>Moves the current position one step forward.</summary>
    public void MoveNext() { if (_currentIndex < _uuids.Count - 1) _currentIndex++; }

    /// <returns>Human-readable position string, e.g. "5 / 50", or empty if no context.</returns>
    public string GetPositionText() => HasContext ? $"{_currentIndex + 1} / {_uuids.Count}" : "";

    /// <summary>Clears the current gallery context.</summary>
    public void Clear()
    {
        _uuids = [];
        _currentIndex = -1;
    }
}
