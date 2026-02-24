using MTGFetchMAUI.Core.Layout;
using MTGFetchMAUI.Services;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using System.Diagnostics;

namespace MTGFetchMAUI.Controls;

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
        _textFont ??= new SKFont { Size = 12f };
        _pricePaint ??= new SKPaint { Color = SKColors.LightGreen, IsAntialias = true };
        _priceFont ??= new SKFont { Size = 8.5f };
        _badgeBgPaint ??= new SKPaint { IsAntialias = true, Color = new SKColor(220, 50, 50) };
        _badgeTextPaint ??= new SKPaint { IsAntialias = true, Color = SKColors.White };
        _badgeFont ??= new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), 11f);
        _shimmerBasePaint ??= new SKPaint { Color = new SKColor(40, 40, 40) };
        _secondaryTextPaint ??= new SKPaint { Color = new SKColor(160, 160, 160), IsAntialias = true };
        _secondaryTextFont ??= new SKFont { Size = 11f };
        _separatorPaint ??= new SKPaint { Color = new SKColor(50, 50, 50) };
        _cardRoundRect ??= new SKRoundRect();
        _imageRoundRect ??= new SKRoundRect();
    }

    /// <summary>Rebuilds size-dependent fonts and schedules a repaint.</summary>
    public void UpdateSizing(bool isLargeScreen)
    {
        _textFont?.Dispose();
        _priceFont?.Dispose();
        _badgeFont?.Dispose();
        _secondaryTextFont?.Dispose();

        _textFont = new SKFont { Size = isLargeScreen ? 15f : 12f };
        _priceFont = new SKFont { Size = isLargeScreen ? 11f : 8.5f };
        _badgeFont = new SKFont(SKTypeface.FromFamilyName(null, SKFontStyle.Bold), isLargeScreen ? 13f : 11f);
        _secondaryTextFont = new SKFont { Size = isLargeScreen ? 13f : 11f };

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
        _cardRoundRect?.Dispose(); _cardRoundRect = null;
        _imageRoundRect?.Dispose(); _imageRoundRect = null;
    }

    public void Paint(SKPaintSurfaceEventArgs e, RenderList list, float scrollY, float viewWidth, DragState? dragState = null)
    {
        // Advance shimmer phase
        long now = _animationStopwatch.ElapsedMilliseconds;
        float delta = (now - _lastFrameTime) / 1000f;
        _lastFrameTime = now;
        if (delta > 0.1f) delta = 0.016f;
        _shimmerPhase += delta * 0.8f;
        if (_shimmerPhase > 1f) _shimmerPhase -= 1f;

        var canvas = e.Surface.Canvas;
        canvas.Clear(new SKColor(18, 18, 18));

        if (list == null || list.Commands.IsEmpty) return;
        if (_bgPaint == null) EnsureResources();

        float scale = e.Info.Width / (viewWidth > 0 ? viewWidth : 360f);
        canvas.Scale(scale);
        canvas.Translate(0, -scrollY);

        bool isList = list.ViewMode == MTGFetchMAUI.Core.Layout.ViewMode.List;
        foreach (var cmd in list.Commands)
        {
            if (cmd is DrawCardCommand draw)
            {
                bool isSource = dragState != null && draw.Index == dragState.SourceIndex;
                bool isTarget = dragState != null && !isSource && draw.Index == dragState.TargetIndex;

                if (isList)
                    RenderListCard(canvas, draw);
                else
                    RenderCard(canvas, draw);

                // Dim the source card slot so the floating drag card stands out
                if (isSource)
                {
                    using var dimPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 160) };
                    _cardRoundRect!.SetRect(draw.Rect, 8f, 8f);
                    canvas.DrawRoundRect(_cardRoundRect, dimPaint);
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
            using var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(0, 0, 0, 140),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10f)
            };
            canvas.DrawRoundRect(dragRect, 8f, 8f, shadowPaint);

            // Render card at drag position (match the active view mode)
            var dragCmd = new DrawCardCommand(dragState.DraggedCard, dragRect, dragState.SourceIndex);
            if (isList)
                RenderListCard(canvas, dragCmd);
            else
                RenderCard(canvas, dragCmd);
        }
    }

    private void DrawTargetHighlight(SKCanvas canvas, SKRect rect)
    {
        using var borderPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(100, 200, 255, 220),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3f
        };
        canvas.DrawRoundRect(rect, 8f, 8f, borderPaint);
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
        var image = _getImage(cacheKey);

        if (image != null && image.Handle != IntPtr.Zero)
        {
            canvas.Save();
            _imageRoundRect!.SetRect(imageRect, 8f, 8f);
            canvas.ClipRoundRect(_imageRoundRect, antialias: true);
            canvas.DrawImage(image, imageRect);
            canvas.Restore();
        }
        else
        {
            DrawShimmer(canvas, imageRect);
        }

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

            using var chipBgPaint = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 185) };
            canvas.DrawRoundRect(chipRect, chipRadius, chipRadius, chipBgPaint);
            canvas.DrawText(card.CachedDisplayPrice, chipRect.Left + chipPadX, chipRect.Bottom - chipPadY - 1f,
                SKTextAlign.Left, _priceFont, _pricePaint);
        }

        // 5. Quantity badge
        if (card.Quantity > 0)
        {
            string qtyStr = card.Quantity.ToString();
            float tw = _badgeFont!.MeasureText(qtyStr);
            float bw = Math.Max(20f, tw + 10f);
            float bx = imageRect.Right - bw - 4f;
            float by = imageRect.Top + 4f;
            canvas.DrawRoundRect(bx, by, bw, 20f, 10f, 10f, _badgeBgPaint!);
            canvas.DrawText(qtyStr, bx + (bw - tw) / 2f, by + 15f, _badgeFont, _badgeTextPaint!);
        }
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
        var image = _getImage(cacheKey);

        if (image != null && image.Handle != IntPtr.Zero)
        {
            canvas.Save();
            _imageRoundRect!.SetRect(imgRect, 4f, 4f);
            canvas.ClipRoundRect(_imageRoundRect, antialias: true);
            canvas.DrawImage(image, imgRect);
            canvas.Restore();
        }
        else
        {
            DrawShimmer(canvas, imgRect);
        }

        // 3. Quantity badge overlaid on thumbnail (top-right corner)
        if (card.Quantity > 0)
        {
            string qtyStr = card.Quantity.ToString();
            float tw = _badgeFont!.MeasureText(qtyStr);
            float bw = Math.Max(18f, tw + 8f);
            float bx = imgRect.Right - bw - 2f;
            float by = imgRect.Top + 2f;
            canvas.DrawRoundRect(bx, by, bw, 18f, 9f, 9f, _badgeBgPaint!);
            canvas.DrawText(qtyStr, bx + (bw - tw) / 2f, by + 13f, _badgeFont, _badgeTextPaint!);
        }

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

    private void DrawShimmer(SKCanvas canvas, SKRect rect)
    {
        canvas.Save();
        _imageRoundRect!.SetRect(rect, 8f, 8f);
        canvas.ClipRoundRect(_imageRoundRect, antialias: true);

        canvas.DrawRect(rect, _shimmerBasePaint);

        float sweepWidth = rect.Width * 0.6f;
        float travelRange = rect.Width + sweepWidth;
        float shimmerX = rect.Left - sweepWidth + travelRange * _shimmerPhase;

        using var shimmerPaint = new SKPaint { IsAntialias = true };
        shimmerPaint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(shimmerX, rect.Top),
            new SKPoint(shimmerX + sweepWidth, rect.Top),
            new[] { SKColors.Transparent, new SKColor(255, 255, 255, 55), SKColors.Transparent },
            new[] { 0f, 0.5f, 1f },
            SKShaderTileMode.Clamp);

        canvas.DrawRect(rect, shimmerPaint);
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
