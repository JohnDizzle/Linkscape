namespace LinkScape.Browser.Messages;

internal sealed record WebViewFullScreenPresentationRequestMessage(bool IsFullScreen);

internal sealed record WebViewFullScreenPresentationChangedMessage(bool IsFullScreen);
