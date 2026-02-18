namespace MTGFetchMAUI.Core;

/// <summary>
/// Represents a card's or deck's color identity as a set of Magic colors.
/// Port of TColorIdentity from MTGCore.pas.
/// </summary>
public struct ColorIdentity : IEquatable<ColorIdentity>
{
    private HashSet<MtgColor> _colors;

    private HashSet<MtgColor> Colors => _colors ??= [];

    public bool W
    {
        get => Colors.Contains(MtgColor.White);
        set { if (value) Colors.Add(MtgColor.White); else Colors.Remove(MtgColor.White); }
    }

    public bool U
    {
        get => Colors.Contains(MtgColor.Blue);
        set { if (value) Colors.Add(MtgColor.Blue); else Colors.Remove(MtgColor.Blue); }
    }

    public bool B
    {
        get => Colors.Contains(MtgColor.Black);
        set { if (value) Colors.Add(MtgColor.Black); else Colors.Remove(MtgColor.Black); }
    }

    public bool R
    {
        get => Colors.Contains(MtgColor.Red);
        set { if (value) Colors.Add(MtgColor.Red); else Colors.Remove(MtgColor.Red); }
    }

    public bool G
    {
        get => Colors.Contains(MtgColor.Green);
        set { if (value) Colors.Add(MtgColor.Green); else Colors.Remove(MtgColor.Green); }
    }

    public string AsString()
    {
        var result = "";
        foreach (MtgColor c in Enum.GetValues<MtgColor>())
        {
            if (Colors.Contains(c))
                result += c.ToChar();
        }
        return result;
    }

    public string[] ToColorArray()
    {
        var list = new List<string>();
        foreach (MtgColor c in Enum.GetValues<MtgColor>())
        {
            if (Colors.Contains(c))
                list.Add(c.ToChar().ToString());
        }
        return list.ToArray();
    }

    public int Count => Colors.Count;
    public bool IsColorless => Colors.Count == 0;
    public bool IsMonoColor => Colors.Count == 1;
    public bool IsMultiColor => Colors.Count > 1;

    public bool Contains(ColorIdentity other)
    {
        if (other._colors == null) return true;
        return other.Colors.IsSubsetOf(Colors);
    }

    public bool Intersects(ColorIdentity other)
    {
        if (other._colors == null || _colors == null) return false;
        return Colors.Overlaps(other.Colors);
    }

    public string[] GetMissingColors(ColorIdentity desired)
    {
        var missing = new List<string>();
        foreach (MtgColor c in Enum.GetValues<MtgColor>())
        {
            if (desired.Colors.Contains(c) && !Colors.Contains(c))
                missing.Add(c.ToChar().ToString());
        }
        return missing.ToArray();
    }

    public void Clear() => _colors?.Clear();

    public static ColorIdentity FromString(string? colors)
    {
        var result = new ColorIdentity();
        if (string.IsNullOrEmpty(colors)) return result;

        foreach (char c in colors)
        {
            char upper = char.ToUpper(c);
            foreach (MtgColor color in Enum.GetValues<MtgColor>())
            {
                if (color.ToChar() == upper)
                {
                    result.Colors.Add(color);
                    break;
                }
            }
        }
        return result;
    }

    public static ColorIdentity Empty => new();

    public static ColorIdentity AllColors
    {
        get
        {
            var result = new ColorIdentity();
            foreach (MtgColor c in Enum.GetValues<MtgColor>())
                result.Colors.Add(c);
            return result;
        }
    }

    public bool Equals(ColorIdentity other) => AsString() == other.AsString();
    public override bool Equals(object? obj) => obj is ColorIdentity ci && Equals(ci);
    public override int GetHashCode() => AsString().GetHashCode();
    public static bool operator ==(ColorIdentity left, ColorIdentity right) => left.Equals(right);
    public static bool operator !=(ColorIdentity left, ColorIdentity right) => !left.Equals(right);
}
