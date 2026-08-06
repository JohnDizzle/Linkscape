using LinkScape.Browser;
using LinkScape.Browser.Messages;
using LinkScape.Services;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;

var commandLineArgs = Environment.GetCommandLineArgs();

ConfigureWebView2BrowserArguments();

if (await LocalMcpServerService.TryRunAsync(commandLineArgs))
{
    return;
}

if (!await LinkScape.ActivationRoutingService.InitializeAsync())
{
    return;
}

TabPersistenceService.EnsureDatabase();
HistoryPersistenceService.EnsureDatabase();
SettingsService.EnsureDatabase();
FavoritesService.EnsureDatabase();
TabCollectionService.EnsureDatabase();
const string WebView2AdditionalBrowserArgumentsKey = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";
const string WebView2SingleSignOnPrimaryAccountArgument = "--allow-single-sign-on-os-primary-account";

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
            
        };

        host.Window.Closed += (_, _) => MainWindowActivation.SaveWindowPlacement();
    });

static void ConfigureWebView2BrowserArguments()
{
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
            LinkScape.AppBackdropBrushes.NormalizePreset(
                SettingsService.GetValueOrDefault(
                    BackdropGradientPresetSettingKey,
                    LinkScape.AppBackdropBrushes.DefaultPreset)));
        var fatalError = UseState<Exception?>(LinkScape.AppErrorStateService.CurrentError, threadSafe: true);
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

            return fatalError.Value is not null
                ? BuildErrorSurface(backdropGradientPreset.Value, fatalError.Value)
                : isShowingStartupSplash.Value
                    ? LinkScape.AppLoadingSurface.Build()
                : BuildMainSurface(backdropGradientPreset.Value, isFullScreenPresentationActive.Value);
        }
        catch (Exception ex)
        {
            LinkScape.AppErrorStateService.SetError(ex);
            return BuildErrorSurface(backdropGradientPreset.Value, ex);
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

            var application = Application.Current;

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
        LinkScape.AppErrorStateService.SetError(args.Exception);
        args.Handled = true;
    }

    private static void RestartApplication()
    {
        try
        {
            var executablePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = true
            });

            Environment.Exit(0);
        }
        catch
        {
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
        LinkScape.AppErrorStateService.ErrorChanged += OnErrorChanged;

        void OnErrorChanged()
        {
            setFatalError(LinkScape.AppErrorStateService.CurrentError);
        }
    }

    private static Element BuildMainSurface(string backdropGradientPreset, bool isFullScreenPresentationActive)
    {
        return FlexColumn(
            TitleBar("LinkScape Browser")
                .Icon("ms-appx:///Assets/Square44x44Logo.targetsize-24.png")
                .IsVisible(!isFullScreenPresentationActive),
            Component<LinkScape.TabViewPage>()
                .Flex(grow: 1, basis: 0)
        )
        .Background(LinkScape.AppBackdropBrushes.CreateBrush(backdropGradientPreset))
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
                    12,
                    (TextBlock("LinkScape Browser hit an unexpected error") with
                    {
                        FontSize = 28,
                        TextWrapping = TextWrapping.WrapWholeWords
                    })
                    .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold),
                    (TextBlock("The app shell was replaced with a safe page so you can recover without a raw stack trace.") with
                    {
                        FontSize = 14,
                        TextWrapping = TextWrapping.WrapWholeWords
                    })
                    .Opacity(0.82),
                    Border(
                        TextBlock(error.Message) with
                        {
                            FontSize = 13,
                            TextWrapping = TextWrapping.WrapWholeWords
                        })
                        .Padding(14)
                        .CornerRadius(12)
                        .Background(BrowserConstants.LayerFillDefaultBrush),
                    HStack(
                        10,
                        Button("Retry", LinkScape.AppErrorStateService.Clear)
                            .AutomationName("Retry app shell")
                            .Height(36)
                            .Padding(14, 0)
                            .CornerRadius(18),
                        Button("Restart app", RestartApplication)
                            .AutomationName("Restart application")
                            .Height(36)
                            .Padding(14, 0)
                            .CornerRadius(18),
                        Button("Copy details", () =>
                        {
                            try
                            {
                                var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
                                package.SetText(error.ToString());
                                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
                            }
                            catch
                            {
                            }
                        })
                            .AutomationName("Copy error details")
                            .Height(36)
                            .Padding(14, 0)
                            .CornerRadius(18)
                    ),
                    (FlexRow(
                        (TextBlock("You may also send the details to ") with
                        {
                            FontSize = 12,
                            TextWrapping = TextWrapping.WrapWholeWords
                        })
                        .Opacity(0.72),
                        Button("Developer", OpenDeveloperContact)
                            .AutomationName("Developer contact")
                            .Padding(0)
                            .CornerRadius(14),
                        (TextBlock(" if you want direct help.") with
                        {
                            FontSize = 12,
                            TextWrapping = TextWrapping.WrapWholeWords
                        })
                        .Opacity(0.72)
                    ) with
                    {
                        ColumnGap = 4
                    })
                    .VAlign(VerticalAlignment.Center)
                )
                .HAlign(HorizontalAlignment.Center)
                .VAlign(VerticalAlignment.Center)
                .MaxWidth(640)
                .Padding(28)
                .CornerRadius(24)
                .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xD8, 0x4B, 0x1F, 0x24)))
                .WithBorder(Theme.SurfaceStroke)
            )
            .Padding(32)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch)
            .Flex(grow: 1, basis: 0)
        )
        .Background(LinkScape.AppBackdropBrushes.CreateBrush(backdropGradientPreset))
        .Backdrop(BackdropKind.AcrylicThin)
        .WithBorder(Theme.SurfaceStroke)
        .Flex(grow: 1, basis: 0);
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

            setBackdropGradientPreset(LinkScape.AppBackdropBrushes.NormalizePreset(value));
        }
    }
}
