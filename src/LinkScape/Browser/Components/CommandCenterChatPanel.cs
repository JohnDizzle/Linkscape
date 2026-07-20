using System.Threading;
using System.Threading.Tasks;
using LinkScape.Browser;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Markdown;

namespace Browser.Components;

internal sealed record CommandCenterChatPanelProps(
    Action<string> OnOpenLinkInNewTab,
    Action OnOpenAiKeyDialog,
    Func<CommandCenterChatContext> GetChatContext);

internal sealed class CommandCenterChatPanel : Component<CommandCenterChatPanelProps>
{
    private const string StoreLogoAssetPath = "ms-appx:///Assets/StoreLogo.png";
    private const string LinkerSvgAssetPath = "ms-appx:///Assets/LoadingLink.silver.svg";
    private const double MessageFontSize = 15;
    private const double MetaFontSize = 12;
    private const double AssistantBubbleMaxWidth = 480;
    private const double UserBubbleMaxWidth = 320;

    private sealed record ChatPanelMessage(string Text, bool IsUser, bool IsError = false, bool IsThinking = false);

    private readonly DispatcherQueue? _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private Microsoft.UI.Xaml.Controls.ScrollViewer? _messagesScrollViewer;
    private FrameworkElement? _messagesBottomAnchor;
    private int _scrollRequestVersion;
    private static readonly SolidColorBrush ChatSurfaceBrush = new(Microsoft.UI.ColorHelper.FromArgb(0xF0, 0x27, 0x27, 0x29));
    private static readonly SolidColorBrush AssistantBubbleBrush = new(Microsoft.UI.ColorHelper.FromArgb(0xF5, 0x2E, 0x2E, 0x31));

    public override Element Render()
    {
        var prompt = UseState(string.Empty);
        var arePromptSuggestionsOpen = UseState(false);
        var messages = UseState<IReadOnlyList<ChatPanelMessage>>(
        [
            new ChatPanelMessage(
                "## Hi, I'm Linker\nAsk about local history, favorites, today's active sites, or type `mcp status` to see the local tools I can use.",
                false)
        ], threadSafe: true);
        var isSending = UseState(false, threadSafe: true);

        void SetMessages(IReadOnlyList<ChatPanelMessage> nextMessages)
        {
            void ApplyMessages()
            {
                messages.Set(nextMessages);
                ScrollMessagesToBottom();
            }

            if (_dispatcherQueue is not null && !_dispatcherQueue.HasThreadAccess)
            {
                _dispatcherQueue.TryEnqueue(ApplyMessages);
                return;
            }

            ApplyMessages();
        }

        void ApplyPrompt(string value)
        {
            prompt.Set(value);
            arePromptSuggestionsOpen.Set(false);
        }

        async void SubmitPrompt()
        {
            var text = prompt.Value.Trim();

            if (string.IsNullOrWhiteSpace(text) || isSending.Value)
            {
                return;
            }

            prompt.Set(string.Empty);
            arePromptSuggestionsOpen.Set(false);
            isSending.Set(true);
            var pendingMessages = messages.Value
                .Concat(
                [
                    new ChatPanelMessage(text, true),
                    new ChatPanelMessage("Thinking…", false, IsThinking: true)
                ])
                .ToArray();

            SetMessages(pendingMessages);

            try
            {
                var response = await CommandCenterChatService.SubmitAsync(text, GetChatContext());
                LocalMcpDiagnostics.Trace("ChatUI", $"Response received. IsError={response.IsError}, TextLength={response.Text?.Length ?? 0}");
                var answer = string.IsNullOrWhiteSpace(response.Text)
                    ? "The chat service returned an empty answer. Check the MCP trace for the selected tool response."
                    : response.Text;
                SetMessages(
                    pendingMessages
                        .Where(message => !message.IsThinking)
                        .Append(new ChatPanelMessage(answer, false, response.IsError))
                        .ToArray());
            }
            catch (Exception ex)
            {
                LocalMcpDiagnostics.Trace("ChatUI", $"Submit failed: {ex}");
                SetMessages(
                    pendingMessages
                        .Where(message => !message.IsThinking)
                        .Append(new ChatPanelMessage($"Unable to answer: {ex.Message}", false, true))
                        .ToArray());
            }
            finally
            {
                isSending.Set(false);
            }
        }

        void ClearChat()
        {
            SetMessages(
            [
                new ChatPanelMessage("## Linker is ready\nSession cleared. Ask about history, favorites, or today's activity.", false)
            ]);
        }

        var quickPrompts = FlexRow(
            CreatePromptPill("Today", () => ApplyPrompt("history tell me today's active sites")),
            CreatePromptPill("Favorites", () => ApplyPrompt("summarize my favorites")),
            CreatePromptPill("Tabs", () => ApplyPrompt("summarize my saved tabs")),
            CreatePromptPill("Collections", () => ApplyPrompt("show my collections")),
            CreatePromptPill("Most visited", () => ApplyPrompt("what sites are most active in my history")),
            CreatePromptPill("Tools", () => ApplyPrompt("mcp status"))) with
        {
            ColumnGap = 6
        };

        var messageList = FlexColumn(
            [
                .. messages.Value.Select(BuildMessageBubble),
                Border(null)
                    .Height(1)
                    .Set(anchor => _messagesBottomAnchor = anchor)
            ]) with
        {
            RowGap = 8
        };

        var input = Border(
            FlexRow(
                AutoSuggestBox(prompt.Value, value =>
                {
                    prompt.Set(value);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        arePromptSuggestionsOpen.Set(false);
                    }
                }, _ => SubmitPrompt())
                    .AutomationName("CommandCenterChatPrompt")
                    .SuggestionChosen(value =>
                    {
                        prompt.Set(value);
                        arePromptSuggestionsOpen.Set(false);
                    })
                    .IsSuggestionListOpen(false)
                    .Flex(grow: 1, basis: 0) with
                {
                    PlaceholderText = isSending.Value ? "Analyzing browser data..." : "Ask about history, favorites, today, active sites, or mcp status...",
                    Suggestions = []
                },
                Button(isSending.Value ? "..." : "Ask", SubmitPrompt).AutomationName("CommandCenterChatSubmit")
                    .IsEnabled(!isSending.Value),
                Button("Clear", ClearChat)) with
            {
                ColumnGap = 8
            })
            .Padding(8)
            .CornerRadius(12)
            .Background(ChatSurfaceBrush)
            .WithBorder(BrowserConstants.SurfaceStrokeColorDefaultBrush);

        var provider = LinkerAiCredentialService.SelectedProvider;
        var keyButtonText = LinkerAiCredentialService.HasAnyApiKey()
            ? provider.DisplayName
            : "Add key";

        return FlexColumn(
            FlexRow(
                TextBlock("Ask about history, favorites, saved tabs, collections, browser data, or the local MCP tool catalog.")
                    .TextWrapping(TextWrapping.Wrap)
                    .Opacity(0.78)
                    .Flex(grow: 1, basis: 0),
                Button(keyButtonText, Props.OnOpenAiKeyDialog)
                    .AutomationName("Add Linker provider key")
                    .ToolTip("Add or update a Linker provider API key")
                    .Padding(10, 4)
                    .CornerRadius(999)) with
            {
                ColumnGap = 8
            },
            quickPrompts,
            Border(
                ScrollViewer(messageList)
                    .Set(scrollViewer =>
                    {
                        _messagesScrollViewer = scrollViewer;
                        scrollViewer.HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled;
                        scrollViewer.HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled;
                    })
                    .Padding(10)
                    .MinHeight(0)
                    .Flex(grow: 1, basis: 0))
                .CornerRadius(14)
                .Background(ChatSurfaceBrush)
                .WithBorder(BrowserConstants.SurfaceStrokeColorDefaultBrush)
                .MinHeight(0)
                .Flex(grow: 1, basis: 0),
            input) with
        {
            RowGap = 10
        };
    }

    private void ScrollMessagesToBottom()
    {
        var scrollViewer = _messagesScrollViewer;
        var version = Interlocked.Increment(ref _scrollRequestVersion);

        if (scrollViewer is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < 5; attempt++)
            {
                await Task.Delay(attempt == 0 ? 20 : 70);

                if (version != Volatile.Read(ref _scrollRequestVersion))
                {
                    return;
                }

                scrollViewer.DispatcherQueue.TryEnqueue(() =>
                {
                    scrollViewer.UpdateLayout();

                    if (_messagesBottomAnchor is not null)
                    {
                        _messagesBottomAnchor.StartBringIntoView(new BringIntoViewOptions
                        {
                            AnimationDesired = true,
                            VerticalAlignmentRatio = 1
                        });
                    }

                    scrollViewer.ChangeView(null, scrollViewer.ScrollableHeight, null, disableAnimation: false);
                });
            }
        });
    }

    private Element BuildMessageBubble(ChatPanelMessage message)
    {
        Element messageContent = message.IsUser
            ? TextBlock(message.Text)
                .TextWrapping(TextWrapping.Wrap)
                .FontSize(MessageFontSize)
            : message.IsThinking
                ? CreateThinkingIndicator()
            : Border(Markdown(message.Text, new MarkdownOptions
                    {
                        LinkBuilder = (children, uri) =>
                            Button(GetMarkdownLinkText(children, uri), () => OpenMarkdownLink(uri)).AutomationName("Linker url" + uri.AbsoluteUri)
                                .TextLink()
                    })
                    .HAlign(HorizontalAlignment.Stretch)
                    .MaxWidth(AssistantBubbleMaxWidth - 24))
                .Padding(0)
                .HAlign(HorizontalAlignment.Stretch)
                .MaxWidth(AssistantBubbleMaxWidth - 24);

        var content = FlexColumn(
            FlexRow(
                CreateAvatarPill(message.IsUser),
                TextBlock(message.IsUser ? "You" : "Linker")
                    .FontSize(MetaFontSize)
                    .Opacity(0.84)
                    .VAlign(VerticalAlignment.Center)
                    .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold)) with
            {
                ColumnGap = 7
            },
            messageContent) with
        {
            RowGap = 8
        };

        return Border(content)
            .Padding(message.IsUser ? 10 : 12)
            .CornerRadius(message.IsUser ? 18 : 16)
            .Background(message.IsUser ? BrowserConstants.AccentFillColorTertiaryBrush : AssistantBubbleBrush)
            .WithBorder(BrowserConstants.SurfaceStrokeColorDefaultBrush)
            .HAlign(message.IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Stretch)
            .MaxWidth(message.IsUser ? UserBubbleMaxWidth : AssistantBubbleMaxWidth);
    }

    private void OpenMarkdownLink(Uri uri)
    {
        var target = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.ToString();

        if (!string.IsNullOrWhiteSpace(target))
        {
            Props.OnOpenLinkInNewTab(target);
        }
    }

    private static string GetMarkdownLinkText(IReadOnlyList<Element> children, Uri uri)
    {
        var text = string.Join(string.Empty, children.Select(ExtractElementText));

        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return uri.IsAbsoluteUri && !string.IsNullOrWhiteSpace(uri.Host)
            ? uri.Host
            : uri.ToString();
    }

    private static string ExtractElementText(Element element)
    {
        var elementType = element.GetType();

        foreach (var propertyName in new[] { "Text", "Content", "Label" })
        {
            var property = elementType.GetProperty(propertyName);
            var value = property?.GetValue(element);
            if (value is string text && !string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return string.Empty;
    }

    private CommandCenterChatContext GetChatContext() =>
        Props.GetChatContext();

    private static Element CreateThinkingIndicator()
    {
        return FlexRow(
            ProgressRing()
                .Width(18)
                .Height(18)
                .Set(progressRing => progressRing.IsActive = true),
            TextBlock("Thinking...")
                .TextWrapping(TextWrapping.Wrap)
                .FontSize(MessageFontSize)
                .Opacity(0.9)
                .Set(AnimateThinkingText)) with
        {
            ColumnGap = 8
        };
    }

    private static void AnimateThinkingText(TextBlock textBlock)
    {
        if (textBlock.Tag is Microsoft.UI.Xaml.Media.Animation.Storyboard)
        {
            return;
        }

        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = 0.45,
            To = 1,
            Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromMilliseconds(700)),
            AutoReverse = true,
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };

        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, textBlock);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Opacity");

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(animation);
        textBlock.Tag = storyboard;
        storyboard.Begin();
    }

    private static Element CreateAvatarPill(bool isUser)
    {
        if (!isUser)
        {
            return Border(
                Image(StoreLogoAssetPath)
                    .AutomationName("Linker")
                    .Width(18)
                    .Height(18)
                    .HAlign(HorizontalAlignment.Center)
                    .VAlign(VerticalAlignment.Center))
                .Width(24)
                .Height(24)
                .CornerRadius(12)
                .Background(BrowserConstants.LayerFillDefaultBrush)
                .WithBorder(BrowserConstants.SurfaceStrokeColorDefaultBrush);
        }

        return Border(
            Image(LinkerSvgAssetPath)
                .AutomationName("You")
                .Width(18)
                .Height(18)
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center))
            .Width(24)
            .Height(24)
            .CornerRadius(12)
            .Background(isUser ? BrowserConstants.AccentFillColorDefaultBrush : BrowserConstants.LayerFillDefaultBrush)
            .WithBorder(BrowserConstants.SurfaceStrokeColorDefaultBrush);
    }

    private static ButtonElement CreatePromptPill(string label, Action onClick)
    {
        return Button(label, onClick)
            .AutomationName(label)
            .Padding(12, 6)
            .CornerRadius(999)
            .Background(BrowserConstants.LayerFillDefaultBrush)
            .WithBorder(BrowserConstants.SurfaceStrokeColorDefaultBrush);
    }
}
