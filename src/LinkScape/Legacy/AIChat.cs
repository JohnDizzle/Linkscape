public class AIChat : Component
{
    private sealed record ChatMessage(string Text, bool IsUser, bool IsThinking = false);

    public override Element Render()
    {
        var starterMessages = new List<ChatMessage>
        {
            new(
                """

                * 🌐 Navigate and search the web.
                * 🖱️ Interact with the browser.
                * 🚀 Manage programs and windows.

                ## What would you like to do?
                """,
                false)
        };

        var quickPrompts = new[]
        {
            "Show my favorites",
            "Manage windows: switch, stack, or cascade",
            "Categorize apps installed on this PC",
            "Open Windows applications",
            "What's trending today for me?",
            "Show browser interaction help",
            "Show scrolling help",
            "Show navigation options"
        };

        var prompt = UseState(string.Empty);
        var messages = UseState<IReadOnlyList<ChatMessage>>(starterMessages);
        var isPinned = UseState(false);
        var showHistory = UseState(false);
        var showHelp = UseState(false);
        var isSending = UseState(false);

        void ApplyPrompt(string value)
        {
            prompt.Set(value);
        }

        void SubmitPrompt()
        {
            var text = prompt.Value.Trim();

            if (string.IsNullOrWhiteSpace(text) || isSending.Value)
            {
                return;
            }

            messages.Set(
            [
                ..messages.Value,
                new ChatMessage(text, true),
                new ChatMessage("Thinking...", false, true)
            ]);

            prompt.Set(string.Empty);
            isSending.Set(true);

            // Placeholder for real service integration.
            messages.Set(
            [
                ..messages.Value,
                new ChatMessage(text, true),
                new ChatMessage("Thinking...", false, true)
            ]);
        }

        void StopOrNewChat()
        {
            if (isSending.Value)
            {
                isSending.Set(false);

                messages.Set(messages.Value
                    .Select(message => message.IsThinking
                        ? message with { Text = "Response stopped by user.", IsThinking = false }
                        : message)
                    .ToArray());

                return;
            }

            messages.Set(
            [
                ..messages.Value,
                new ChatMessage("Session cleared. Hi, I'm Web Dive. Ask me anything.", false)
            ]);
        }

        return CreateFlyout(
            prompt.Value,
            prompt.Set,
            messages.Value,
            quickPrompts,
            isPinned.Value,
            isPinned.Set,
            showHistory.Value,
            showHistory.Set,
            showHelp.Value,
            showHelp.Set,
            isSending.Value,
            SubmitPrompt,
            StopOrNewChat,
            ApplyPrompt);
    }

    private Element CreateFlyout(
        string prompt,
        Action<string> setPrompt,
        IReadOnlyList<ChatMessage> messages,
        IReadOnlyList<string> quickPrompts,
        bool isPinned,
        Action<bool> setIsPinned,
        bool showHistory,
        Action<bool> setShowHistory,
        bool showHelp,
        Action<bool> setShowHelp,
        bool isSending,
        Action submitPrompt,
        Action stopOrNewChat,
        Action<string> applyPrompt)
    {
        var header = Border(
            FlexRow(
                TextBlock("🌐") with
                {
                    FontSize = 28,
                },

                Heading("Web Dive's AI Agent").Flex(grow: 1, basis: 0),

                Button("Help", () => setShowHelp(!showHelp))
            ) with
            {
                ColumnGap = 10
            })
            .Padding(16)
            .Flex(shrink: 0);

        var helpPanel = showHelp
            ? Border(
                TextBlock(
                    """
                    Quick commands:

                    Navigation
                    • "Go back", "Go forward", "Go home", or "Reload".
                    • Paste or type a valid URL to navigate directly.
                    • "Open favorite <name>" to go to a saved favorite.

                    Page interaction
                    • "Click <label>", "Select <item>", "Play <media>", "Summarize this page".

                    Shortcut
                    • CTRL + SHIFT + W to open this assistant quickly.
                    """) with
                {
                    TextWrapping = TextWrapping.Wrap
                })
                .Padding(16, 0, 16, 8)
                .Flex(shrink: 0)
            : null;

        var chatHistory = CreateChatHistoryUI(messages, showHistory);

        var input = CreateInputUI(
            prompt,
            setPrompt,
            quickPrompts,
            isPinned,
            setIsPinned,
            showHistory,
            setShowHistory,
            isSending,
            submitPrompt,
            stopOrNewChat,
            applyPrompt);

        var footer = Border(
            FlexRow(
                TextBlock("⌨"),
                TextBlock("CTRL + SHIFT + W to open")
            ) with
            {
                ColumnGap = 6
            })
            .Padding(0, 0, 0, 8)
            .Flex(shrink: 0);

        return helpPanel is null
            ? FlexColumn(header, chatHistory, input, footer)
            : FlexColumn(header, helpPanel, chatHistory, input, footer);
    }

    private Element CreateChatHistoryUI(
        IReadOnlyList<ChatMessage> messages,
        bool showHistory)
    {
        var historyPanel = showHistory
            ? Border(
                TextBlock("History / logs view placeholder") with
                {
                    TextWrapping = TextWrapping.Wrap
                })
                .Padding(16, 0, 16, 12)
                .Flex(shrink: 0)
            : null;

        var messageList = ScrollViewer(
            FlexColumn(messages.Select(CreateBubbleBorder).ToArray()) with
            {
                RowGap = 4
            })
            .Padding(20, 8, 20, 24)
            .Flex(grow: 1, basis: 0);

        return historyPanel is null
            ? messageList
            : FlexColumn(historyPanel, messageList).Flex(grow: 1, basis: 0);
    }

    private Element CreateInputUI(
        string prompt,
        Action<string> setPrompt,
        IReadOnlyList<string> quickPrompts,
        bool isPinned,
        Action<bool> setIsPinned,
        bool showHistory,
        Action<bool> setShowHistory,
        bool isSending,
        Action submitPrompt,
        Action stopOrNewChat,
        Action<string> applyPrompt)
    {
        var toolbar = FlexRow(
            Button("📜 Logs", () => setShowHistory(!showHistory)),
            Button("🔍 Scan", () => applyPrompt("Scan this page")),
            Button(isSending ? "🛑" : "📝 New", stopOrNewChat),
            Button(isPinned ? "📍 Unpin" : "📌 Pin", () => setIsPinned(!isPinned)),
            Button("🧭 Navigation", () => applyPrompt("Show navigation options"))
        ) with
        {
            ColumnGap = 8
        };

        var promptMenu = FlexRow(
            Button("⭐ Favorites", () => applyPrompt("Show my favorites")),
            Button("🗔 Windows", () => applyPrompt("Manage windows: switch, stack, or cascade")),
            Button("⚙️ Apps", () => applyPrompt("Open Windows applications")),
            Button("📢 Trending", () => applyPrompt("What's trending today for me?"))
        ) with
        {
            ColumnGap = 8
        };

        var inputBox = Border(
            FlexRow(
                AutoSuggestBox(prompt, setPrompt)
                    .AutomationName("PromptInput")
                    .Flex(grow: 1, basis: 0) with
                                {
                                    PlaceholderText = isSending ? "Waiting for response..." : "Ask a question...",
                                    Suggestions = string.IsNullOrWhiteSpace(prompt)
                        ? quickPrompts.ToArray()
                        : []
                },

                Button("Ask", submitPrompt),
                Button("🎤 Mic")
            ) with
            {
                ColumnGap = 8
            })
            .Padding(12, 10, 12, 10)
            .IsVisible(!isSending)
            with
            {
                CornerRadius = 16,
                BorderThickness = 1.5
            };

        return Border(
            FlexColumn(
                toolbar,
                promptMenu,
                inputBox
            ) with
            {
                RowGap = 8
            })
            .Padding(20, 0, 20, 12)
            .Flex(shrink: 0);
    }

    private Element CreateBubbleBorder(ChatMessage message)
    {
        var content = message.IsThinking
            ? (Element)(FlexRow(
                TextBlock("⏳"),
                TextBlock(message.Text) with
                {
                    TextWrapping = TextWrapping.Wrap
                }) with
            {
                ColumnGap = 8
            })
            : (Element)(TextBlock(message.Text) with
            {
                TextWrapping = TextWrapping.Wrap
            });

        return Border(content)
            .Padding(16)
            with
            {
                CornerRadius = 14,
                Margin = message.IsUser
                    ? new Thickness(48, 0, 0, 12)
                    : new Thickness(0, 0, 48, 12),
                BorderThickness = 1.0
            };
    }
}
