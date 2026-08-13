using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using LinkScape.Browser;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.AppLifecycle;
using Windows.Services.Store;

namespace LinkScape.Services.Application;

internal static class AppUpdateService
{
    internal const string AutomaticDailyChecksSettingKey = "updates.automaticDailyChecks";
    internal const string LastSeenPackageVersionSettingKey = "updates.lastSeenPackageVersion";
    internal const string WhatsNewPageUrl = "https://linker.local/Updates/index.html";
    private const string LastDailyCheckSettingKey = "updates.lastDailyCheckUtc";
    private static readonly TimeSpan DailyCheckInterval = TimeSpan.FromDays(1);
    private static readonly SemaphoreSlim CheckLock = new(1, 1);
    private static WeakReference<FrameworkElement>? _updateFlyoutAnchor;

    internal static void RegisterFlyoutAnchor(FrameworkElement anchor) =>
        _updateFlyoutAnchor = new WeakReference<FrameworkElement>(anchor);

    internal static Task CheckForUpdatesIfDueAsync()
    {
        if (!IsAutomaticDailyCheckEnabled() || !IsDailyCheckDue(DateTimeOffset.UtcNow))
        {
            return Task.CompletedTask;
        }

        return CheckForUpdatesAsync(isManualCheck: false);
    }

    internal static Task CheckForUpdatesNowAsync() => CheckForUpdatesAsync(isManualCheck: true);

    internal static bool TryGetUnseenPackageVersion(out string version)
    {
        if (!TryGetCurrentPackageVersion(out version))
        {
            return false;
        }

        var lastSeenVersion = SettingsService.GetValue(LastSeenPackageVersionSettingKey)?.Trim();
        if (string.IsNullOrWhiteSpace(lastSeenVersion))
        {
            // Versions released before this feature did not persist a package
            // version. Treat a missing value as unseen so existing users receive
            // the first bundled What's New page after upgrading.
            return true;
        }

        return !string.Equals(lastSeenVersion, version, StringComparison.OrdinalIgnoreCase);
    }

    internal static string GetCurrentPackageVersionDisplay() =>
        TryGetCurrentPackageVersion(out var version) ? version : "Development";

    internal static string GetWhatsNewPageUrl(string? version = null)
    {
        var displayVersion = string.IsNullOrWhiteSpace(version)
            ? GetCurrentPackageVersionDisplay()
            : version.Trim();

        return $"{WhatsNewPageUrl}?version={Uri.EscapeDataString(displayVersion)}";
    }

    internal static void MarkPackageVersionSeen(string version)
    {
        if (!string.IsNullOrWhiteSpace(version))
        {
            SettingsService.SetValue(LastSeenPackageVersionSettingKey, version.Trim());
        }
    }

    private static bool TryGetCurrentPackageVersion(out string version)
    {
        version = string.Empty;

        try
        {
            version = FormatVersion(Windows.ApplicationModel.Package.Current.Id.Version);
            return true;
        }
        catch
        {
            // Package.Current is unavailable for unpackaged development runs.
            return false;
        }
    }

    private static async Task CheckForUpdatesAsync(bool isManualCheck)
    {
        if (!await CheckLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var xamlRoot = global::LinkScape.Application.MainWindowActivation.GetXamlRoot();
            if (xamlRoot is null)
            {
                return;
            }

            var storeContext = StoreContext.GetDefault();
            var hwnd = global::LinkScape.Application.MainWindowActivation.Hwnd;
            if (hwnd != 0)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(storeContext, hwnd);
            }

            var availableUpdates = await storeContext.GetAppAndOptionalStorePackageUpdatesAsync();
            SettingsService.SetValue(
                LastDailyCheckSettingKey,
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));

            if (availableUpdates.Count == 0)
            {
                if (isManualCheck)
                {
                    ShowStatusFlyout(
                        xamlRoot,
                        "LinkScape is up to date",
                        "You already have the newest Store version installed.");
                }

                return;
            }

            var updates = availableUpdates.ToArray();
            var latestVersion = FormatVersion(updates[0].Package.Id.Version);
            if (!await ShowUpdatePromptAsync(xamlRoot, latestVersion))
            {
                return;
            }

            await DownloadAndInstallAsync(storeContext, updates, xamlRoot);
        }
        catch (Exception ex)
        {
            if (isManualCheck && global::LinkScape.Application.MainWindowActivation.GetXamlRoot() is { } xamlRoot)
            {
                ShowStatusFlyout(
                    xamlRoot,
                    "Could not check for updates",
                    ex.Message);
            }

            // Store APIs can be unavailable for unpackaged development builds or
            // accounts that are not signed in. Automatic checks remain silent so
            // they never interrupt normal browser startup in those environments.
        }
        finally
        {
            CheckLock.Release();
        }
    }

    private static bool IsAutomaticDailyCheckEnabled() =>
        !bool.TryParse(SettingsService.GetValue(AutomaticDailyChecksSettingKey), out var enabled) || enabled;

    private static bool IsDailyCheckDue(DateTimeOffset now)
    {
        var rawValue = SettingsService.GetValue(LastDailyCheckSettingKey);
        return !DateTimeOffset.TryParse(
                   rawValue,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.RoundtripKind,
                   out var lastCheck) ||
               now - lastCheck >= DailyCheckInterval;
    }

    private static async Task<bool> ShowUpdatePromptAsync(XamlRoot xamlRoot, string version)
    {
        var choice = new TaskCompletionSource<bool>();
        var updateNowButton = CreateActionButton("Update now", isPrimary: true);
        var laterButton = CreateActionButton("Later");
        var flyout = CreateBrandedFlyout(
            "Update available",
            $"LinkScape {version} is ready to download from Microsoft Store.",
            new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { updateNowButton, laterButton }
            });

        updateNowButton.Click += (_, _) =>
        {
            choice.TrySetResult(true);
            flyout.Hide();
        };
        laterButton.Click += (_, _) =>
        {
            choice.TrySetResult(false);
            flyout.Hide();
        };
        flyout.Closed += (_, _) => choice.TrySetResult(false);

        ShowBySettingsButton(flyout, xamlRoot);
        return await choice.Task;
    }

    private static async Task DownloadAndInstallAsync(
        StoreContext storeContext,
        IReadOnlyList<StorePackageUpdate> updates,
        XamlRoot xamlRoot)
    {
        var statusText = new TextBlock
        {
            Text = "Preparing the LinkScape update…",
            TextWrapping = TextWrapping.Wrap
        };
        var detailText = new TextBlock
        {
            Text = "Windows may ask you to confirm the download and installation.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.72
        };
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            IsIndeterminate = true
        };
        var flyout = CreateBrandedFlyout(
            "Updating LinkScape",
            null,
            statusText,
            progressBar,
            detailText);
        var updateInProgress = true;
        flyout.Closing += (_, args) => args.Cancel = updateInProgress;

        ShowBySettingsButton(flyout, xamlRoot);

        try
        {
            var operation = storeContext.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
            operation.Progress = (_, status) =>
            {
                global::LinkScape.Application.MainWindowActivation.TryEnqueue(() =>
                {
                    var totalProgress = Math.Clamp(status.TotalDownloadProgress * 100d, 0d, 100d);
                    var isDownloading = status.PackageUpdateState is
                        StorePackageUpdateState.Pending or StorePackageUpdateState.Downloading;

                    progressBar.IsIndeterminate = isDownloading;
                    if (!isDownloading)
                    {
                        progressBar.Value = status.PackageUpdateState == StorePackageUpdateState.Deploying
                            ? Math.Clamp((totalProgress - 80d) / 20d * 100d, 0d, 100d)
                            : totalProgress;
                    }

                    statusText.Text = GetProgressText(status.PackageUpdateState);
                    detailText.Text = status.PackageDownloadSizeInBytes > 0
                        ? $"{FormatMegabytes(status.PackageBytesDownloaded)} of {FormatMegabytes(status.PackageDownloadSizeInBytes)} downloaded"
                        : status.PackageUpdateState == StorePackageUpdateState.Deploying
                            ? $"Installing… {progressBar.Value:0}%"
                            : "Downloading from Microsoft Store…";
                });
            };

            var result = await operation;
            progressBar.IsIndeterminate = false;
            updateInProgress = false;

            if (result.OverallState == StorePackageUpdateState.Completed)
            {
                progressBar.Value = 100;
                statusText.Text = "Update installed — restart required";
                detailText.Text = "Restart LinkScape to load the updated application files.";
                flyout.Hide();
                await ShowRestartPromptAsync(xamlRoot);
                return;
            }

            statusText.Text = result.OverallState == StorePackageUpdateState.Canceled
                ? "Update postponed"
                : "The update could not be completed";
            detailText.Text = GetResultDetail(result.OverallState);
        }
        catch (Exception ex)
        {
            updateInProgress = false;
            progressBar.IsIndeterminate = false;
            statusText.Text = "The update could not be completed";
            detailText.Text = ex.Message;
        }
    }

    private static async Task ShowRestartPromptAsync(XamlRoot xamlRoot)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "LinkScape update installed",
            Content = new StackPanel
            {
                Width = 390,
                Spacing = 12,
                Children =
                {
                    CreateBrandHeader("Restart required"),
                    new TextBlock
                    {
                        Text = "Restart LinkScape now to use the updated version, or choose Later and restart when convenient.",
                        TextWrapping = TextWrapping.Wrap,
                        Opacity = 0.78
                    }
                }
            },
            PrimaryButtonText = "Restart now",
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        // On success Restart terminates this process synchronously and launches
        // the newly installed package. A returned value is always a failure.
        var failureReason = AppInstance.Restart(string.Empty);
        ShowStatusFlyout(
            xamlRoot,
            "LinkScape could not restart",
            $"Windows reported {failureReason}. Close and reopen LinkScape to finish the update.");
    }

    private static Flyout CreateBrandedFlyout(string title, string? description, params UIElement[] content)
    {
        var panel = new StackPanel
        {
            Width = 390,
            Spacing = 12
        };
        panel.Children.Add(CreateBrandHeader(title));

        if (!string.IsNullOrWhiteSpace(description))
        {
            panel.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.78
            });
        }

        foreach (var element in content)
        {
            panel.Children.Add(element);
        }

        return new Flyout
        {
            Placement = FlyoutPlacementMode.BottomEdgeAlignedRight,
            FlyoutPresenterStyle = CreateFlyoutPresenterStyle(),
            Content = panel
        };
    }

    private static UIElement CreateBrandHeader(string title) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        Children =
        {
            new Image
            {
                Source = new BitmapImage(new Uri("ms-appx:///Assets/StoreLogo.png")),
                Width = 34,
                Height = 34
            },
            new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new TextBlock
                    {
                        Text = "LINKSCAPE BROWSER",
                        FontSize = 10,
                        CharacterSpacing = 120,
                        Opacity = 0.68
                    },
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 18,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    }
                }
            }
        }
    };

    private static Button CreateActionButton(string label, bool isPrimary = false)
    {
        var button = new Button
        {
            Content = label,
            MinWidth = 96,
            Height = 34,
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        if (isPrimary)
        {
            button.Background = BrowserConstants.AccentFillColorDefaultBrush;
        }

        return button;
    }

    private static Style CreateFlyoutPresenterStyle() => new(typeof(FlyoutPresenter))
    {
        Setters =
        {
            new Setter(Control.BackgroundProperty, new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xF5, 0x20, 0x20, 0x24))),
            new Setter(Control.ForegroundProperty, new SolidColorBrush(Microsoft.UI.Colors.White)),
            new Setter(Control.BorderBrushProperty, BrowserConstants.AccentFillColorDefaultBrush),
            new Setter(Control.BorderThicknessProperty, new Thickness(1)),
            new Setter(Control.CornerRadiusProperty, new CornerRadius(18)),
            new Setter(Control.PaddingProperty, new Thickness(16))
        }
    };

    private static void ShowStatusFlyout(XamlRoot xamlRoot, string title, string description)
    {
        var closeButton = CreateActionButton("Close", isPrimary: true);
        var flyout = CreateBrandedFlyout(title, description, closeButton);
        closeButton.Click += (_, _) => flyout.Hide();
        ShowBySettingsButton(flyout, xamlRoot);
    }

    private static void ShowBySettingsButton(Flyout flyout, XamlRoot xamlRoot)
    {
        if (_updateFlyoutAnchor is not null &&
            _updateFlyoutAnchor.TryGetTarget(out var anchor) &&
            anchor.XamlRoot == xamlRoot)
        {
            flyout.ShowAt(anchor);
            return;
        }

        if (xamlRoot.Content is FrameworkElement fallback)
        {
            flyout.ShowAt(fallback);
        }
    }

    private static string GetProgressText(StorePackageUpdateState state) => state switch
    {
        StorePackageUpdateState.Downloading => "Downloading the update…",
        StorePackageUpdateState.Deploying => "Installing the update…",
        StorePackageUpdateState.Completed => "Finishing the update…",
        _ => "Preparing the update…"
    };

    private static string GetResultDetail(StorePackageUpdateState state) => state switch
    {
        StorePackageUpdateState.Canceled => "You can try again when LinkScape checks tomorrow.",
        StorePackageUpdateState.ErrorLowBattery => "Connect this device to power, then try again.",
        StorePackageUpdateState.ErrorWiFiRequired => "Connect to Wi-Fi, then try again.",
        StorePackageUpdateState.ErrorWiFiRecommended => "A Wi-Fi connection is recommended for this update.",
        _ => "Please try again after restarting LinkScape."
    };

    private static string FormatVersion(Windows.ApplicationModel.PackageVersion version) =>
        $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

    private static string FormatMegabytes(ulong bytes) =>
        $"{bytes / 1024d / 1024d:0.0} MB";
}
