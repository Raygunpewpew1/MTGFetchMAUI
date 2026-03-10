using AetherVault.Core.Layout;
using AetherVault.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Diagnostics;

namespace AetherVault.Controls;

/// <summary>
/// Owns all Skia resources and card-drawing logic for CardGrid.
/// Pure renderer: reads images via a delegate, triggers no side effects.
/// </summary>
internal sealed class CardGridRenderer : IDisposable
{
    private readonly SKCanvasView _canvas;
    private readonly Func<string, SKImage?> _getImage;

    // Shimmer animation
    private float _shimmerPhase;
    private readonly Stopwatch _animationStopwatch = Stopwatch.StartNew();
    private long _lastFrameTime;

    // Cached paints / fonts
    private SKPaint? _bgPaint;
    private SKPaint? _textPaint;
    private SKFont? _textFont;
    private SKPaint? _pricePaint;
    private SKFont? _priceFont;
    private SKPaint? _shimmerBasePaint;
    private SKPaint? _badgeBgPaint;
    private SKPaint? _badgeTextPaint;
    private SKFont? _badgeFont;
    private SKRoundRect? _cardRoundRect;
    private SKRoundRect? _imageRoundRect;
    private SKPaint? _secondaryTextPaint;
    private SKFont? _secondaryTextFont;
    private SKPaint? _separatorPaint;
    private SKPaint? _dimPaint;
    private SKPaint? _shadowPaint;
    private SKPaint? _borderPaint;
    private SKPaint? _chipBgPaint;
    private SKPaint? _shimmerPaint;
    private SKShader? _shimmerShader;

    // Theme background (matches App Background #121212) so empty/zero-size canvas never shows black.
    private static readonly SKColor ThemeBackground = new SKColor(0x12, 0x12, 0x12);

    // List view thumbnail dimensions
    private const float ListImgWidth = 55f;
    private const float ListImgHeight = ListImgWidth * 1.3968f; // ≈ 76.8px

    /// <param name="canvas">Canvas to invalidate after sizing changes.</param>
    /// <param name="getImage">Returns a cached SKImage for the given cache key, or null.</param>
    public CardGridRenderer(SKCanvasView canvas, Func<string, SKImage?> getImage)
    {
        _canvas = canvas;
        _getImage = getImage;
        _lastFrameTime = _animationStopwatch.ElapsedMilliseconds;
    }

    public void EnsureResources()
    {
        _bgPaint ??= new SKPaint { Color = new SKColor(30, 30, 30), IsAntialias = true };
        _textPaint ??= new SKPaint { Color = SKColors.White, IsAntialias = true };
        _textFont ??= new SKFont(SKTypeface.FromFamilyName("CrimsonText-Regular", SKFontStyle.Normal), 12f) { Subpixel = true };
        _pricePaint ??= new SKPaint { Color = SKColors.LightGreen, IsAntialias = true };
        _priceFont ??= new SKFont(SKTypeface.FromFamilyName("CrimsonText-Regular", SKFontStyle.Normal), 8.5f) { Subpixel = true };
        _badgeBgPaint ??= new SKPaint { IsAntialias = true, Color = new SKColor(220, 50, 50) };
        _badgeTextPaint ??= new SKPaint { IsAntialias = true, Color = SKColors.White };
        _badgeFont ??= new SKFont(SKTypeface.FromFamilyName("CrimsonText-Bold", SKFontStyle.Bold), 11f) { Subpixel = true };
        _shimmerBasePaint ??= new SKPaint { Color = new SKColor(40, 40, 40) };
        _secondaryTextPaint ??= new SKPaint { Color = new SKColor(160, 160, 160), IsAntialias = true };
        _secondaryTextFont ??= new SKFont(SKTypeface.FromFamilyName("CrimsonText-Regular", SKFontStyle.Normal), 11f) { Subpixel = true };
        _separatorPaint ??= new SKPaint { Color = new SKColor(50, 50, 50) };
        _dimPaint ??= new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 160) };
        _shadowPaint ??= new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 140), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10f) };
        _borderPaint ??= new SKPaint { IsAntialias = true, Color = new SKColor(100, 200, 255, 220), Style = SKPaintStyle.Stroke, StrokeWidth = 3f };
        _chipBgPaint ??= new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 185) };
        _shimmerPaint ??= new SKPaint { IsAntialias = true };
        _cardRoundRect ??= new SKRoundRect();
        _imageRoundRect ??= new SKRoundRect();
        // Cached gradient for shimmer (0..1 x, band in middle); phase applied via local matrix per draw.
        _shimmerShader ??= SKShader.CreateLinearGradient(
            new SKPoint(0f, 0f),
            new SKPoint(1f, 0f),
            [SKColors.Transparent, new SKColor(255, 255, 255, 55), SKColors.Transparent],
            [0f, 0.5f, 1f],
            SKShaderTileMode.Clamp);
    }

    /// <summary>Rebuilds size-dependent fonts and schedules a repaint.</summary>
    public void UpdateSizing(bool isLargeScreen)
    {
        _textFont?.Dispose();
        _priceFont?.Dispose();
        _badgeFont?.Dispose();
        _secondaryTextFont?.Dispose();

        _textFont = new SKFont(SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Normal), isLargeScreen ? 15f : 12f);
        _priceFont = new SKFont(SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Normal), isLargeScreen ? 11f : 8.5f);
        _badgeFont = new SKFont(SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Bold), isLargeScreen ? 13f : 11f);
        _secondaryTextFont = new SKFont(SKTypeface.FromFamilyName("sans-serif", SKFontStyle.Normal), isLargeScreen ? 13f : 11f);

        _canvas.InvalidateSurface();
    }

    public void Dispose()
    {
        _bgPaint?.Dispose(); _bgPaint = null;
        _textPaint?.Dispose(); _textPaint = null;
        _textFont?.Dispose(); _textFont = null;
        _pricePaint?.Dispose(); _pricePaint = null;
        _priceFont?.Dispose(); _priceFont = null;
        _badgeBgPaint?.Dispose(); _badgeBgPaint = null;
        _badgeTextPaint?.Dispose(); _badgeTextPaint = null;
        _badgeFont?.Dispose(); _badgeFont = null;
        _shimmerBasePaint?.Dispose(); _shimmerBasePaint = null;
        _secondaryTextPaint?.Dispose(); _secondaryTextPaint = null;
        _secondaryTextFont?.Dispose(); _secondaryTextFont = null;
        _separatorPaint?.Dispose(); _separatorPaint = null;
        _dimPaint?.Dispose(); _dimPaint = null;
        _shadowPaint?.Dispose(); _shadowPaint = null;
        _borderPaint?.Dispose(); _borderPaint = null;
        _chipBgPaint?.Dispose(); _chipBgPaint = null;
        _shimmerPaint?.Dispose(); _shimmerPaint = null;
        _shimmerShader?.Dispose(); _shimmerShader = null;
        _cardRoundRect?.Dispose(); _cardRoundRect = null;
        _imageRoundRect?.Dispose(); _imageRoundRect = null;
    }

    public void Paint(SKPaintSurfaceEventArgs e, RenderList list, float scrollY, float viewWidth, DragState? dragState = null)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;
        if (info.Width <= 0 || info.Height <= 0)
        {
            canvas.Clear(ThemeBackground);
            return;
        }

        try
        {
            // When there are no cards, clear to theme background so we never leave a black surface (Android black-screen fix).
            if (list == null || list.Commands.IsEmpty)
            {
                canvas.Clear(ThemeBackground);
                return;
            }

            // Advance shimmer phase
            long now = _animationStopwatch.ElapsedMilliseconds;
            float delta = (now - _lastFrameTime) / 1000f;
            _lastFrameTime = now;
            if (delta > 0.1f) delta = 0.016f;
            _shimmerPhase += delta * 0.8f;
            if (_shimmerPhase > 1f) _shimmerPhase -= 1f;

            canvas.Clear(new SKColor(18, 18, 18));
            if (_bgPaint == null) EnsureResources();

            float scale = info.Width / (viewWidth > 0 ? viewWidth : 360f);
            canvas.Scale(scale);
            canvas.Translate(0, -scrollY);

            var viewMode = list.ViewMode;
            foreach (var cmd in list.Commands)
            {
                if (cmd is DrawCardCommand draw)
                {
                    bool isSource = dragState != null && draw.Index == dragState.SourceIndex;
                    bool isTarget = dragState != null && !isSource && draw.Index == dragState.TargetIndex;

                    if (viewMode == AetherVault.Core.Layout.ViewMode.List)
                        RenderListCard(canvas, draw);
                    else if (viewMode == AetherVault.Core.Layout.ViewMode.TextOnly)
                        RenderTextOnlyCard(canvas, draw);
                    else
                        RenderCard(canvas, draw);

                    // Dim the source card slot so the floating drag card stands out
                    if (isSource)
                    {
                        _cardRoundRect!.SetRect(draw.Rect, 8f, 8f);
                        canvas.DrawRoundRect(_cardRoundRect, _dimPaint);
                    }

                    // Highlight the drop target slot with an accent border
                    if (isTarget)
                        DrawTargetHighlight(canvas, draw.Rect);
                }
            }

            // Draw the floating drag card at the pointer position
            if (dragState?.DraggedCard != null && (dragState.CanvasX != 0 || dragState.CanvasY != 0))
            {
                float cw = list.CardWidth;
                float ch = list.CardHeight;
                float dx = dragState.CanvasX - cw / 2f;
                float dy = dragState.CanvasY - ch / 2f;
                var dragRect = new SKRect(dx, dy, dx + cw, dy + ch);

                // Drop shadow
                canvas.DrawRoundRect(dragRect, 8f, 8f, _shadowPaint);

                // Render card at drag position (match the active view mode)
                var dragCmd = new DrawCardCommand(dragState.DraggedCard, dragRect, dragState.SourceIndex);
                if (list.ViewMode == AetherVault.Core.Layout.ViewMode.List)
                    RenderListCard(canvas, dragCmd);
                else if (list.ViewMode == AetherVault.Core.Layout.ViewMode.TextOnly)
                    RenderTextOnlyCard(canvas, dragCmd);
                else
                    RenderCard(canvas, dragCmd);
            }
        }
        catch (Exception ex)
        {
            canvas.Clear(new SKColor(18, 18, 18));
            Logger.LogStuff($"[CardGridRenderer] Paint error: {ex}", LogLevel.Error);
        }
    }

    private void DrawTargetHighlight(SKCanvas canvas, SKRect rect)
    {
        canvas.DrawRoundRect(rect, 8f, 8f, _borderPaint);
    }

    private void DrawCardImageOrShimmer(SKCanvas canvas, SKRect imageRect, string cacheKey, float cornerRadius = 8f)
    {
        var image = _getImage(cacheKey);
        if (image != null && image.Handle != IntPtr.Zero)
        {
            canvas.Save();
            _imageRoundRect!.SetRect(imageRect, cornerRadius, cornerRadius);
            canvas.ClipRoundRect(_imageRoundRect, antialias: true);
            canvas.DrawImage(image, imageRect);
            canvas.Restore();
        }
        else
        {
            DrawShimmer(canvas, imageRect);
        }
    }

    private void DrawQuantityBadge(SKCanvas canvas, int quantity, float rightEdge, float top, float height)
    {
        if (quantity <= 0) return;
        string qtyStr = quantity.ToString();
        float tw = _badgeFont!.MeasureText(qtyStr);
        float minWidth = height >= 19f ? 20f : 18f;
        float padding = height >= 19f ? 10f : 8f;
        float bw = Math.Max(minWidth, tw + padding);
        float x = rightEdge - bw;
        float radius = height / 2f;
        canvas.DrawRoundRect(x, top, bw, height, radius, radius, _badgeBgPaint!);
        canvas.DrawText(qtyStr, x + (bw - tw) / 2f, top + height - 5f, _badgeFont, _badgeTextPaint!);
    }

    private void RenderCard(SKCanvas canvas, DrawCardCommand cmd)
    {
        var rect = cmd.Rect;
        var card = cmd.Card;

        // 1. Card background
        _cardRoundRect!.SetRect(rect, 8f, 8f);
        canvas.DrawRoundRect(_cardRoundRect, _bgPaint);

        // 2. Card image
        var imageRect = new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + rect.Width * 1.3968f);
        var cacheKey = ImageDownloadService.GetCacheKey(card.ScryfallId, "normal", "");
        DrawCardImageOrShimmer(canvas, imageRect, cacheKey, cornerRadius: 8f);

        // 3. Name text + set symbol
        float textY = imageRect.Bottom + 16f;
        float textX = rect.Left + 4f;
        float textWidth = rect.Width - 8f;

        if (!string.IsNullOrEmpty(card.SetCode))
        {
            var setSymbol = SetSvgCache.GetSymbol(card.SetCode);
            if (setSymbol != null)
            {
                float symbolSize = 14f;
                float symbolY = textY - (_textFont!.Size * 0.6f) - (symbolSize / 2f);
                float symbolX = rect.Right - 4f - symbolSize;
                SetSvgCache.DrawSymbol(canvas, card.SetCode, symbolX, symbolY, symbolSize, SKColors.White);
                textWidth -= (symbolSize + 4f);
            }
        }

        DrawWrappedText(canvas, card.Name, textX, textY, textWidth, _textFont, _textPaint);

        // 4. Price chip
        if (!string.IsNullOrEmpty(card.CachedDisplayPrice))
        {
            const float chipPadX = 5f;
            const float chipPadY = 3f;
            const float chipRadius = 4f;
            const float chipMargin = 6f;

            float priceTextWidth = _priceFont!.MeasureText(card.CachedDisplayPrice);
            float chipW = priceTextWidth + chipPadX * 2f;
            float chipH = _priceFont.Size + chipPadY * 2f;
            float chipY = imageRect.Bottom - (imageRect.Height * 0.15f) - chipH;
            float chipX = imageRect.Left + chipMargin;
            var chipRect = new SKRect(chipX, chipY, chipX + chipW, chipY + chipH);

            canvas.DrawRoundRect(chipRect, chipRadius, chipRadius, _chipBgPaint);
            canvas.DrawText(card.CachedDisplayPrice, chipRect.Left + chipPadX, chipRect.Bottom - chipPadY - 1f,
                SKTextAlign.Left, _priceFont, _pricePaint);
        }

        // 5. Quantity badge
        DrawQuantityBadge(canvas, card.Quantity, imageRect.Right - 4f, imageRect.Top + 4f, 20f);
    }

    private void RenderListCard(SKCanvas canvas, DrawCardCommand cmd)
    {
        var row = cmd.Rect;
        var card = cmd.Card;

        const float pad = 10f;
        const float gap = 10f;

        // 1. Row separator at bottom
        canvas.DrawRect(new SKRect(0f, row.Bottom - 1f, row.Right, row.Bottom), _separatorPaint);

        // 2. Thumbnail rect — vertically centered in row
        float imgTop = row.Top + (row.Height - ListImgHeight) / 2f;
        var imgRect = new SKRect(pad, imgTop, pad + ListImgWidth, imgTop + ListImgHeight);

        var cacheKey = ImageDownloadService.GetCacheKey(card.ScryfallId, "normal", "");
        DrawCardImageOrShimmer(canvas, imgRect, cacheKey, cornerRadius: 4f);

        // 3. Quantity badge overlaid on thumbnail (top-right corner)
        DrawQuantityBadge(canvas, card.Quantity, imgRect.Right - 2f, imgRect.Top + 2f, 18f);

        // 4. Text area — to the right of the thumbnail
        float textLeft = pad + ListImgWidth + gap;
        float textRight = row.Right - pad;
        float textWidth = textRight - textLeft;

        float nameSize = _textFont!.Size;
        float secSize = _secondaryTextFont!.Size;
        float lineHeight = nameSize * 1.3f;
        float secLineHeight = secSize * 1.3f;

        // Vertically distribute: name + type + set+price block, centered
        float blockHeight = lineHeight + secLineHeight + secLineHeight;
        float textTop = row.Top + (row.Height - blockHeight) / 2f + nameSize;

        // Card name
        string nameTrunc = TruncateWithEllipsis(card.Name, textWidth, _textFont);
        canvas.DrawText(nameTrunc, textLeft, textTop, SKTextAlign.Left, _textFont, _textPaint!);

        // Type line
        float typeY = textTop + lineHeight;
        if (!string.IsNullOrEmpty(card.TypeLine))
        {
            string typeTrunc = TruncateWithEllipsis(card.TypeLine, textWidth, _secondaryTextFont);
            canvas.DrawText(typeTrunc, textLeft, typeY, SKTextAlign.Left, _secondaryTextFont, _secondaryTextPaint!);
        }

        // Set code (left) + Price (right)
        float metaY = typeY + secLineHeight;
        if (!string.IsNullOrEmpty(card.SetCode))
        {
            SetSvgCache.DrawSymbol(canvas, card.SetCode, textLeft, metaY - secSize * 0.8f, secSize, new SKColor(160, 160, 160));
            float symbolSpace = secSize + 4f;
            canvas.DrawText(card.SetCode.ToUpper(), textLeft + symbolSpace, metaY, SKTextAlign.Left, _secondaryTextFont, _secondaryTextPaint!);
        }

        if (!string.IsNullOrEmpty(card.CachedDisplayPrice))
        {
            canvas.DrawText(card.CachedDisplayPrice, textRight, metaY, SKTextAlign.Right, _priceFont!, _pricePaint!);
        }
    }

    private void RenderTextOnlyCard(SKCanvas canvas, DrawCardCommand cmd)
    {
        var row = cmd.Rect;
        var card = cmd.Card;
        const float pad = 10f;

        // 1. Separator
        canvas.DrawRect(new SKRect(0f, row.Bottom - 1f, row.Right, row.Bottom), _separatorPaint);

        // 2. Name
        float textY = row.MidY + (_textFont!.Size / 2f) - 2f;
        float nameX = pad;
        float nameWidth = row.Width * 0.35f;
        string nameTrunc = TruncateWithEllipsis(card.Name, nameWidth, _textFont);
        canvas.DrawText(nameTrunc, nameX, textY, SKTextAlign.Left, _textFont, _textPaint!);

        // 3. Mana Cost
        float measuredName = _textFont.MeasureText(nameTrunc);
        float manaX = nameX + measuredName + 8f;
        float manaEndX = DrawManaCost(canvas, card.ManaCost, manaX, row.MidY - 7f, 14f);

        // 4. Quantity Badge (Rightmost)
        float priceRightLimit = row.Right - pad;
        if (card.Quantity > 0)
        {
            DrawQuantityBadge(canvas, card.Quantity, row.Right - 4f, row.MidY - 9f, 18f);
            float tw = _badgeFont!.MeasureText(card.Quantity.ToString());
            float bw = Math.Max(18f, tw + 8f);
            priceRightLimit = row.Right - 4f - bw - 8f;
        }

        // 5. Price
        if (!string.IsNullOrEmpty(card.CachedDisplayPrice))
        {
            canvas.DrawText(card.CachedDisplayPrice, priceRightLimit, textY, SKTextAlign.Right, _priceFont!, _pricePaint!);
        }

        // 6. Set Symbol (Left of Price)
        float setSize = 14f;
        // Estimate price width to position set symbol
        float priceWidth = string.IsNullOrEmpty(card.CachedDisplayPrice) ? 0 : _priceFont!.MeasureText(card.CachedDisplayPrice);
        float setX = priceRightLimit - priceWidth - setSize - 12f;
        if (!string.IsNullOrEmpty(card.SetCode))
        {
            SetSvgCache.DrawSymbol(canvas, card.SetCode, setX, row.MidY - setSize / 2f, setSize, new SKColor(160, 160, 160));
        }

        // 7. Type Line (Between Mana and Set Symbol)
        float typeX = row.Width * 0.45f;
        // Make sure it doesn't overlap with mana cost if name is long, or with set symbol
        // Use manaEndX + padding instead of fixed offset
        if (typeX < manaEndX + 10f) typeX = manaEndX + 10f;

        float typeRightLimit = setX - 10f;
        float typeWidth = typeRightLimit - typeX;

        if (typeWidth > 20f && !string.IsNullOrEmpty(card.TypeLine))
        {
            string typeTrunc = TruncateWithEllipsis(card.TypeLine, typeWidth, _secondaryTextFont!);
            canvas.DrawText(typeTrunc, typeX, textY, SKTextAlign.Left, _secondaryTextFont, _secondaryTextPaint!);
        }
    }

    private float DrawManaCost(SKCanvas canvas, string manaCost, float x, float y, float size)
    {
        if (string.IsNullOrEmpty(manaCost)) return x;

        float currentX = x;
        int i = 0;
        while (i < manaCost.Length)
        {
            if (manaCost[i] == '{')
            {
                int end = manaCost.IndexOf('}', i);
                if (end > i)
                {
                    string symbol = manaCost.Substring(i + 1, end - i - 1);
                    ManaSvgCache.DrawSymbol(canvas, symbol, currentX, y, size);
                    currentX += size + 2f;
                    i = end + 1;
                    continue;
                }
            }
            i++;
        }
        return currentX;
    }

    private void DrawShimmer(SKCanvas canvas, SKRect rect)
    {
        if (_shimmerPaint == null || _shimmerShader == null) return;

        canvas.Save();
        _imageRoundRect!.SetRect(rect, 8f, 8f);
        canvas.ClipRoundRect(_imageRoundRect, antialias: true);

        canvas.DrawRect(rect, _shimmerBasePaint!);

        float sweepWidth = rect.Width * 0.6f;
        float travelRange = rect.Width + sweepWidth;
        // Map draw coords to shader 0..1 so the band sweeps; phase slides the gradient.
        var translate = SKMatrix.CreateTranslation(-rect.Left - _shimmerPhase * travelRange, -rect.Top);
        var scale = SKMatrix.CreateScale(1f / travelRange, 1f / rect.Height);
        var localMatrix = SKMatrix.Concat(translate, scale);
        _shimmerPaint.Shader = _shimmerShader.WithLocalMatrix(localMatrix);
        try
        {
            canvas.DrawRect(rect.Left, rect.Top, travelRange, rect.Height, _shimmerPaint);
        }
        finally
        {
            _shimmerPaint.Shader = null;
        }
        canvas.Restore();
    }

    private void DrawWrappedText(SKCanvas canvas, string text, float x, float y,
        float maxWidth, SKFont? font, SKPaint? paint, int maxLines = 2)
    {
        if (string.IsNullOrEmpty(text) || font == null || paint == null) return;

        var words = text.Split(' ');
        var currentLine = "";
        float lineHeight = font.Size * 1.2f;
        float currentY = y;
        int lineCount = 0;

        foreach (var word in words)
        {
            if (lineCount >= maxLines) break;

            string testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
            float width = font.MeasureText(testLine);

            if (width > maxWidth)
            {
                if (!string.IsNullOrEmpty(currentLine))
                {
                    string toDraw = lineCount == maxLines - 1
                        ? TruncateWithEllipsis(currentLine, maxWidth, font)
                        : currentLine;
                    canvas.DrawText(toDraw, x, currentY, SKTextAlign.Left, font, paint);
                    lineCount++;
                    currentY += lineHeight;
                    currentLine = word;
                }
                else
                {
                    canvas.DrawText(TruncateWithEllipsis(word, maxWidth, font), x, currentY, SKTextAlign.Left, font, paint);
                    lineCount++;
                    currentY += lineHeight;
                    currentLine = "";
                }
            }
            else
            {
                currentLine = testLine;
            }
        }

        if (!string.IsNullOrEmpty(currentLine) && lineCount < maxLines)
        {
            string toDraw = lineCount == maxLines - 1
                ? TruncateWithEllipsis(currentLine, maxWidth, font)
                : currentLine;
            canvas.DrawText(toDraw, x, currentY, SKTextAlign.Left, font, paint);
        }
    }

    private static string TruncateWithEllipsis(string text, float maxWidth, SKFont font)
    {
        if (font.MeasureText(text) <= maxWidth) return text;
        while (text.Length > 0 && font.MeasureText(text + "…") > maxWidth)
            text = text[..^1];
        return text + "…";
    }
}
