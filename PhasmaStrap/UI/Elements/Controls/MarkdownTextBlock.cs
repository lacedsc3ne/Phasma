using PhasmaStrap.UI.ViewModels;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig;
using System.Windows.Media;
using Block = Markdig.Syntax.Block;
using FontFamily = System.Windows.Media.FontFamily;

namespace PhasmaStrap.UI.Elements.Controls
{
    /// <summary>
    /// TextBlock with markdown support: paragraphs, headings, bullet and numbered lists, quotes,
    /// code, emphasis and links. (It used to show paragraphs only, so a changelog - headings and
    /// bullets - came out empty.)
    /// </summary>
    [ContentProperty("MarkdownText")]
    [Localizability(LocalizationCategory.Text)]
    class MarkdownTextBlock : TextBlock
    {
        private static readonly MarkdownPipeline _markdownPipeline = new MarkdownPipelineBuilder()
                .UseEmphasisExtras(Markdig.Extensions.EmphasisExtras.EmphasisExtraOptions.Marked) // enable '==' support
                .UseSoftlineBreakAsHardlineBreak()
                .Build();

        public static readonly DependencyProperty MarkdownTextProperty =
            DependencyProperty.Register(nameof(MarkdownText), typeof(string), typeof(MarkdownTextBlock),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender, OnTextMarkdownChanged));

        [Localizability(LocalizationCategory.Text)]
        public string MarkdownText
        {
            get => (string)GetValue(MarkdownTextProperty);
            set => SetValue(MarkdownTextProperty, value);
        }

        private static readonly FontFamily CodeFont = new("Consolas");

        // all of an inline container's children, in order
        private static Span Children(ContainerInline container)
        {
            var span = new Span();
            foreach (Markdig.Syntax.Inlines.Inline child in container)
            {
                var wpf = GetWpfInlineFromMarkdownInline(child);
                if (wpf is not null)
                    span.Inlines.Add(wpf);
            }
            return span;
        }

        private static System.Windows.Documents.Inline? GetWpfInlineFromMarkdownInline(Markdig.Syntax.Inlines.Inline? inline)
        {
            switch (inline)
            {
                case LiteralInline literalInline:
                    return new Run(literalInline.ToString());

                case EmphasisInline emphasisInline:
                    switch (emphasisInline.DelimiterChar)
                    {
                        case '*':
                        case '_':
                            return emphasisInline.DelimiterCount == 1
                                ? new Italic(Children(emphasisInline))
                                : new Bold(Children(emphasisInline));

                        case '=': // marked
                            {
                                var span = Children(emphasisInline);
                                span.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
                                return span;
                            }
                    }
                    return Children(emphasisInline);

                case LinkInline linkInline:
                    {
                        string? url = linkInline.Url;
                        if (linkInline.IsImage)
                            return null;
                        if (string.IsNullOrEmpty(url))
                            return Children(linkInline);

                        return new Hyperlink(Children(linkInline))
                        {
                            Command = GlobalViewModel.OpenWebpageCommand,
                            CommandParameter = url
                        };
                    }

                case AutolinkInline autolink:
                    return new Hyperlink(new Run(autolink.Url))
                    {
                        Command = GlobalViewModel.OpenWebpageCommand,
                        CommandParameter = autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url
                    };

                case CodeInline code:
                    return new Run(code.Content)
                    {
                        FontFamily = CodeFont,
                        Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                    };

                case LineBreakInline:
                    return new LineBreak();

                case ContainerInline container:
                    return Children(container);
            }

            return null;
        }

        private void AddInlines(ContainerInline? container)
        {
            if (container is null)
                return;
            foreach (Markdig.Syntax.Inlines.Inline inline in container)
            {
                var wpf = GetWpfInlineFromMarkdownInline(inline);
                if (wpf is not null)
                    Inlines.Add(wpf);
            }
        }

        private bool _anything;

        // a gap before every block but the first
        private void BlockGap(bool tight)
        {
            if (!_anything)
            {
                _anything = true;
                return;
            }

            Inlines.Add(new LineBreak());
            if (!tight)
                Inlines.Add(new LineBreak());
        }

        private void AddBlock(Block block, string indent, bool tight)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    {
                        BlockGap(false);
                        var span = new Span { FontWeight = FontWeights.SemiBold, FontSize = Math.Max(FontSize, FontSize * (heading.Level <= 1 ? 1.35 : heading.Level == 2 ? 1.2 : 1.05)) };
                        if (heading.Inline is not null)
                            foreach (Markdig.Syntax.Inlines.Inline inline in heading.Inline)
                            {
                                var wpf = GetWpfInlineFromMarkdownInline(inline);
                                if (wpf is not null)
                                    span.Inlines.Add(wpf);
                            }
                        Inlines.Add(span);
                        break;
                    }

                case ParagraphBlock paragraph:
                    BlockGap(tight);
                    if (indent.Length > 0)
                        Inlines.Add(new Run(indent));
                    AddInlines(paragraph.Inline);
                    break;

                case ListBlock list:
                    {
                        int number = int.TryParse(list.OrderedStart, out int start) ? start : 1;
                        foreach (Block item in list)
                        {
                            if (item is not ListItemBlock listItem)
                                continue;

                            string marker = list.IsOrdered ? $"{number++}. " : "•  ";
                            bool first = true;
                            foreach (Block child in listItem)
                            {
                                if (first && child is ParagraphBlock p)
                                {
                                    BlockGap(true);
                                    Inlines.Add(new Run(indent + marker));
                                    AddInlines(p.Inline);
                                }
                                else
                                {
                                    AddBlock(child, indent + "     ", true);
                                }
                                first = false;
                            }
                        }
                        break;
                    }

                case QuoteBlock quote:
                    foreach (Block child in quote)
                        AddBlock(child, indent + "│ ", tight);
                    break;

                case CodeBlock code: // fenced or indented
                    BlockGap(tight);
                    Inlines.Add(new Run(code.Lines.ToString().TrimEnd())
                    {
                        FontFamily = CodeFont,
                        Background = new SolidColorBrush(Color.FromArgb(40, 128, 128, 128)),
                    });
                    break;

                case ThematicBreakBlock:
                    BlockGap(false);
                    Inlines.Add(new Run("────────────") { Foreground = new SolidColorBrush(Color.FromArgb(90, 128, 128, 128)) });
                    break;

                case ContainerBlock container:
                    foreach (Block child in container)
                        AddBlock(child, indent, tight);
                    break;
            }
        }

        private static void OnTextMarkdownChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs dependencyPropertyChangedEventArgs)
        {
            if (dependencyObject is not MarkdownTextBlock markdownTextBlock)
                return;

            if (dependencyPropertyChangedEventArgs.NewValue is not string rawDocument)
                return;

            var document = Markdown.Parse(rawDocument, _markdownPipeline);

            markdownTextBlock.Inlines.Clear();
            markdownTextBlock._anything = false;

            foreach (var block in document)
                markdownTextBlock.AddBlock(block, "", false);
        }
    }
}
