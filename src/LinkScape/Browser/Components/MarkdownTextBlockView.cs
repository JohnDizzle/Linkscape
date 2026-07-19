using LinkScape.Browser;
using CommunityToolkit.WinUI.UI.Controls;
using Microsoft.UI.Text;

namespace Browser.Components;

internal sealed record MarkdownTextBlockViewProps(
    string Markdown,
    bool IsError = false);

internal sealed class MarkdownTextBlockView : Component<MarkdownTextBlockViewProps>
{
    private MarkdownTextBlock? _markdownTextBlock;

    public override Element Render()
    {
        return Border(null)
            .Set(host =>
            {
                var markdownTextBlock = GetMarkdownTextBlock();
                host.Child = markdownTextBlock;
                host.DispatcherQueue.TryEnqueue(() => host.Child = GetMarkdownTextBlock());
            })
            .Padding(2)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Top);
    }

    private MarkdownTextBlock GetMarkdownTextBlock()
    {
        if (_markdownTextBlock is null)
        {
            _markdownTextBlock = new MarkdownTextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 15,
                Opacity = 0.92,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Top
            };
        }

        _markdownTextBlock.Text = Props.Markdown ?? string.Empty;
        _markdownTextBlock.FontWeight = Props.IsError ? FontWeights.SemiBold : FontWeights.Normal;
        return _markdownTextBlock;
    }
}
