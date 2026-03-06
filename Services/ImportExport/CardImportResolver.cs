using AetherVault.Data;
using AetherVault.Models;

namespace AetherVault.Services.ImportExport;

internal sealed class CardImportResolver(ICardRepository cardRepo)
{
    private readonly ICardRepository _cardRepo = cardRepo;

    internal async Task<Session> CreateSessionAsync()
    {
        var lookupRows = await _cardRepo.GetImportLookupRowsAsync();
        var index = BuildLookupIndex(lookupRows);
        return new Session(_cardRepo, index);
    }

    internal sealed class Session
    {
        private readonly ICardRepository _cardRepo;
        private readonly ImportLookupIndex _index;

        // Fallback caches store both hits and misses (null) to avoid repeated misses.
        private readonly Dictionary<string, string?> _scryfallFallbackCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _setNumberFallbackCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _nameSetFallbackCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _nameExactFallbackCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string?> _namePartialFallbackCache = new(StringComparer.OrdinalIgnoreCase);

        internal Session(ICardRepository cardRepo, ImportLookupIndex index)
        {
            _cardRepo = cardRepo;
            _index = index;
        }

        internal string? ResolveFromLookup(string? scryfallId, string? set, string? number, string? name)
        {
            if (!string.IsNullOrWhiteSpace(scryfallId) &&
                _index.ByScryfallId.TryGetValue(NormalizeKey(scryfallId), out var scryfallUuid))
            {
                return scryfallUuid;
            }

            var setNumberKey = BuildSetNumberKey(set, number);
            if (!string.IsNullOrEmpty(setNumberKey) &&
                _index.BySetNumber.TryGetValue(setNumberKey, out var setNumberUuid))
            {
                return setNumberUuid;
            }

            var normalizedName = NormalizeKey(name);
            if (string.IsNullOrEmpty(normalizedName) ||
                !_index.ByName.TryGetValue(normalizedName, out var candidates) ||
                candidates.Count == 0)
            {
                return null;
            }

            var normalizedSet = NormalizeKey(set);
            if (!string.IsNullOrEmpty(normalizedSet))
            {
                for (int i = 0; i < candidates.Count; i++)
                {
                    var candidate = candidates[i];
                    if (string.Equals(NormalizeKey(candidate.SetCode), normalizedSet, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(NormalizeKey(candidate.SetName), normalizedSet, StringComparison.OrdinalIgnoreCase))
                    {
                        return candidate.UUID;
                    }
                }
            }

            return candidates[0].UUID;
        }

        internal async Task<string?> ResolveFromFallbackAsync(string? scryfallId, string? set, string? number, string? name)
        {
            if (!string.IsNullOrWhiteSpace(scryfallId))
            {
                var cacheKey = NormalizeKey(scryfallId);
                if (!_scryfallFallbackCache.TryGetValue(cacheKey, out var cachedUuid))
                {
                    var helper = _cardRepo.CreateSearchHelper();
                    helper.SearchCards(includeTokens: true).WhereScryfallId(cacheKey).Limit(1);
                    var matches = await _cardRepo.SearchCardsAdvancedAsync(helper);
                    cachedUuid = matches.Length > 0 ? matches[0].UUID : null;
                    _scryfallFallbackCache[cacheKey] = cachedUuid;
                }

                if (!string.IsNullOrEmpty(cachedUuid))
                {
                    return cachedUuid;
                }
            }

            var setNumberKey = BuildSetNumberKey(set, number);
            if (!string.IsNullOrEmpty(setNumberKey))
            {
                if (!_setNumberFallbackCache.TryGetValue(setNumberKey, out var cachedUuid))
                {
                    var helper = _cardRepo.CreateSearchHelper();
                    helper.SearchCards(includeTokens: true).WhereSet(set!).WhereNumber(number!).Limit(1);
                    var matches = await _cardRepo.SearchCardsAdvancedAsync(helper);
                    cachedUuid = matches.Length > 0 ? matches[0].UUID : null;
                    _setNumberFallbackCache[setNumberKey] = cachedUuid;
                }

                if (!string.IsNullOrEmpty(cachedUuid))
                {
                    return cachedUuid;
                }
            }

            var normalizedName = NormalizeKey(name);
            if (!string.IsNullOrEmpty(normalizedName) && !string.IsNullOrWhiteSpace(set))
            {
                var nameSetKey = $"{normalizedName}|{NormalizeKey(set)}";
                if (!_nameSetFallbackCache.TryGetValue(nameSetKey, out var cachedUuid))
                {
                    var helper = _cardRepo.CreateSearchHelper();
                    helper.SearchCards(includeTokens: true).WhereNameEquals(name!).WherePrimarySideOnly().Limit(100);
                    var matches = await _cardRepo.SearchCardsAdvancedAsync(helper);

                    Card? chosen = null;
                    for (int i = 0; i < matches.Length; i++)
                    {
                        var card = matches[i];
                        if (card.SetCode.Equals(set, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrWhiteSpace(card.SetName) && card.SetName.Equals(set, StringComparison.OrdinalIgnoreCase)))
                        {
                            chosen = card;
                            break;
                        }
                    }

                    chosen ??= matches.FirstOrDefault();
                    cachedUuid = chosen?.UUID;
                    _nameSetFallbackCache[nameSetKey] = cachedUuid;
                }

                if (!string.IsNullOrEmpty(cachedUuid))
                {
                    return cachedUuid;
                }
            }

            if (!string.IsNullOrEmpty(normalizedName))
            {
                if (!_nameExactFallbackCache.TryGetValue(normalizedName, out var cachedUuid))
                {
                    var helper = _cardRepo.CreateSearchHelper();
                    helper.SearchCards(includeTokens: true).WhereNameEquals(name!).WherePrimarySideOnly().Limit(1);
                    var matches = await _cardRepo.SearchCardsAdvancedAsync(helper);
                    cachedUuid = matches.Length > 0 ? matches[0].UUID : null;
                    _nameExactFallbackCache[normalizedName] = cachedUuid;
                }

                if (!string.IsNullOrEmpty(cachedUuid))
                {
                    return cachedUuid;
                }

                if (!_namePartialFallbackCache.TryGetValue(normalizedName, out cachedUuid))
                {
                    var helper = _cardRepo.CreateSearchHelper();
                    helper.SearchCards(includeTokens: true).WhereNameContains(name!).WherePrimarySideOnly().Limit(50);
                    var candidates = await _cardRepo.SearchCardsAdvancedAsync(helper);

                    Card? chosen = null;
                    for (int i = 0; i < candidates.Length; i++)
                    {
                        if (candidates[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                        {
                            chosen = candidates[i];
                            break;
                        }
                    }

                    chosen ??= candidates.FirstOrDefault();
                    cachedUuid = chosen?.UUID;
                    _namePartialFallbackCache[normalizedName] = cachedUuid;
                }

                if (!string.IsNullOrEmpty(cachedUuid))
                {
                    return cachedUuid;
                }
            }

            return null;
        }
    }

    internal sealed record ImportCandidate(string UUID, string SetCode, string? SetName);

    internal sealed class ImportLookupIndex
    {
        public Dictionary<string, string> ByScryfallId { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> BySetNumber { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<ImportCandidate>> ByName { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static ImportLookupIndex BuildLookupIndex(IReadOnlyList<ImportLookupRow> rows)
    {
        var index = new ImportLookupIndex();

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.UUID))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(row.ScryfallId))
            {
                index.ByScryfallId.TryAdd(NormalizeKey(row.ScryfallId), row.UUID);
            }

            if (!string.IsNullOrWhiteSpace(row.Number))
            {
                var setCodeKey = BuildSetNumberKey(row.SetCode, row.Number);
                if (!string.IsNullOrEmpty(setCodeKey))
                {
                    index.BySetNumber.TryAdd(setCodeKey, row.UUID);
                }

                var setNameKey = BuildSetNumberKey(row.SetName, row.Number);
                if (!string.IsNullOrEmpty(setNameKey))
                {
                    index.BySetNumber.TryAdd(setNameKey, row.UUID);
                }
            }

            var candidate = new ImportCandidate(row.UUID, row.SetCode, row.SetName);
            AddNameCandidate(index.ByName, row.Name, candidate);
            AddNameCandidate(index.ByName, row.FaceName, candidate);
        }

        return index;
    }

    private static void AddNameCandidate(
        Dictionary<string, List<ImportCandidate>> byName,
        string? name,
        ImportCandidate candidate)
    {
        var normalized = NormalizeKey(name);
        if (string.IsNullOrEmpty(normalized))
        {
            return;
        }

        if (!byName.TryGetValue(normalized, out var candidates))
        {
            byName[normalized] = [candidate];
            return;
        }

        for (int i = 0; i < candidates.Count; i++)
        {
            if (candidates[i].UUID == candidate.UUID)
            {
                return;
            }
        }

        candidates.Add(candidate);
    }

    private static string NormalizeKey(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "" : value.Trim();

    private static string BuildSetNumberKey(string? set, string? number)
    {
        var normalizedSet = NormalizeKey(set);
        var normalizedNumber = NormalizeKey(number);
        if (string.IsNullOrEmpty(normalizedSet) || string.IsNullOrEmpty(normalizedNumber))
        {
            return "";
        }

        return $"{normalizedSet}|{normalizedNumber}";
    }
}

