namespace AetherVault.Core;

/// <summary>
/// Lightweight set metadata for filter pickers (code + display name).
/// </summary>
public record SetInfo(string Code, string Name)
{
    public override string ToString() => Name;
}
