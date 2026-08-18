using LinkScape.Browser;
using LinkScape.Browser.Messages;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;

var commandLineArgs = Environment.GetCommandLineArgs();

ConfigureWebView2Environment();

if (await LocalMcpServerService.TryRunAsync(commandLineArgs))
{
    return;
}

if (!await ActivationRoutingService.InitializeAsync())
{
    return;
}

TabPersistenceService.EnsureDatabase();
HistoryPersistenceService.EnsureDatabase();
SettingsService.EnsureDatabase();
FavoritesService.EnsureDatabase();
TabCollectionService.EnsureDatabase();
InstalledWebAppService.EnsureDatabase();
const string WebView2AdditionalBrowserArgumentsKey = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";
const string WebView2SingleSignOnPrimaryAccountArgument = "--allow-single-sign-on-os-primary-account";
const string WebView2VisualHostingForOwnedWindowsKey = "WEBVIEW2_USE_VISUAL_HOSTING_FOR_OWNED_WINDOWS";

var services = new ServiceCollection();
_ = services.AddSingleton<WeakReferenceMessenger>();
_ = services.AddSingleton<IMessenger, WeakReferenceMessenger>(provider =>
    provider.GetRequiredService<WeakReferenceMessenger>());
LinkScapeServiceProvider.Initialize(services.BuildServiceProvider());

ReactorApp.Run<App>("LinkScape",
    configure: host =>
    {
        MainWindowActivation.Register(host.Window);
        var restored = false;

        host.Window.Activated += (s, e) =>
        {
            if (restored || e.WindowActivationState == WindowActivationState.Deactivated)
            {  
                return;
            }

            restored = true;
            MainWindowActivation.RestoreWindowPlacement();
            _ = AppJumpListService.RefreshAsync(reportUnavailable: true);
            
        };

        host.Window.Closed += (_, _) =>
        {
            MainWindowActivation.SaveWindowPlacement();
            AppWindowRegistry.CloseAll();
        };
    });

static void ConfigureWebView2Environment()
{
    Environment.SetEnvironmentVariable(
        WebView2VisualHostingForOwnedWindowsKey,
        "1",
        EnvironmentVariableTarget.Process);

    var existing = Environment.GetEnvironmentVariable(WebView2AdditionalBrowserArgumentsKey);
    if (existing?.Contains(WebView2SingleSignOnPrimaryAccountArgument, StringComparison.OrdinalIgnoreCase) == true)
    {
        return;
    }

    var arguments = string.IsNullOrWhiteSpace(existing)
        ? WebView2SingleSignOnPrimaryAccountArgument
        : $"{existing} {WebView2SingleSignOnPrimaryAccountArgument}";

    Environment.SetEnvironmentVariable(
        WebView2AdditionalBrowserArgumentsKey,
        arguments,
        EnvironmentVariableTarget.Process);
}

class App : Component
{
    private const string BackdropGradientPresetSettingKey = "ui.backdrop.gradientPreset";
    private const int StartupSplashDurationMilliseconds = 1010;
    private const string StoreLogoAssetPath = "ms-appx:///Assets/StoreLogo.png";
    private static readonly object UnhandledExceptionSyncRoot = new();
    private static bool _unhandledExceptionHandlerRegistered;
    private bool _errorListenerRegistered;
    private bool _settingsListenerRegistered;
    private bool _fullScreenPresentationMessengerRegistered;
    private Action<bool>? _setFullScreenPresentationState;
    private bool _startupSplashDismissScheduled;
    private bool _dailyUpdateCheckScheduled;
    private static IMessenger Messenger => LinkScapeServiceProvider.GetRequiredService<IMessenger>();

    public override Element Render()
    {
        var backdropGradientPreset = UseState(
            AppBackdropBrushes.NormalizePreset(
                SettingsService.GetValueOrDefault(
                    BackdropGradientPresetSettingKey,
                    AppBackdropBrushes.DefaultPreset)));
        var fatalError = UseState<Exception?>(AppErrorStateService.CurrentError, threadSafe: true);
        var isShowingStartupSplash = UseState(true, threadSafe: true);
        var isFullScreenPresentationActive = UseState(
            MainWindowActivation.IsFullScreenPresentationActive,
            threadSafe: true);

        RegisterSettingsListener(backdropGradientPreset.Set);
        RegisterErrorListener(fatalError.Set);
        RegisterFullScreenPresentationMessenger(isFullScreenPresentationActive.Set);
        RegisterUnhandledExceptionHandler();
        ScheduleStartupSplashDismissal(isShowingStartupSplash.Set);
        ScheduleDailyUpdateCheck();

        try
        {

            var activeError = fatalError.Value ?? AppErrorStateService.CurrentError;

            if (activeError is not null)
            {
                return BuildErrorSurface(backdropGradientPreset.Value, activeError);
            }

            return isShowingStartupSplash.Value
                ? AppLoadingSurface.Build()
                : BuildMainSurface(backdropGradientPreset.Value, isFullScreenPresentationActive.Value);
        }
        catch (Exception ex)
        {
            var activeError = AppErrorStateService.CurrentError;

            if (activeError is null)
            {
                AppErrorStateService.SetError(ex);
                activeError = ex;
            }

            return BuildErrorSurface(backdropGradientPreset.Value, activeError);
        }
    }

    private static void RegisterUnhandledExceptionHandler()
    {
        lock (UnhandledExceptionSyncRoot)
        {
            if (_unhandledExceptionHandlerRegistered)
            {
                return;
            }

            var application = Microsoft.UI.Xaml.Application.Current;

            if (application is null)
            {
                return;
            }

            application.UnhandledException += OnUnhandledException;
            _unhandledExceptionHandlerRegistered = true;
        }
    }

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        AppErrorStateService.SetError(args.Exception);
        args.Handled = true;
    }

    private static void RestartApplication()
    {
        try
        {
            // A successful restart terminates this process and does not return.
            var failureReason =
                Microsoft.Windows.AppLifecycle.AppInstance.Restart(string.Empty);

            AppErrorStateService.SetError(
                new InvalidOperationException(
                    $"Windows could not restart LinkScape: {failureReason}"));
        }
        catch (Exception ex)
        {
            AppErrorStateService.SetError(ex);
        }
    }

    private static void OpenDeveloperContact()
    {
        try
        {
            var subject = Uri.EscapeDataString("LinkScape error details");
            var body = Uri.EscapeDataString("Please paste the copied error details here.");
            var uri = new Uri($"mailto:dbamdin@fizzledbydizzlelive.onmicrosoft?subject={subject}&body={body}");
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
        }
        catch
        {
        }
    }

    private void RegisterFullScreenPresentationMessenger(Action<bool> setIsFullScreenPresentationActive)
    {
        _setFullScreenPresentationState = setIsFullScreenPresentationActive;

        if (_fullScreenPresentationMessengerRegistered)
        {
            return;
        }

        _fullScreenPresentationMessengerRegistered = true;
        Messenger.Register<App, WebViewFullScreenPresentationChangedMessage>(
            this,
            static (recipient, message) =>
                recipient._setFullScreenPresentationState?.Invoke(message.IsFullScreen));
    }

    private void RegisterErrorListener(Action<Exception?> setFatalError)
    {
        if (_errorListenerRegistered)
        {
            return;
        }

        _errorListenerRegistered = true;
        AppErrorStateService.ErrorChanged += OnErrorChanged;

        void OnErrorChanged()
        {
            setFatalError(AppErrorStateService.CurrentError);
        }
    }

    private static Element BuildMainSurface(string backdropGradientPreset, bool isFullScreenPresentationActive)
    {
        return FlexColumn(
            TitleBar("LinkScape Browser")
                .Icon("ms-appx:///Assets/Square44x44Logo.targetsize-24.png")
                .IsVisible(!isFullScreenPresentationActive),
            Component<LinkScape.Browser.TabViewPage>()
                .Flex(grow: 1, basis: 0)
        )
        .Background(AppBackdropBrushes.CreateBrush(backdropGradientPreset))
        .Backdrop(BackdropKind.AcrylicThin)
        .WithBorder(Theme.SurfaceStroke)
        .Flex(grow: 1, basis: 0);
    }

    private void ScheduleStartupSplashDismissal(Action<bool> setIsShowingStartupSplash)
    {
        if (_startupSplashDismissScheduled)
        {
            return;
        }

        _startupSplashDismissScheduled = true;

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(StartupSplashDurationMilliseconds);
            setIsShowingStartupSplash(false);
        });
    }

    private void ScheduleDailyUpdateCheck()
    {
        if (_dailyUpdateCheckScheduled)
        {
            return;
        }

        _dailyUpdateCheckScheduled = true;

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(StartupSplashDurationMilliseconds + 750);
            _ = MainWindowActivation.TryEnqueue(
                () => _ = AppUpdateService.CheckForUpdatesIfDueAsync());
        });
    }

    private static Element BuildErrorSurface(string backdropGradientPreset, Exception error)
    {
        return FlexColumn(
            TitleBar("LinkScape Browser").Icon("ms-appx:///Assets/Square44x44Logo.targetsize-24.png"),
            Border(
                VStack(
                    18,
                    HStack(
                        16,
                        Border(
                            Image(StoreLogoAssetPath)
                                .AutomationName("LinkScape logo")
                                .Width(44)
                                .Height(44)
                                .Set(image => image.Stretch = Stretch.UniformToFill))
                            .Width(64)
                            .Height(64)
                            .CornerRadius(20)
                            .Background(new SolidColorBrush(ColorHelper.FromArgb(0x22, 0xFF, 0xFF, 0xFF)))
                            .WithBorder(new SolidColorBrush(ColorHelper.FromArgb(0x30, 0xFF, 0xFF, 0xFF))),
                        VStack(
                            5,
                            (TextBlock("Recovery mode") with
                            {
                                FontSize = 12,
                                CharacterSpacing = 80
                            })
                            .Opacity(0.72),
                            (TextBlock("LinkScape caught a rough edge") with
                            {
                                FontSize = 30,
                                TextWrapping = TextWrapping.WrapWholeWords
                            })
                            .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                            (TextBlock("Your browser shell is paused on this calm page so you can retry, restart, or share the diagnostic details without losing the plot.") with
                            {
                                FontSize = 14,
                                TextWrapping = TextWrapping.WrapWholeWords
                            })
                            .Opacity(0.82)
                        )
                        .Flex(grow: 1, basis: 0)
                    )
                    .VAlign(VerticalAlignment.Center),
                    Border(
                        VStack(
                            8,
                            (TextBlock("What happened") with
                            {
                                FontSize = 13
                            })
                            .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                            (TextBlock(error.Message) with
                            {
                                FontSize = 13,
                                TextWrapping = TextWrapping.WrapWholeWords
                            })
                            .Opacity(0.86),
                            (TextBlock("Use Copy details if you want the in-memory error text.") with
                            {
                                FontSize = 12,
                                TextWrapping = TextWrapping.WrapWholeWords
                            })
                            .Opacity(0.66)
                        ))
                        .Padding(16)
                        .CornerRadius(16)
                        .Background(new SolidColorBrush(ColorHelper.FromArgb(0x24, 0xFF, 0xFF, 0xFF)))
                        .WithBorder(new SolidColorBrush(ColorHelper.FromArgb(0x26, 0xFF, 0xFF, 0xFF))),
                    Border(
                        (TextBlock(BuildErrorDetails(error)) with
                        {
                            FontSize = 12,
                            TextWrapping = TextWrapping.WrapWholeWords,
                            MaxLines = 8
                        })
                        .Set(textBlock => textBlock.FontFamily = new FontFamily("Cascadia Code"))
                        .Opacity(0.74))
                        .Padding(14)
                        .CornerRadius(14)
                        .Background(new SolidColorBrush(ColorHelper.FromArgb(0x20, 0x00, 0x00, 0x00)))
                        .WithBorder(new SolidColorBrush(ColorHelper.FromArgb(0x20, 0xFF, 0xFF, 0xFF))),
                    HStack(
                        10,
                        BuildErrorButton("Retry", AppErrorStateService.Clear, "Retry app shell", isPrimary: true),
                        BuildErrorButton("Restart", RestartApplication, "Restart application"),
                        BuildErrorButton("Copy details", () => CopyErrorDetails(error), "Copy error details")
                    ),
                    (TextBlock("You can also copy details and send them to the Developer contact when you want direct help.") with
                    {
                        FontSize = 12,
                        TextWrapping = TextWrapping.WrapWholeWords
                    })
                    .Opacity(0.66),
                    Button("Developer", OpenDeveloperContact)
                        .AutomationName("Developer contact")
                        .HorizontalAlignment(HorizontalAlignment.Left)
                        .Height(30)
                        .Padding(12, 0)
                        .CornerRadius(15)
                        .Background(new SolidColorBrush(ColorHelper.FromArgb(0x18, 0xFF, 0xFF, 0xFF)))
                        .Foreground(new SolidColorBrush(Colors.White))
                )
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
                .MaxWidth(820)
                .Padding(30)
                .CornerRadius(28)
                .Background(new SolidColorBrush(ColorHelper.FromArgb(0xD6, 0x1E, 0x20, 0x26)))
                .WithBorder(new SolidColorBrush(ColorHelper.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)))
            )
            .Padding(32)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch)
            .Flex(grow: 1, basis: 0)
        )
        .Background(AppBackdropBrushes.CreateBrush(backdropGradientPreset))
        .Backdrop(BackdropKind.AcrylicThin)
        .WithBorder(Theme.SurfaceStroke)
        .Flex(grow: 1, basis: 0);
    }

    private static Element BuildErrorButton(string label, Action onClick, string automationName, bool isPrimary = false)
    {
        return Button(label, onClick)
            .AutomationName(automationName)
            .Height(38)
            .Padding(16, 0)
            .CornerRadius(19)
            .Background(isPrimary
                ? new SolidColorBrush(ColorHelper.FromArgb(0xFF, 0xE8, 0x5F, 0x43))
                : new SolidColorBrush(ColorHelper.FromArgb(0x1E, 0xFF, 0xFF, 0xFF)))
            .Foreground(new SolidColorBrush(Colors.White));
    }

    private static string BuildErrorDetails(Exception error)
    {
        return $"""
            Type: {error.GetType().Name}
            Message: {error.Message}

            {error}
            """;
    }

    private static void CopyErrorDetails(Exception error)
    {
        try
        {
            var package = new DataPackage
            {
                RequestedOperation = DataPackageOperation.Copy
            };
            package.SetText(BuildErrorDetails(error));
            Clipboard.SetContent(package);
        }
        catch
        {
        }
    }

    private void RegisterSettingsListener(Action<string> setBackdropGradientPreset)
    {
        if (_settingsListenerRegistered)
        {
            return;
        }

        _settingsListenerRegistered = true;
        SettingsService.SettingChanged += OnSettingChanged;

        void OnSettingChanged(string key, string? value)
        {
            if (!string.Equals(key, BackdropGradientPresetSettingKey, StringComparison.Ordinal))
            {
                return;
            }

            setBackdropGradientPreset(AppBackdropBrushes.NormalizePreset(value));
        }
    }
}
