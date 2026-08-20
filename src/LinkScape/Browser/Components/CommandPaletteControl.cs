using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace LinkScape.Browser.Components;

internal sealed class CommandPaletteControl : UserControl
{
    private readonly TextBox _filterBox;
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

        _filterBox = new TextBox
        {
            Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            IsSpellCheckEnabled = false,
            IsTextPredictionEnabled = false
        };
        _filterBox.TextChanged += OnFilterTextChanged;
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
        if (_filterBox.FocusState != FocusState.Unfocused)
        {
            return;
        }

        _filterBox.Focus(FocusState.Programmatic);
        MoveCaretToEnd();
    }

    private void MoveCaretToEnd()
    {
        _filterBox.SelectionStart = _filterBox.Text.Length;
        _filterBox.SelectionLength = 0;
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
            MoveCaretToEnd();
        }
        finally
        {
            _suppressFilterChanged = false;
        }
    }

    private void OnFilterTextChanged(object sender, TextChangedEventArgs args)
    {
        if (!_suppressFilterChanged)
        {
            _onFilterChanged(_filterBox.Text);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == Windows.System.VirtualKey.Enter)
        {
            args.Handled = true;
            _onSubmitted(_filterBox.Text);
            return;
        }

        if (args.Key != Windows.System.VirtualKey.Escape)
        {
            return;
        }

        args.Handled = true;
        _onDismissed();
    }
}
