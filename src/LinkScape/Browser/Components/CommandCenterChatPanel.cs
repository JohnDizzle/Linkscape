using System.Threading;
using System.Threading.Tasks;
using LinkScape.Browser;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Reactor.Markdown;
using System.Text.RegularExpressions;
using Windows.ApplicationModel.DataTransfer;

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
    private const int MaxExtractedLinks = 3;
    private const string DefaultSearchProviderSettingKey = "browser.search.defaultProvider";

    private sealed record ChatPanelMessage(string Text, bool IsUser, bool IsError = false, bool IsThinking = false);

    private readonly DispatcherQueue? _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    private Microsoft.UI.Xaml.Controls.ScrollViewer? _messagesScrollViewer;
    private FrameworkElement? _messagesBottomAnchor;
    private AutoSuggestBox? _promptAutoSuggestBox;
    private string _latestPromptText = string.Empty;
    private string? _providerPageIdentity;
    private int _scrollRequestVersion;
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
        var previousProviderResponseId = UseState<string?>(null, threadSafe: true);
        var searchProviderName = BrowserSearchProviders.GetByKey(
            SettingsService.GetValueOrDefault(
                DefaultSearchProviderSettingKey,
                BrowserSearchProviders.DefaultProviderKey)).DisplayName;

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
            var nextValue = value ?? string.Empty;
            _latestPromptText = nextValue;
            prompt.Set(nextValue);
            arePromptSuggestionsOpen.Set(false);
        }

        async void SubmitPrompt(string? submittedText = null)
        {
            var text = !string.IsNullOrWhiteSpace(submittedText)
                ? submittedText.Trim()
                : !string.IsNullOrWhiteSpace(_latestPromptText)
                    ? _latestPromptText.Trim()
                    : (prompt.Value ?? string.Empty).Trim();
            var conversationBeforePrompt = messages.Value;

            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            _latestPromptText = string.Empty;
            prompt.Set(string.Empty);
            arePromptSuggestionsOpen.Set(false);
            RefocusPromptInput();

            if (TryOpenPriorMessageLink(text, messages.Value, out var openedMessage))
            {
                SetMessages(
                    messages.Value
                        .Concat(
                        [
                            new ChatPanelMessage(text, true),
                            new ChatPanelMessage(openedMessage, false)
                        ])
                        .ToArray());
                return;
            }

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
                var baseContext = Props.GetChatContext();
                var pageIdentity = BuildPageIdentity(baseContext);
                var effectivePreviousResponseId = string.Equals(
                    _providerPageIdentity,
                    pageIdentity,
                    StringComparison.Ordinal)
                        ? previousProviderResponseId.Value
                        : null;
                var response = await CommandCenterChatService.SubmitAsync(
                    text,
                    GetChatContext(effectivePreviousResponseId, conversationBeforePrompt),
                    CancellationToken.None);
                LocalMcpDiagnostics.Trace("ChatUI", $"Response received. IsError={response.IsError}, TextLength={response.Text?.Length ?? 0}");
                if (!string.IsNullOrWhiteSpace(response.ProviderResponseId))
                {
                    previousProviderResponseId.Set(response.ProviderResponseId);
                    _providerPageIdentity = pageIdentity;
                }
                else
                {
                    previousProviderResponseId.Set(null);
                    _providerPageIdentity = null;
                }
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
            previousProviderResponseId.Set(null);
            _providerPageIdentity = null;
            SetMessages(
            [
                new ChatPanelMessage("## Linker is ready\nSession cleared. Ask about history, favorites, or today's activity.", false)
            ]);
        }

        var quickPrompts = FlexRow(
            CreatePromptMenuPill("Tabs", ApplyPrompt,
                ("Summarize saved tabs", "summarize my saved tabs"),
                ("Show active tabs", "show my active tabs"),
                ("Find a tab", $"find {searchProviderName} in my tabs")),
            CreatePromptMenuPill("Collections", ApplyPrompt,
                ("Show collections", "show my collections"),
                ("Show a collection", "what's in the personal collection?"),
                ("Add current page", "add current page to collection personal")),
            CreatePromptMenuPill("Favorites", ApplyPrompt,
                ("Summarize favorites", "summarize my favorites"),
                ("Search favorites", $"search for {searchProviderName} in my favorites"),
                ("Show recent favorites", "show my recent favorites")),
            CreatePromptMenuPill("History", ApplyPrompt,
                ("Today's activity", "history tell me today's active sites"),
                ("Recent history", "show my recent history"),
                ("Most visited", "what sites are most active in my history"),
                ("Search history", $"search history for {searchProviderName}"),
                ("This month", "show me history for this month"),
                ("This year", "show me history for this year")),
            CreatePromptMenuPill("Status", ApplyPrompt,
                ("MCP tools", "mcp status")),
            CreatePromptMenuPill("Group", ApplyPrompt,
                ("Group by day", "report history by day"),
                ("Group by month", "report history by month"),
                ("Group by year", "report history by year"),
                ("Include favorites", "report history by month with favorites"),
                ("Include collections", "report history by month with collections"),
                ("Favorites and collections", "report history by month with favorites and collections"),
                ("Archived by year", "break down archived history by year"))) with
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
                    var nextValue = value ?? string.Empty;
                    _latestPromptText = nextValue;
                    prompt.Set(nextValue);
                    if (!string.IsNullOrWhiteSpace(nextValue))
                    {
                        arePromptSuggestionsOpen.Set(false);
                    }
                }, submitted => SubmitPrompt(submitted))
                    .AutomationName("CommandCenterChatPrompt")
                    .SuggestionChosen(value =>
                    {
                        var nextValue = value ?? string.Empty;
                        _latestPromptText = nextValue;
                        prompt.Set(nextValue);
                        arePromptSuggestionsOpen.Set(false);
                        })
                        .IsSuggestionListOpen(false)
                        .Set(ConfigurePromptContextMenu)
                        .Flex(grow: 1, basis: 0) with
                {
                    PlaceholderText = isSending.Value ? "Analyzing browser data..." : "Ask about history, favorites, today, active sites, or mcp status...",
                    Suggestions = []
                },
                Button("Ask", () => SubmitPrompt())
                    .AutomationName("CommandCenterChatSubmit"),
                Button("Clear", ClearChat)) with
            {
                ColumnGap = 8
            })
            .Padding(8)
            .CornerRadius(12)
            .Background(BrowserMaterialTheme.ChatSurfaceBrush)
            .WithBorder(BrowserMaterialTheme.GlassStrokeBrush);

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
                .Background(BrowserMaterialTheme.ChatSurfaceBrush)
                .WithBorder(BrowserMaterialTheme.GlassStrokeBrush)
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
            : BuildAssistantMarkdownContent(message.Text);

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
            .Background(message.IsUser ? BrowserMaterialTheme.ChatUserBubbleBrush : BrowserMaterialTheme.ChatAssistantBubbleBrush)
            .WithBorder(BrowserMaterialTheme.GlassStrokeBrush)
            .HAlign(message.IsUser ? HorizontalAlignment.Right : HorizontalAlignment.Stretch)
            .MaxWidth(message.IsUser ? UserBubbleMaxWidth : AssistantBubbleMaxWidth)
            .Set(border => border.ContextFlyout = CreateMessageContextFlyout(message.Text));
    }

    private static MenuFlyout CreateMessageContextFlyout(string messageText)
    {
        var flyout = new MenuFlyout();
        var copyItem = new MenuFlyoutItem
        {
            Text = "Copy message"
        };
        copyItem.Click += (_, _) => CopyMessageToClipboard(messageText);
        flyout.Items.Add(copyItem);
        return flyout;
    }

    private static void CopyMessageToClipboard(string messageText)
    {
        var package = new DataPackage
        {
            RequestedOperation = DataPackageOperation.Copy
        };
        package.SetText(messageText ?? string.Empty);
        Clipboard.SetContent(package);
    }

    private void ConfigurePromptContextMenu(AutoSuggestBox autoSuggestBox)
    {
        _promptAutoSuggestBox = autoSuggestBox;

        void AttachToEditor()
        {
            var editor = FindVisualDescendant<TextBox>(autoSuggestBox);
            if (editor is not null)
            {
                editor.ContextFlyout = CreatePromptEditingFlyout(editor);
            }
        }

        autoSuggestBox.Loaded += (_, _) => AttachToEditor();
        AttachToEditor();
    }

    private void RefocusPromptInput()
    {
        void ApplyFocus()
        {
            _promptAutoSuggestBox?.Focus(FocusState.Programmatic);
        }

        if (_dispatcherQueue is not null)
        {
            _dispatcherQueue.TryEnqueue(ApplyFocus);
            return;
        }

        ApplyFocus();
    }

    private static MenuFlyout CreatePromptEditingFlyout(TextBox editor)
    {
        var flyout = new MenuFlyout();

        var cutItem = new MenuFlyoutItem { Text = "Cut" };
        cutItem.Click += (_, _) => editor.CutSelectionToClipboard();
        flyout.Items.Add(cutItem);

        var copyItem = new MenuFlyoutItem { Text = "Copy" };
        copyItem.Click += (_, _) => editor.CopySelectionToClipboard();
        flyout.Items.Add(copyItem);

        var pasteItem = new MenuFlyoutItem { Text = "Paste" };
        pasteItem.Click += (_, _) => editor.PasteFromClipboard();
        flyout.Items.Add(pasteItem);

        flyout.Items.Add(new MenuFlyoutSeparator());

        var selectAllItem = new MenuFlyoutItem { Text = "Select all" };
        selectAllItem.Click += (_, _) => editor.SelectAll();
        flyout.Items.Add(selectAllItem);

        return flyout;
    }

    private static T? FindVisualDescendant<T>(DependencyObject parent)
        where T : DependencyObject
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualDescendant<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private void OpenMarkdownLink(Uri uri)
    {
        var target = uri.IsAbsoluteUri ? uri.AbsoluteUri : uri.ToString();

        if (!string.IsNullOrWhiteSpace(target))
        {
            Props.OnOpenLinkInNewTab(target);
        }
    }

    private Element BuildAssistantMarkdownContent(string markdown)
    {
        var links = ExtractMarkdownLinks(markdown)
            .DistinctBy(link => NormalizeLinkUrl(link.Url))
            .ToArray();
        var markdownContent = Border(Markdown(markdown, new MarkdownOptions
            {
                LinkBuilder = (children, uri) =>
                    TextBlock(GetMarkdownLinkText(children, uri))
            })
            .HAlign(HorizontalAlignment.Stretch)
            .MaxWidth(AssistantBubbleMaxWidth - 24))
            .Padding(0)
            .HAlign(HorizontalAlignment.Stretch)
            .MaxWidth(AssistantBubbleMaxWidth - 24);

        if (links.Length == 0)
        {
            return markdownContent;
        }

        var visibleLinks = links.Take(MaxExtractedLinks).ToArray();
        var overflowCount = Math.Max(0, links.Length - visibleLinks.Length);

        return FlexColumn(
            markdownContent,
            Border(
                FlexColumn(
                    TextBlock(overflowCount > 0
                            ? $"Open links from this answer ({overflowCount} more in text)"
                            : "Open links from this answer")
                        .FontSize(MetaFontSize)
                        .Opacity(0.78),
                    FlexColumn(
                        visibleLinks.Select(BuildExtractedLinkButton).ToArray()) with
                    {
                        RowGap = 6
                    }) with
                {
                    RowGap = 6
                })
                .Padding(8, 7)
                .CornerRadius(10)
                .Background(BrowserMaterialTheme.GlassFillBrush)
                .WithBorder(BrowserMaterialTheme.GlassStrokeBrush)
                .MaxWidth(AssistantBubbleMaxWidth - 24)
                .HAlign(HorizontalAlignment.Stretch)) with
        {
            RowGap = 8
        };
    }

    private ButtonElement BuildExtractedLinkButton(MarkdownLink link)
    {
        return Button(
                (FlexRow(
                    TextBlock(BrowserConstants.GlyphGo)
                        .FontFamily(BrowserConstants.IconFontFamily)
                        .FontSize(12)
                        .Opacity(0.82),
                    VStack(1,
                        TextBlock(GetLinkActionLabel(link.Label))
                            .FontSize(13)
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .TextWrapping(TextWrapping.NoWrap)
                            .Set(textBlock =>
                            {
                                textBlock.MaxLines = 1;
                                textBlock.MinWidth = 0;
                            }),
                        TextBlock(GetLinkHost(link.Url))
                            .FontSize(11)
                            .Opacity(0.68)
                            .TextTrimming(TextTrimming.CharacterEllipsis)
                            .TextWrapping(TextWrapping.NoWrap)
                            .Set(textBlock =>
                            {
                                textBlock.MaxLines = 1;
                                textBlock.MinWidth = 0;
                            })
                    )
                    .MinWidth(0)
                    .Flex(grow: 1, basis: 0)
                ) with
                {
                    ColumnGap = 8
                })
                .HAlign(HorizontalAlignment.Stretch),
                () => Props.OnOpenLinkInNewTab(link.Url))
            .WithKey(link.Url)
            .AutomationName("Open " + link.Label)
            .ToolTip(link.Url)
            .Padding(9, 6)
            .CornerRadius(8)
            .Background(BrowserMaterialTheme.PillFillBrush)
            .WithBorder(BrowserMaterialTheme.GlassStrokeBrush)
            .HAlign(HorizontalAlignment.Stretch)
            .MinWidth(0);
    }

    private static string GetLinkActionLabel(string label)
    {
        label = UnescapeMarkdownLinkText(label);
        label = Regex.Replace(label, @"\s+", " ").Trim();
        label = Regex.Replace(label, @"^\(\d+\)\s*", string.Empty).Trim();
        const int maxLength = 64;
        return label.Length <= maxLength
            ? label
            : label[..Math.Max(0, maxLength - 1)].TrimEnd() + "…";
    }

    private static string UnescapeMarkdownLinkText(string? value) =>
        (value ?? string.Empty)
            .Replace("\\|", "|", StringComparison.Ordinal)
            .Replace("\\[", "[", StringComparison.Ordinal)
            .Replace("\\]", "]", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);

    private static string GetLinkHost(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
        {
            return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                ? uri.Host[4..]
                : uri.Host;
        }

        return url;
    }

    private bool TryOpenPriorMessageLink(
        string prompt,
        IReadOnlyList<ChatPanelMessage> messages,
        out string openedMessage)
    {
        openedMessage = string.Empty;
        if (!IsOpenPriorLinkPrompt(prompt))
        {
            return false;
        }

        var query = ExtractOpenPriorLinkQuery(prompt);
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var link = messages
            .Where(message => !message.IsUser)
            .Reverse()
            .SelectMany(message => ExtractMarkdownLinks(message.Text))
            .FirstOrDefault(candidate => IsLinkMatch(candidate, query));

        if (link is null)
        {
            return false;
        }

        Props.OnOpenLinkInNewTab(link.Url);
        openedMessage = $"Opened **[{EscapeMarkdownLinkText(link.Label)}](<{EscapeMarkdownLinkUrl(link.Url)}>)** in LinkScape.";
        return true;
    }

    private static bool IsOpenPriorLinkPrompt(string prompt) =>
        Regex.IsMatch(prompt ?? string.Empty, @"\b(?:go\s+to|open|visit|take\s+me\s+to)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string ExtractOpenPriorLinkQuery(string prompt)
    {
        var match = Regex.Match(
            prompt ?? string.Empty,
            @"\b(?:go\s+to|open|visit|take\s+me\s+to)\s+(?:the\s+)?(?<query>.+)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        return match.Success
            ? match.Groups["query"].Value.Trim().Trim('.', ',', ';', ':', '!', '?', '"', '\'')
            : string.Empty;
    }

    private static bool IsLinkMatch(MarkdownLink link, string query)
    {
        query = NormalizeLinkSearchText(query);
        if (string.IsNullOrWhiteSpace(query))
        {
            return false;
        }

        var label = NormalizeLinkSearchText(link.Label);
        var url = NormalizeLinkSearchText(link.Url);
        var host = Uri.TryCreate(link.Url, UriKind.Absolute, out var uri)
            ? NormalizeLinkSearchText(uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase))
            : string.Empty;

        return label.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            url.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            host.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            query.Contains(label, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLinkSearchText(string value) =>
        Regex.Replace(value ?? string.Empty, @"\s+", " ")
            .Trim()
            .ToLowerInvariant();

    private static string NormalizeLinkUrl(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return value.TrimEnd('/').ToLowerInvariant();
        }

        var builder = new UriBuilder(uri)
        {
            Host = uri.Host.ToLowerInvariant()
        };

        return builder.Uri.AbsoluteUri.TrimEnd('/').ToLowerInvariant();
    }

    private sealed record MarkdownLink(string Label, string Url);

    private static IEnumerable<MarkdownLink> ExtractMarkdownLinks(string markdown)
    {
        foreach (Match match in Regex.Matches(
            markdown ?? string.Empty,
            @"\[(?<label>[^\]]+)\]\((?:<)?(?<url>https?://[^>)]+)(?:>)?\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            var label = match.Groups["label"].Value.Trim();
            var url = match.Groups["url"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(url))
            {
                yield return new MarkdownLink(label, url);
            }
        }
    }

    private static string EscapeMarkdownLinkText(string value) =>
        (value ?? string.Empty)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal);

    private static string EscapeMarkdownLinkUrl(string value) =>
        (value ?? string.Empty).Replace(">", "%3E", StringComparison.Ordinal);

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

    private CommandCenterChatContext GetChatContext(
        string? previousProviderResponseId,
        IReadOnlyList<ChatPanelMessage> messages)
    {
        var context = Props.GetChatContext();
        return context with
        {
            PreviousProviderResponseId = previousProviderResponseId,
            ConversationTurns = messages
                .Skip(1)
                .Where(message => !message.IsThinking && !message.IsError && !string.IsNullOrWhiteSpace(message.Text))
                .TakeLast(12)
                .Select(message => new CommandCenterChatTurn(message.IsUser ? "user" : "assistant", message.Text))
                .ToArray()
        };
    }

    private static string BuildPageIdentity(CommandCenterChatContext context)
    {
        var url = (context.ActiveUrl ?? string.Empty).Trim();
        var normalizedUrl = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.AbsoluteUri
            : url;
        return $"{context.ActiveTabId ?? string.Empty}|{normalizedUrl}";
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
                .Background(BrowserMaterialTheme.PillFillBrush)
                .WithBorder(BrowserMaterialTheme.GlassStrokeBrush);
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
            .Background(BrowserMaterialTheme.PillFillBrush)
            .WithBorder(BrowserMaterialTheme.GlassStrokeBrush);
    }

    private static ButtonElement CreatePromptMenuPill(
        string label,
        Action<string> applyPrompt,
        params (string Label, string Prompt)[] prompts)
    {
        var flyout = new MenuFlyout();
        foreach (var prompt in prompts)
        {
            var item = new MenuFlyoutItem
            {
                Text = prompt.Label
            };
            item.Click += (_, _) => applyPrompt(prompt.Prompt);
            flyout.Items.Add(item);
        }

        return Button(label, () => { })
            .AutomationName(label)
            .ToolTip($"Show {label.ToLowerInvariant()} prompts")
            .Padding(12, 6)
            .CornerRadius(6)
            .Background(BrowserMaterialTheme.PillFillBrush)
            .WithBorder(BrowserMaterialTheme.GlassStrokeBrush)
            .Set(button => button.Flyout = flyout);
    }
}
