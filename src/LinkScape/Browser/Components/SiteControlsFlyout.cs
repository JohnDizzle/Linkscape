using LinkScape.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using System;
using System.Threading.Tasks;

namespace LinkScape.Browser.Components;

internal static class SiteControlsFlyout
{
    private sealed record PermissionChoice(string Label, CoreWebView2PermissionState State);

    private static readonly PermissionChoice[] PermissionChoices =
    [
        new("Ask", CoreWebView2PermissionState.Default),
        new("Allow", CoreWebView2PermissionState.Allow),
        new("Block", CoreWebView2PermissionState.Deny)
    ];

    internal static Flyout Create(BrowserTab selectedTab, BrowserWebViewHostController controller)
    {
        var flyout = new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft,
            FlyoutPresenterStyle = CreateFlyoutPresenterStyle(),
            Content = CreateLoadingContent(selectedTab)
        };

        flyout.Opened += async (_, _) => await LoadAsync(flyout, selectedTab, controller);
        return flyout;
    }

    private static async Task LoadAsync(
        Flyout flyout,
        BrowserTab selectedTab,
        BrowserWebViewHostController controller)
    {
        flyout.Content = CreateLoadingContent(selectedTab);

        try
        {
            var snapshot = await controller.GetSiteControlsAsync();
            flyout.Content = snapshot.IsAvailable
                ? CreateSiteControlsContent(flyout, snapshot, controller)
                : CreateUnavailableContent(snapshot.Error ?? "Site controls are unavailable for this page.");
        }
        catch (Exception ex)
        {
            flyout.Content = CreateUnavailableContent($"Site controls could not be loaded: {ex.Message}");
        }
    }

    private static FrameworkElement CreateSiteControlsContent(
        Flyout flyout,
        SiteControlsSnapshot snapshot,
        BrowserWebViewHostController controller)
    {
        var root = new StackPanel
        {
            Width = 380,
            Spacing = 12,
            Padding = new Thickness(2)
        };

        root.Children.Add(CreateHeader(snapshot));
        root.Children.Add(CreateConnectionCard(snapshot));
        root.Children.Add(CreateSectionTitle("Permissions"));

        var permissionPanel = new StackPanel { Spacing = 6 };

        foreach (var permission in snapshot.Permissions)
        {
            permissionPanel.Children.Add(CreatePermissionRow(permission, controller));
        }

        root.Children.Add(permissionPanel);

        root.Children.Add(CreateSectionTitle("Zoom"));
        root.Children.Add(CreateZoomControl(snapshot, controller));

        root.Children.Add(CreateSectionTitle("Site data"));
        root.Children.Add(CreateSiteDataCard(snapshot));

        var actionGrid = new Grid { ColumnSpacing = 8 };
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actionGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var resetButton = CreateTextButton("Reset permissions", "\uE777");
        resetButton.Click += async (_, _) =>
        {
            flyout.Hide();
            if (!await ConfirmAsync(
                "Reset site permissions?",
                $"Restore all permissions for {snapshot.Host} to Ask?",
                "Reset"))
            {
                return;
            }

            await RunActionAsync(
                controller.ResetSitePermissionsAsync,
                "Site permissions were reset.",
                "Could not reset site permissions");
        };

        var clearButton = CreateTextButton("Clear site data", "\uE74D");
        clearButton.Click += async (_, _) =>
        {
            flyout.Hide();
            if (!await ConfirmAsync(
                "Clear site data?",
                $"Delete cookies and locally stored data for {snapshot.Host}? You may be signed out of this site.",
                "Clear data"))
            {
                return;
            }

            await RunActionAsync(
                controller.ClearSiteDataAsync,
                "Site data was cleared and the page reloaded.",
                "Could not clear site data");
        };

        Microsoft.UI.Xaml.Controls.Grid.SetColumn(resetButton, 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(clearButton, 1);
        actionGrid.Children.Add(resetButton);
        actionGrid.Children.Add(clearButton);
        root.Children.Add(actionGrid);

        return new ScrollViewer
        {
            Content = root,
            MaxHeight = 680,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
    }

    private static FrameworkElement CreateHeader(SiteControlsSnapshot snapshot)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var favicon = new Image
        {
            Width = 24,
            Height = 24,
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(
                new Uri(BrowserUrl.GetFaviconUrl(snapshot.Origin), UriKind.Absolute))
        };
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(favicon, 0);

        var labels = new StackPanel { Spacing = 2 };
        labels.Children.Add(new TextBlock
        {
            Text = snapshot.Host,
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        labels.Children.Add(new TextBlock
        {
            Text = snapshot.Origin,
            FontSize = 12,
            Opacity = 0.68,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(labels, 1);

        grid.Children.Add(favicon);
        grid.Children.Add(labels);
        return grid;
    }

    private static FrameworkElement CreateConnectionCard(SiteControlsSnapshot snapshot)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 9
        };
        row.Children.Add(new FontIcon
        {
            Glyph = snapshot.IsSecure ? "\uE72E" : "\uE7BA",
            FontSize = 15,
            Foreground = new SolidColorBrush(snapshot.IsSecure
                ? Microsoft.UI.Colors.MediumSeaGreen
                : Microsoft.UI.Colors.Orange)
        });
        row.Children.Add(new TextBlock
        {
            Text = snapshot.ConnectionLabel,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });

        return CreateFlyoutCard(row, padding: new Thickness(12, 9, 12, 9));
    }

    private static Border CreateFlyoutCard(UIElement child, Thickness? padding = null)
    {
        return new Border
        {
            Child = child,
            Padding = padding ?? new Thickness(12),
            CornerRadius = new CornerRadius(10),
            Background = BrowserMaterialTheme.GlassFillBrush,
            BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            Shadow = new ThemeShadow(),
            Translation = new System.Numerics.Vector3(0, 1, 10)
        };
    }

    private static FrameworkElement CreatePermissionRow(
        SitePermissionSetting permission,
        BrowserWebViewHostController controller)
    {
        var grid = new Grid
        {
            ColumnSpacing = 12,
            Padding = new Thickness(4, 2, 0, 2)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = permission.DisplayName,
            VerticalAlignment = VerticalAlignment.Center
        };

        var selector = new ComboBox
        {
            MinWidth = 96,
            HorizontalAlignment = HorizontalAlignment.Right,
            Background = BrowserMaterialTheme.PillFillBrush,
            BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };

        foreach (var choice in PermissionChoices)
        {
            selector.Items.Add(new ComboBoxItem { Content = choice.Label, Tag = choice.State });
        }

        var currentState = permission.State;
        selector.SelectedIndex = Array.FindIndex(PermissionChoices, choice => choice.State == permission.State);
        selector.SelectionChanged += async (_, _) =>
        {
            if (selector.SelectedItem is not ComboBoxItem { Tag: CoreWebView2PermissionState state } ||
                state == currentState)
            {
                return;
            }

            selector.IsEnabled = false;
            try
            {
                await controller.SetSitePermissionAsync(permission.Kind, state);
                currentState = state;
                BrowserNoticeService.Show(
                    $"{permission.DisplayName} is now {GetStateLabel(state).ToLowerInvariant()} for this site. The page was reloaded.",
                    "info");
            }
            catch (Exception ex)
            {
                selector.SelectedIndex = Array.FindIndex(PermissionChoices, choice => choice.State == currentState);
                BrowserNoticeService.Show($"Could not update {permission.DisplayName.ToLowerInvariant()}: {ex.Message}");
            }
            finally
            {
                selector.IsEnabled = true;
            }
        };

        Microsoft.UI.Xaml.Controls.Grid.SetColumn(label, 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(selector, 1);
        grid.Children.Add(label);
        grid.Children.Add(selector);
        return grid;
    }

    private static FrameworkElement CreateZoomControl(
        SiteControlsSnapshot snapshot,
        BrowserWebViewHostController controller)
    {
        var panel = new StackPanel { Spacing = 8 };
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var valueLabel = new TextBlock
        {
            Text = $"{snapshot.ZoomPercent}%",
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        };

        var resetButton = CreateIconButton("\uE72C", "Reset zoom to 100%");
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(valueLabel, 0);
        Microsoft.UI.Xaml.Controls.Grid.SetColumn(resetButton, 1);
        header.Children.Add(valueLabel);
        header.Children.Add(resetButton);

        var slider = new Slider
        {
            Minimum = SiteControlsService.MinZoomPercent,
            Maximum = SiteControlsService.MaxZoomPercent,
            StepFrequency = 10,
            SmallChange = 10,
            LargeChange = 25,
            Value = snapshot.ZoomPercent,
            TickFrequency = 50,
            TickPlacement = TickPlacement.BottomRight,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Foreground = BrowserMaterialTheme.LoadingStrokeBrush
        };
        AutomationProperties.SetName(slider, "Page zoom");

        slider.ValueChanged += (sender, args) =>
        {
            var nextPercent = SiteControlsService.ClampZoomPercent((int)Math.Round(args.NewValue / 10) * 10);
            valueLabel.Text = $"{nextPercent}%";
            _ = controller.SetZoomPercentAsync(nextPercent);
        };

        resetButton.Click += (sender, args) =>
        {
            slider.Value = 100;
            valueLabel.Text = "100%";
            _ = controller.SetZoomPercentAsync(100);
        };

        panel.Children.Add(header);
        panel.Children.Add(slider);

        return CreateFlyoutCard(panel, padding: new Thickness(12, 9, 12, 9));
    }

    private static FrameworkElement CreateSiteDataCard(SiteControlsSnapshot snapshot)
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = SiteControlsService.FormatStorageUsage(snapshot.StorageUsageBytes),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock
        {
            Text = snapshot.CookieCount == 1 ? "1 cookie" : $"{snapshot.CookieCount} cookies",
            FontSize = 12,
            Opacity = 0.68
        });

        return CreateFlyoutCard(panel, padding: new Thickness(12, 9, 12, 9));
    }

    private static TextBlock CreateSectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 12,
        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Opacity = 0.72
    };

    private static Button CreateTextButton(string text, string glyph)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(10, 7, 10, 7),
            Background = BrowserMaterialTheme.PillFillBrush,
            BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8)
        };
        SetButtonLabel(button, text, glyph);
        return button;
    }

    private static Button CreateIconButton(string glyph, string tooltip)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
            Width = 32,
            Height = 28,
            Padding = new Thickness(0),
            Background = BrowserMaterialTheme.PillFillBrush,
            BorderBrush = BrowserMaterialTheme.GlassStrokeBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(button, tooltip);
        return button;
    }

    private static Style CreateFlyoutPresenterStyle()
    {
        return new Style(typeof(FlyoutPresenter))
        {
            Setters =
            {
                new Setter(Control.BackgroundProperty, BrowserMaterialTheme.ChatSurfaceBrush),
                new Setter(Control.ForegroundProperty, new SolidColorBrush(Microsoft.UI.Colors.White)),
                new Setter(Control.BorderBrushProperty, BrowserMaterialTheme.GlassStrokeBrush),
                new Setter(Control.BorderThicknessProperty, new Thickness(1)),
                new Setter(Control.CornerRadiusProperty, new CornerRadius(14)),
                new Setter(Control.PaddingProperty, new Thickness(12))
            }
        };
    }

    private static void SetButtonLabel(Button button, string text, string glyph)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        panel.Children.Add(new FontIcon { Glyph = glyph, FontSize = 14 });
        panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        button.Content = panel;
    }

    private static FrameworkElement CreateLoadingContent(BrowserTab selectedTab)
    {
        var panel = new StackPanel
        {
            Width = 340,
            Spacing = 10,
            Padding = new Thickness(8)
        };
        panel.Children.Add(new TextBlock
        {
            Text = Uri.TryCreate(selectedTab.Url, UriKind.Absolute, out var uri) ? uri.Host : "Site controls",
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new ProgressRing { IsActive = true, Width = 24, Height = 24 });
        panel.Children.Add(new TextBlock { Text = "Loading site permissions…", Opacity = 0.68 });
        return panel;
    }

    private static FrameworkElement CreateUnavailableContent(string message)
    {
        var panel = new StackPanel
        {
            Width = 340,
            Spacing = 10,
            Padding = new Thickness(8)
        };
        panel.Children.Add(new TextBlock
        {
            Text = "Site controls",
            FontSize = 17,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        });
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Opacity = 0.72 });
        return panel;
    }

    private static async Task<bool> ConfirmAsync(string title, string message, string primaryButtonText)
    {
        var xamlRoot = global::LinkScape.Application.MainWindowActivation.GetXamlRoot();
        if (xamlRoot is null)
        {
            return false;
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = primaryButtonText,
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static async Task RunActionAsync(Func<Task> action, string successMessage, string failurePrefix)
    {
        try
        {
            await action();
            BrowserNoticeService.Show(successMessage, "info");
        }
        catch (Exception ex)
        {
            BrowserNoticeService.Show($"{failurePrefix}: {ex.Message}");
        }
    }

    private static string GetStateLabel(CoreWebView2PermissionState state) => state switch
    {
        CoreWebView2PermissionState.Allow => "Allowed",
        CoreWebView2PermissionState.Deny => "Blocked",
        _ => "Ask"
    };
}
