using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.ShareTarget;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace LinkScape.Services;

internal static class WindowsShareTargetService
{
    private const ulong MaximumImageBytes = 25UL * 1024 * 1024;
    private static readonly SemaphoreSlim DialogGate = new(1, 1);

    internal static async Task ShowImagePreviewAsync(
        ShareOperation shareOperation,
        Action<string> openUrl)
    {
        ArgumentNullException.ThrowIfNull(shareOperation);
        ArgumentNullException.ThrowIfNull(openUrl);

        shareOperation.ReportStarted();

        try
        {
            var sharedImage = await ReadSharedImageAsync(shareOperation.Data);
            if (sharedImage is null)
            {
                shareOperation.ReportError("LinkScape could not read an image from this share.");
                BrowserNoticeService.Show("The shared data did not contain a readable image.");
                return;
            }

            await DialogGate.WaitAsync();
            try
            {
                global::MainWindowActivation.RestoreAndActivate();
                var result = await ShowPreviewDialogAsync(sharedImage);

                if (result == ContentDialogResult.Primary)
                {
                    await SaveImageAsync(sharedImage);
                }
                else if (result == ContentDialogResult.Secondary && sharedImage.SourceUrl is not null)
                {
                    openUrl(sharedImage.SourceUrl.AbsoluteUri);
                }

                shareOperation.ReportCompleted();
            }
            finally
            {
                DialogGate.Release();
            }
        }
        catch (Exception ex)
        {
            try
            {
                shareOperation.ReportError("LinkScape could not import the shared image.");
            }
            catch
            {
            }

            LocalMcpDiagnostics.Trace("WindowsShareTarget", $"Share import failed: {ex}");
            BrowserNoticeService.Show($"Could not import the shared image: {ex.Message}");
        }
    }

    private static async Task<SharedImage?> ReadSharedImageAsync(DataPackageView data)
    {
        if (!data.Contains(StandardDataFormats.Bitmap))
        {
            return null;
        }

        var imageReference = await data.GetBitmapAsync();
        using var imageStream = await imageReference.OpenReadAsync();
        var contentType = imageStream.ContentType;

        if (imageStream.Size == 0 || imageStream.Size > MaximumImageBytes)
        {
            throw new InvalidOperationException(
                imageStream.Size > MaximumImageBytes
                    ? "The shared image is larger than the 25 MB import limit."
                    : "The shared image is empty.");
        }

        var bytes = new byte[checked((int)imageStream.Size)];
        using (var reader = new DataReader(imageStream))
        {
            var loaded = await reader.LoadAsync((uint)bytes.Length);
            if (loaded != bytes.Length)
            {
                throw new InvalidOperationException("The complete shared image could not be read.");
            }

            reader.ReadBytes(bytes);
        }

        Uri? sourceUrl = null;
        if (data.Contains(StandardDataFormats.WebLink))
        {
            sourceUrl = await data.GetWebLinkAsync();
        }

        string? text = null;
        if (data.Contains(StandardDataFormats.Text))
        {
            text = await data.GetTextAsync();
        }

        var title = string.IsNullOrWhiteSpace(data.Properties.Title)
            ? "Shared image"
            : data.Properties.Title.Trim();

        return new SharedImage(
            title,
            text?.Trim(),
            sourceUrl,
            contentType,
            bytes);
    }

    private static async Task<ContentDialogResult> ShowPreviewDialogAsync(SharedImage sharedImage)
    {
        var xamlRoot = global::MainWindowActivation.GetXamlRoot();
        if (xamlRoot is null)
        {
            throw new InvalidOperationException("The LinkScape window is not ready to preview shared content.");
        }

        var bitmap = await CreateBitmapAsync(sharedImage.Bytes);
        var content = new StackPanel
        {
            Spacing = 12,
            MaxWidth = 840
        };

        content.Children.Add(new Border
        {
            MaxHeight = 540,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Image
                {
                    Source = bitmap,
                    MaxWidth = 800,
                    MaxHeight = 520,
                    Stretch = Stretch.Uniform
                }
            }
        });

        if (sharedImage.SourceUrl is not null)
        {
            content.Children.Add(new TextBlock
            {
                Text = sharedImage.SourceUrl.AbsoluteUri,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.72
            });
        }
        else if (!string.IsNullOrWhiteSpace(sharedImage.Text))
        {
            content.Children.Add(new TextBlock
            {
                Text = sharedImage.Text,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 72,
                Opacity = 0.72
            });
        }

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = sharedImage.Title,
            Content = content,
            PrimaryButtonText = "Save image",
            CloseButtonText = "Discard",
            DefaultButton = ContentDialogButton.Primary
        };

        if (sharedImage.SourceUrl is not null)
        {
            dialog.SecondaryButtonText = "Open page";
        }

        return await dialog.ShowAsync();
    }

    private static async Task<BitmapImage> CreateBitmapAsync(byte[] bytes)
    {
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);
        var bitmap = new BitmapImage();
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private static async Task SaveImageAsync(SharedImage sharedImage)
    {
        var extension = GetFileExtension(sharedImage.ContentType);
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = CreateSafeFileName(sharedImage.Title)
        };

        picker.FileTypeChoices.Add("Image", new List<string> { extension });

        var hwnd = global::MainWindowActivation.Hwnd;
        if (hwnd == 0)
        {
            throw new InvalidOperationException("The LinkScape window is not ready to save the image.");
        }

        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSaveFileAsync();
        if (file is null)
        {
            return;
        }

        await FileIO.WriteBytesAsync(file, sharedImage.Bytes);
        BrowserNoticeService.Show($"Saved shared image as {file.Name}.");
    }

    private static string GetFileExtension(string? contentType) =>
        contentType?.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/gif" => ".gif",
            "image/bmp" => ".bmp",
            "image/tiff" => ".tif",
            "image/webp" => ".webp",
            _ => ".png"
        };

    private static string CreateSafeFileName(string title)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = new string(title
            .Where(character => !invalidCharacters.Contains(character))
            .ToArray())
            .Trim();

        return string.IsNullOrWhiteSpace(safeName) ? "Shared image" : safeName;
    }

    private sealed record SharedImage(
        string Title,
        string? Text,
        Uri? SourceUrl,
        string? ContentType,
        byte[] Bytes);
}
