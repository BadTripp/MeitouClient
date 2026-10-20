namespace Meitou.Rendering.Ui;

/// <summary>Text measuring for layouts that must fit what <see cref="Draw"/> will paint (the tooltips).</summary>
public sealed partial class UiPainter
{
    /// <summary>The size of a measured text block.</summary>
    public readonly record struct TextSize(float Width, float Height);

    /// <summary>
    /// The font a TrueType font resource (<c>Kenshi_StandardFont_Medium</c> ...) is drawn with at <paramref name="scale"/> (the control's <c>TextScale</c>, else the viewport height over 1080),
    /// baked at the em pixel size the original resizes it to; null when <paramref name="fontName"/> is no such resource.
    /// </summary>
    DebugOverlay? ResourceFont(string fontName, float scale, float minimumTextPixels)
    {
        if (!resources.Resources.TryGetValue(fontName, out var fontResource) || (string?)fontResource.Attribute("type") != "ResourceTrueTypeFont") return null;
        string? Property(string key) => fontResource.Elements("Property").FirstOrDefault(p => (string?)p.Attribute("key") == key)?.Attribute("value")?.Value;
        float Number(string key, float fallback) => float.TryParse(Property(key), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float number) && float.IsFinite(number) && number > 0 ? number : fallback;
        if (Property("Source") is not { } source || FontPath(source) is not { } sourcePath) return null;
        // TrueType resource sizes are points at the resource's DPI, resized as the original does on load. Bake at the resulting em pixel size.
        float resolution = Number("Resolution", 50);
        int resourcePixels = Math.Clamp((int)MathF.Round(Math.Max(minimumTextPixels, EffectiveFontSize(sourcePath, Number("Size", 20), resolution) * resolution / 72 * scale)), 6, 192);
        var key = (sourcePath, resourcePixels, Property("Antialias") == "false");
        if (!resourceFonts.TryGetValue(key, out var exact)) resourceFonts[key] = exact = DebugOverlay.FromUiFont(overlay.Gpu, sourcePath, resourcePixels, key.Item3);
        return exact;
    }

    /// <summary>
    /// The size <see cref="Draw"/> gives <paramref name="text"/> in font resource <paramref name="fontName"/> on a viewport <paramref name="viewportHeight"/> high: wrapped to
    /// <paramref name="wrapWidth"/> when that is above zero (a control with <c>WordWrap</c> true), else one line per line break. <paramref name="minimumTextPixels"/> is the value passed to <see cref="Draw"/>.
    /// </summary>
    public TextSize MeasureText(string fontName, string text, int viewportHeight, float wrapWidth, float minimumTextPixels)
    {
        var font = ResourceFont(fontName, viewportHeight / 1080f, minimumTextPixels) ?? standardFont ?? overlay;
        float scale = font == standardFont || font == overlay ? Math.Clamp(viewportHeight / 1080f, 0.5f, 1.5f) : 1;
        text = text.Replace("\\n", "\n", StringComparison.Ordinal).ReplaceLineEndings("\n");
        var lines = wrapWidth > 0 ? font.WrapText(text, wrapWidth, scale) : text.Split('\n');
        float width = 0;
        foreach (string line in lines) width = Math.Max(width, font.MeasureText(line, scale));
        return new(width, Math.Max(1, lines.Count) * font.LineHeight * scale);
    }
}
