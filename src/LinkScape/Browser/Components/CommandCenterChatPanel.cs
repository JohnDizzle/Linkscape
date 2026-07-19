using LinkScape.Browser;

namespace Browser.Components;

internal sealed class CommandCenterChatPanel : Component
{
    private const string StoreLogoAssetPath = "ms-appx:///Assets/StoreLogo.png";
    private const double MessageFontSize = 15;
    private const double MetaFontSize = 12;

    private sealed record ChatPanelMessage(string Text, bool IsUser, bool IsError = false, bool IsThinking = false);

    public override Element Render()
    {
        var prompt = UseState(string.Empty);
        var messages = UseState<IReadOnlyList<ChatPanelMessage>>(
        [
            new ChatPanelMessage(
                "## Hi, I'm Linker\nAsk about local history, favorites, today's active sites, or type `mcp status` to see the local tools I can use.",
                false)
        ], threadSafe: true);
        var isSending = UseState(false, threadSafe: true);

        void ApplyPrompt(string value)
        {
            prompt.Set(value);
        }

        async void SubmitPrompt()
        {
            var text = prompt.Value.Trim();

            if (string.IsNullOrWhiteSpace(text) || isSending.Value)
            {
                return;
            }

            prompt.Set(string.Empty);
            isSending.Set(true);
            var pendingMessages = messages.Value
                .Concat(
                [
                    new ChatPanelMessage(text, true),
                    new ChatPanelMessage("Thinking…", false, IsThinking: true)
                ])
                .ToArray();

            messages.Set(pendingMessages);

            try
            {
                var response = await CommandCenterChatService.SubmitAsync(text);
                LocalMcpDiagnostics.Trace("ChatUI", $"Response received. IsError={response.IsError}, TextLength={response.Text?.Length ?? 0}");
                var answer = string.IsNullOrWhiteSpace(response.Text)
                    ? "The chat service returned an empty answer. Check the MCP trace for the selected tool response."
                    : response.Text;
                messages.Set([..pendingMessages.Where(message => !message.IsThinking), new ChatPanelMessage(answer, false, response.IsError)]);
            }
            catch (Exception ex)
            {
                LocalMcpDiagnostics.Trace("ChatUI", $"Submit failed: {ex}");
                messages.Set([..pendingMessages.Where(message => !message.IsThinking), new ChatPanelMessage($"Unable to answer: {ex.Message}", false, true)]);
            }
            finally
            {
                isSending.Set(false);
            }
        }

        void ClearChat()
        {
            messages.Set(
            [
                new ChatPanelMessage("## Linker is ready\nSession cleared. Ask about history, favorites, or today's activity.", false)
            ]);
        }

        var quickPrompts = FlexRow(
            CreatePromptPill("Today", () => ApplyPrompt("history tell me today's active sites")),
            CreatePromptPill("Favorites", () => ApplyPrompt("summarize my favorites")),
            CreatePromptPill("Most visited", () => ApplyPrompt("what sites are most active in my history")),
            CreatePromptPill("Tools", () => ApplyPrompt("mcp status"))) with
        {
            ColumnGap = 6
        };

        var messageList = FlexColumn(messages.Value.Select(BuildMessageBubble).ToArray()) with
        {
            RowGap = 8
        };

        var input = Border(
            FlexRow(
                AutoSuggestBox(prompt.Value, prompt.Set, _ => SubmitPrompt())
                    .AutomationName("CommandCenterChatPrompt")
                    .Flex(grow: 1, basis: 0) with
                {
                    PlaceholderText = isSending.Value ? "Analyzing browser data..." : "Ask about history, favorites, today, active sites, or mcp status...",
                    Suggestions = string.IsNullOrWhiteSpace(prompt.Value)
                        ? new[]
                        {
                            "history tell me today's active sites",
                            "summarize my favorites",
                            "what sites are most active in my history",
                            "mcp status"
                        }
                        : []
                },
                Button(isSending.Value ? "..." : "Ask", SubmitPrompt).AutomationName("CommandCenterChatSubmit")
                    .IsEnabled(!isSending.Value),
                Button("Clear", ClearChat)) with
            {
                ColumnGap = 8
            })
            .Padding(8)
            .CornerRadius(12)
            .Background(BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush)
            .WithBorder(BrowserConstants.SurfaceStrokeColorDefaultBrush);

        return FlexColumn(
            TextBlock("Ask about history, favorites, today's activity, browser data, or the local MCP tool catalog.")
                .TextWrapping(TextWrapping.Wrap)
                .Opacity(0.78),
            quickPrompts,
            Border(
                ScrollViewer(messageList)
                    .Padding(10)
                    .MinHeight(0)
                    .Flex(grow: 1, basis: 0))
                .CornerRadius(14)
                .Background(BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush)
                .WithBorder(BrowserConstants.SurfaceStrokeColorDefaultBrush)
                .MinHeight(0)
                .Flex(grow: 1, basis: 0),
            input) with
        {
            RowGap = 10
        };
    }

    private static Element BuildMessageBubble(ChatPanelMessage message)
    {
        Element messageContent = message.IsUser
            ? TextBlock(message.Text)
                .TextWrapping(TextWrapping.Wrap)
                .FontSize(MessageFontSize)
            : message.IsThinking
                ? CreateThinkingIndicator()
            : Component<MarkdownTextBlockView, MarkdownTextBlockViewProps>(
                new MarkdownTextBlockViewProps(message.Text, message.IsError));

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
            .Background(message.IsUser ? BrowserConstants.AccentFillColorTertiaryBrush : BrowserConstants.LayerOnMicaBaseAltFillColorDefaultBrush)
            .WithBorder(BrowserConstants.SurfaceStrokeColorDefaultBrush)
            .HAlign(message.IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Stretch)
            .MaxWidth(message.IsUser ? 320 : double.PositiveInfinity);
    }

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
        return Border(
            TextBlock(isUser ? "U" : "L")
                .FontSize(11)
                .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.Bold)
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
            .Padding(12, 6)
            .CornerRadius(999)
            .Background(BrowserConstants.LayerFillDefaultBrush)
            .WithBorder(BrowserConstants.SurfaceStrokeColorDefaultBrush);
    }
}
