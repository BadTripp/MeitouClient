using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Meitou.Launcher;

/// <summary>
/// A news post's Markdown as themed controls (Markdig's syntax tree, drawn here): headings, paragraphs with bold, italic, code and
/// links, lists, quotes, code blocks, rules and images (fetched through <see cref="NewsFeed"/>). Raw HTML is left out.
/// </summary>
static class MarkdownView
{
    static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, DejaVu Sans Mono, monospace");

    public static Control Render(NewsPost post, NewsFeed feed, Action<string> open)
    {
        var panel = new StackPanel { Spacing = 10 };
        foreach (var block in Markdig.Markdown.Parse(post.Body))
            if (Block(block, post, feed, open) is { } control) panel.Children.Add(control);
        return panel;
    }

    /// <summary>An image loaded in the background; until then (or when it fails) it takes no room.</summary>
    public static Control Image(string url, NewsFeed feed, double maxHeight)
    {
        var image = new Image { Stretch = Stretch.Uniform, MaxHeight = maxHeight, HorizontalAlignment = HorizontalAlignment.Left };
        RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
        var frame = new Border { CornerRadius = new CornerRadius(6), ClipToBounds = true, HorizontalAlignment = HorizontalAlignment.Left, Child = image, IsVisible = false };
        _ = Task.Run(async () =>
        {
            var bytes = await feed.ImageAsync(url);
            if (bytes is null) return;
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    image.Source = new Bitmap(new MemoryStream(bytes));
                    frame.IsVisible = true;
                }
                catch (Exception) { } // not an image the decoder knows: leave it out
            });
        });
        return frame;
    }

    static Control? Block(Block block, NewsPost post, NewsFeed feed, Action<string> open)
    {
        switch (block)
        {
            case HeadingBlock heading:
                var title = Text(heading.Inline, post, open);
                title.FontSize = heading.Level <= 1 ? 20 : heading.Level == 2 ? 18 : 16;
                title.Margin = new Thickness(0, 6, 0, 0);
                return title;
            case ParagraphBlock paragraph:
                // A paragraph of only images shows them as pictures.
                var inlines = paragraph.Inline?.ToList() ?? [];
                if (inlines.Count > 0 && inlines.All(i => i is LinkInline { IsImage: true } or LineBreakInline || i is LiteralInline l && string.IsNullOrWhiteSpace(l.Content.ToString())))
                {
                    var images = new StackPanel { Spacing = 8 };
                    foreach (var link in inlines.OfType<LinkInline>())
                        images.Children.Add(Image(NewsFeed.Resolve(post, link.Url ?? ""), feed, 420));
                    return images;
                }
                return Text(paragraph.Inline, post, open);
            case ListBlock list:
                var items = new StackPanel { Spacing = 4 };
                int number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                foreach (var item in list.OfType<ListItemBlock>())
                {
                    var row = new DockPanel();
                    var marker = new TextBlock { Text = list.IsOrdered ? $"{number++}." : "•", Width = 22, Foreground = Brush("BrassLight") };
                    DockPanel.SetDock(marker, Dock.Left);
                    row.Children.Add(marker);
                    var body = new StackPanel { Spacing = 4 };
                    foreach (var child in item)
                        if (Block(child, post, feed, open) is { } control) body.Children.Add(control);
                    row.Children.Add(body);
                    items.Children.Add(row);
                }
                return items;
            case QuoteBlock quote:
                var quoted = new StackPanel { Spacing = 6 };
                foreach (var child in quote)
                    if (Block(child, post, feed, open) is { } control) quoted.Children.Add(control);
                return new Border { BorderBrush = Brush("Brass"), BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 2, 0, 2), Child = quoted };
            case CodeBlock code:
                return new Border
                {
                    Background = Brush("Field"), CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 8),
                    Child = new SelectableTextBlock { Text = code.Lines.ToString().TrimEnd(), FontFamily = Mono, FontSize = 12.5, FontWeight = FontWeight.Normal, TextWrapping = TextWrapping.Wrap },
                };
            case ThematicBreakBlock:
                return new Border { Height = 1, Background = Brush("PlateRim"), Margin = new Thickness(0, 4) };
            default:
                return null;
        }
    }

    static TextBlock Text(ContainerInline? container, NewsPost post, Action<string> open)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.Medium, FontSize = 14, LineHeight = 21, Inlines = [] };
        if (container is not null)
            foreach (var inline in container) Add(text.Inlines!, inline, post, open);
        return text;
    }

    static void Add(InlineCollection target, Markdig.Syntax.Inlines.Inline inline, NewsPost post, Action<string> open)
    {
        switch (inline)
        {
            case LiteralInline literal:
                target.Add(new Run(literal.Content.ToString()));
                break;
            case EmphasisInline emphasis:
                Span span = emphasis.DelimiterCount >= 2 ? new Bold() : new Italic();
                if (emphasis.DelimiterCount >= 2) span.Foreground = Brush("PaperLight");
                foreach (var child in emphasis) Add(span.Inlines, child, post, open);
                target.Add(span);
                break;
            case CodeInline code:
                target.Add(new Run(code.Content) { FontFamily = Mono, FontWeight = FontWeight.Normal, FontSize = 13, Foreground = Brush("PaperLight") });
                break;
            case LineBreakInline lineBreak:
                target.Add(lineBreak.IsHard ? new LineBreak() : new Run(" "));
                break;
            case LinkInline { IsImage: false } link:
                target.Add(Link(string.Concat(link.Descendants<LiteralInline>().Select(l => l.Content.ToString())), NewsFeed.Resolve(post, link.Url ?? ""), open));
                break;
            case AutolinkInline auto:
                target.Add(Link(auto.Url, auto.Url, open));
                break;
            case ContainerInline container:
                foreach (var child in container) Add(target, child, post, open);
                break;
        }
    }

    static InlineUIContainer Link(string label, string url, Action<string> open)
    {
        var link = new TextBlock
        {
            Text = label, Foreground = Brush("BrassLight"), TextDecorations = TextDecorations.Underline, Cursor = new Cursor(StandardCursorType.Hand),
            FontWeight = FontWeight.SemiBold,
        };
        ToolTip.SetTip(link, url);
        link.PointerPressed += (_, e) => { open(url); e.Handled = true; };
        return new InlineUIContainer(link) { BaselineAlignment = BaselineAlignment.TextBottom };
    }

    static IBrush Brush(string key) =>
        Application.Current!.TryGetResource(key, null, out var value) && value is IBrush brush ? brush : Brushes.Gray;
}
