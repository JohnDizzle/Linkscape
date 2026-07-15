using LinkScape.Browser;
using LinkScape.Models;

namespace Browser.Components;

internal sealed class BrowserTitleBarController
{
    internal Action<string, bool>? SetAddressTextCore { get; set; }

    public void SetAddressText(string value, bool preserveUserEdit = false) =>
        SetAddressTextCore?.Invoke(value, preserveUserEdit);
}

internal sealed record BrowserTitleBarProps(
    BrowserTitleBarController Controller,
    BrowserTab SelectedTab,
    bool IsTabsCollapsed,
    bool CanGoBack,
    bool CanGoForward,
    Action OnToggleTabs,
    Action OnBack,
    Action OnRefresh,
    Action OnForward,
    Action<string> OnSubmitAddress,
    Action<string> OnNavigateCurrentTab,
    string SelectedSearchProviderKey,
    IReadOnlyList<BrowserSearchProvider> SearchProviders,
    Action<string> OnSelectSearchProvider,
    Action OnToggleFavorite,
    Action OnAddTab,
    Action OnCloseTab);

internal sealed class BrowserTitleBar : Component<BrowserTitleBarProps>
{
    private Microsoft.UI.Xaml.Controls.AutoSuggestBox? _addressBox;
    private string _addressBarText = string.Empty;
    private bool _isAddressBarEditing;
    private bool _suppressAddressBoxTextChanged;
    private bool _isInitialized;

    public override Element Render()
    {
        if (!_isInitialized)
        {
            _addressBarText = Props.SelectedTab.Url;
            _isInitialized = true;
        }

        Props.Controller.SetAddressTextCore = SetAddressBarText;

        return BrowserChrome.BuildTitleBar(
            Props.SelectedTab,
            _addressBarText,
            Props.IsTabsCollapsed,
            Props.CanGoBack,
            Props.CanGoForward,
            Props.OnToggleTabs,
            Props.OnBack,
            Props.OnRefresh,
            Props.OnForward,
            SetAddressBarDraft,
            Props.OnSubmitAddress,
            AttachAddressBox,
            Props.OnNavigateCurrentTab,
            Props.SelectedSearchProviderKey,
            Props.SearchProviders,
            Props.OnSelectSearchProvider,
            Props.OnToggleFavorite,
            Props.OnAddTab,
            Props.OnCloseTab);
    }

    private void AttachAddressBox(Microsoft.UI.Xaml.Controls.AutoSuggestBox addressBox)
    {
        _addressBox = addressBox;

        if (!string.Equals(addressBox.Text, _addressBarText, StringComparison.Ordinal))
        {
            _suppressAddressBoxTextChanged = true;
            addressBox.Text = _addressBarText;
            _suppressAddressBoxTextChanged = false;
        }
    }

    private void SetAddressBarDraft(string value)
    {
        if (_suppressAddressBoxTextChanged)
        {
            return;
        }

        _isAddressBarEditing = true;
        _addressBarText = value;
    }

    private void SetAddressBarText(string value, bool preserveUserEdit = false)
    {
        var nextValue = value ?? string.Empty;
        _addressBarText = nextValue;

        if (preserveUserEdit && _isAddressBarEditing)
        {
            return;
        }

        _isAddressBarEditing = false;

        if (_addressBox is null || string.Equals(_addressBox.Text, nextValue, StringComparison.Ordinal))
        {
            return;
        }

        _suppressAddressBoxTextChanged = true;
        _addressBox.Text = nextValue;
        _suppressAddressBoxTextChanged = false;
    }
}
