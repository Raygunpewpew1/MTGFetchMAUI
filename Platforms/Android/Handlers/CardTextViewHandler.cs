using Android.Graphics;
using Android.Text;
using Android.Text.Style;
using Android.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using MTGFetchMAUI.Controls;
using System.Text.RegularExpressions;

namespace MTGFetchMAUI.Platforms.Android.Handlers;

public class CardTextViewHandler : ViewHandler<CardTextView, TextView>
{
    private static readonly Regex SymbolPattern = new(@"\{([^}]+)\}", RegexOptions.Compiled);

    private static readonly string[] DefaultKeywords =
    [
        "Flying", "First strike", "Double strike", "Deathtouch", "Haste",
        "Hexproof", "Indestructible", "Lifelink", "Menace", "Reach",
        "Trample", "Vigilance", "Flash", "Defender", "Ward", "Shroud",
        "Fear", "Intimidate", "Prowess", "Convoke", "Delve",
        "Equip", "Enchant", "Exile", "Transform", "Mill",
        "Scry", "Surveil", "Protection", "Flashback", "Kicker",
        "Cycling", "Cascade", "Infect", "Toxic"
    ];

    private static Regex? _keywordRegex;
    private static readonly HashSet<string> _keywords = new(DefaultKeywords, StringComparer.OrdinalIgnoreCase);

    public static IPropertyMapper<CardTextView, CardTextViewHandler> Mapper =
        new PropertyMapper<CardTextView, CardTextViewHandler>(ViewHandler.ViewMapper)
        {
            [nameof(CardTextView.CardText)] = MapCardText,
            [nameof(CardTextView.TextColor)] = MapTextColor,
            [nameof(CardTextView.TextSize)] = MapTextSize,
            [nameof(CardTextView.KeywordColor)] = MapCardText,
            [nameof(CardTextView.SymbolSize)] = MapCardText
        };

    public CardTextViewHandler() : base(Mapper)
    {
        EnsureRegex();
    }

    private static void EnsureRegex()
    {
        if (_keywordRegex != null) return;
        var sorted = _keywords.OrderByDescending(k => k.Length);
        var pattern = @"\b(" + string.Join("|", sorted.Select(Regex.Escape)) + @")\b";
        _keywordRegex = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    protected override TextView CreatePlatformView()
    {
        var textView = new TextView(Context);
        textView.SetLineSpacing(0f, 1.2f); // Default line spacing
        return textView;
    }

    private static void MapTextColor(CardTextViewHandler handler, CardTextView view)
    {
        handler.PlatformView.SetTextColor(view.TextColor.ToPlatform());
    }

    private static void MapTextSize(CardTextViewHandler handler, CardTextView view)
    {
        handler.PlatformView.TextSize = (float)view.TextSize;
    }

    private static void MapCardText(CardTextViewHandler handler, CardTextView view)
    {
        var context = handler.Context;
        if (context?.Resources == null) return;
        var resources = context.Resources;

        if (string.IsNullOrEmpty(view.CardText))
        {
            handler.PlatformView.TextFormatted = null;
            return;
        }

        SpannableStringBuilder ssb = new SpannableStringBuilder();
        string text = view.CardText;

        int lastIndex = 0;
        foreach (Match match in SymbolPattern.Matches(text))
        {
            if (match.Index > lastIndex)
            {
                string textPart = text.Substring(lastIndex, match.Index - lastIndex);
                AppendTextWithKeywords(ssb, textPart, view.KeywordColor);
            }

            string symbol = match.Groups[1].Value;
            string resourceName = "mana_" + symbol.Replace("/", "_").ToLowerInvariant();

            // Get Drawable
            int resId = 0;
            try
            {
                resId = resources.GetIdentifier(resourceName, "drawable", context.PackageName);
            }
            catch { }

            bool added = false;
            if (resId != 0)
            {
                var drawable = context.GetDrawable(resId);
                if (drawable != null)
                {
                    // Convert SymbolSize (DIP) to pixels
                    float sizeDip = (float)view.SymbolSize;
                    float scale = resources.DisplayMetrics?.Density ?? 1.0f;
                    int sizePx = (int)(sizeDip * scale + 0.5f);

                    drawable.SetBounds(0, 0, sizePx, sizePx);

                    var imageSpan = new ImageSpan(drawable, SpanAlign.Baseline);
                    int start = ssb.Length();
                    ssb.Append(" ");
                    ssb.SetSpan(imageSpan, start, start + 1, SpanTypes.ExclusiveExclusive);
                    added = true;
                }
            }

            if (!added)
            {
                ssb.Append(match.Value);
            }

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            AppendTextWithKeywords(ssb, text.Substring(lastIndex), view.KeywordColor);
        }

        handler.PlatformView.TextFormatted = ssb;
    }

    private static void AppendTextWithKeywords(SpannableStringBuilder ssb, string text, Microsoft.Maui.Graphics.Color keywordColor)
    {
        if (_keywordRegex == null)
        {
            ssb.Append(text);
            return;
        }

        int lastIndex = 0;
        foreach (Match match in _keywordRegex.Matches(text))
        {
            if (match.Index > lastIndex)
            {
                ssb.Append(text.Substring(lastIndex, match.Index - lastIndex));
            }

            int start = ssb.Length();
            ssb.Append(match.Value);
            int end = ssb.Length();

            ssb.SetSpan(new StyleSpan(TypefaceStyle.Bold), start, end, SpanTypes.ExclusiveExclusive);
            ssb.SetSpan(new ForegroundColorSpan(keywordColor.ToPlatform()), start, end, SpanTypes.ExclusiveExclusive);

            lastIndex = match.Index + match.Length;
        }

        if (lastIndex < text.Length)
        {
            ssb.Append(text.Substring(lastIndex));
        }
    }
}
