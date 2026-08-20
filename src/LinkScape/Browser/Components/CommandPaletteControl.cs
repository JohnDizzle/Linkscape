using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace LinkScape.Browser.Components;

internal sealed class CommandPaletteControl : UserControl
{
    private readonly AutoSuggestBox _filterBox;
    private readonly ContentControl _headerHost;
    private readonly ContentControl _sourceHost;
    private readonly ContentControl _resultsHost;
    private readonly Border _surface;
    private readonly Action<string> _onFilterChanged;
    private readonly Action<string> _onSubmitted;
    private readonly Action _onDismissed;
    private bool _suppressFilterChanged;

    internal CommandPaletteControl(
        Action<string> onFilterChanged,
        Action<string> onSubmitted,
        Action onDismissed)
    {
        _onFilterChanged = onFilterChanged;
        _onSubmitted = onSubmitted;
        _onDismissed = onDismissed;

        _filterBox = new AutoSuggestBox
        {
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            QueryIcon = new FontIcon
            {
                FontFamily = BrowserConstants.IconFontFamily,
                Glyph = BrowserConstants.GlyphMagnifyGlass,
                FontSize = 14
            }
        };
        _filterBox.TextChanged += OnFilterTextChanged;
        _filterBox.QuerySubmitted += OnFilterSubmitted;
        _filterBox.KeyDown += OnKeyDown;

        _headerHost = CreateStretchingHost();
        _sourceHost = CreateStretchingHost();
        _resultsHost = CreateStretchingHost();

        var content = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                _headerHost,
                _filterBox,
                _sourceHost,
                _resultsHost
            }
        };

        _surface = new Border
        {
            MaxHeight = 560,
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(16),
            Background = BrowserMaterialTheme.ChatSurfaceBrush,
            BorderBrush = BrowserMaterialTheme.SelectedStrokeBrush,
            BorderThickness = new Thickness(1),
            Child = content,
            Shadow = new Microsoft.UI.Xaml.Media.ThemeShadow(),
            Translation = new System.Numerics.Vector3(0, 2, 12)
        };

        Content = _surface;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        KeyDown += OnKeyDown;
    }

    private static ContentControl CreateStretchingHost() => new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        HorizontalContentAlignment = HorizontalAlignment.Stretch
    };

    internal void Update(
        double width,
        string filterText,
        string placeholder,
        UIElement header,
        UIElement sources,
        UIElement results)
    {
        _surface.Width = width;
        _filterBox.PlaceholderText = placeholder;
        SetFilterText(filterText);
        _headerHost.Content = header;
        _sourceHost.Content = sources;
        _resultsHost.Content = results;
    }

    internal void FocusFilter()
    {
        _filterBox.Focus(FocusState.Programmatic);
    }

    private void SetFilterText(string value)
    {
        if (string.Equals(_filterBox.Text, value, StringComparison.Ordinal))
        {
            return;
        }

        _suppressFilterChanged = true;
        try
        {
            _filterBox.Text = value;
        }
        finally
        {
            _suppressFilterChanged = false;
        }
    }

    private void OnFilterTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (!_suppressFilterChanged && args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            _onFilterChanged(sender.Text);
        }
    }

    private void OnFilterSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _onSubmitted(args.QueryText);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != Windows.System.VirtualKey.Escape)
        {
            return;
        }

        args.Handled = true;
        _onDismissed();
    }
}
