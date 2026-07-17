using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace LinkScape;

internal static class AppLoadingSurface
{
    private const string StoreLogoAssetPath = "ms-appx:///Assets/StoreLogo.scale-100.png";
    private const string SilverLinkAssetPath = "ms-appx:///Assets/LoadingLink.silver.svg";

    public static Element Build()
    {
        return FlexColumn(
            TitleBar("LinkScape Browser").Icon("ms-appx:///Assets/Square44x44Logo.targetsize-24.png"),
            Border(
                VStack(28,
                    BuildBrandHeader(),
                    BuildHeroPanel(),
                    VStack(10,
                        (TextBlock("Preparing your browser workspace") with
                        {
                            FontSize = 16,
                            TextWrapping = TextWrapping.WrapWholeWords
                        })
                        .Opacity(0.82),
                        (TextBlock("Tabs, favorites, and history are getting ready.") with
                        {
                            FontSize = 13,
                            TextWrapping = TextWrapping.WrapWholeWords
                        })
                        .Opacity(0.68)
                    )
                    .HAlign(HorizontalAlignment.Center),
                    BuildProgressIndicator()
                )
                .HAlign(HorizontalAlignment.Stretch)
                .VAlign(VerticalAlignment.Center)
                .MaxWidth(820)
                .Padding(28)
                .CornerRadius(32)
                .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x8E, 0x08, 0x08, 0x08)))
                .WithBorder(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x36, 0xFF, 0xFF, 0xFF)))
            )
            .Padding(32)
            .HAlign(HorizontalAlignment.Stretch)
            .VAlign(VerticalAlignment.Stretch)
            .Flex(grow: 1, basis: 0)
        )
        .Background(CreateBackdropBrush())
        .Backdrop(BackdropKind.AcrylicThin)
        .WithBorder(Theme.SurfaceStroke)
        .Flex(grow: 1, basis: 0);
    }

    private static Element BuildBrandHeader()
    {
        return Border(
            HStack(14,
                Border(
                    Image(StoreLogoAssetPath)
                        .AccessibilityHidden()
                        .Width(40)
                        .Height(40)
                        .Set(image => image.Stretch = Stretch.UniformToFill)
                )
                .Width(48)
                .Height(48)
                .CornerRadius(16)
                .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x28, 0xFF, 0xFF, 0xFF)))
                .WithBorder(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)))
                .Padding(4),
                VStack(2,
                    (TextBlock("Welcome") with
                    {
                        FontSize = 13,
                        TextWrapping = TextWrapping.WrapWholeWords
                    })
                    .Opacity(0.68),
                    (TextBlock("LinkScape Browser") with
                    {
                        FontSize = 24,
                        TextWrapping = TextWrapping.WrapWholeWords
                    })
                    .Set(textBlock => textBlock.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold)
                )
            )
        )
        .HAlign(HorizontalAlignment.Stretch);
    }

    private static Element BuildHeroPanel()
    {
        return Border(
            VStack(20,
                HStack(18,
                    BuildLightSpot(width: 124, height: 24, baseAngle: -14, driftAngle: 10, durationSeconds: 16, opacity: 0.18),
                    BuildLightSpot(width: 176, height: 34, baseAngle: 4, driftAngle: 12, durationSeconds: 19, opacity: 0.28),
                    BuildLightSpot(width: 112, height: 22, baseAngle: 12, driftAngle: 8, durationSeconds: 15, opacity: 0.16)
                )
                .HAlign(HorizontalAlignment.Center),
                BuildCenterGlobe()
                    .HAlign(HorizontalAlignment.Center),
                HStack(20,
                    BuildLightSpot(width: 88, height: 18, baseAngle: -22, driftAngle: 10, durationSeconds: 15, opacity: 0.14),
                    BuildLightSpot(width: 236, height: 42, baseAngle: -18, driftAngle: 14, durationSeconds: 20, opacity: 0.34)
                        .Margin(0, -10, 0, 0),
                    BuildLightSpot(width: 104, height: 20, baseAngle: 24, driftAngle: 10, durationSeconds: 17, opacity: 0.16)
                )
                .HAlign(HorizontalAlignment.Center)
            )
            .HAlign(HorizontalAlignment.Center)
            .VAlign(VerticalAlignment.Center)
        )
        .Height(292)
        .Padding(24)
        .CornerRadius(30)
        .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xCC, 0x03, 0x03, 0x03)))
        .WithBorder(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x24, 0xFF, 0xFF, 0xFF)));
    }

    private static Element BuildCenterGlobe()
    {
        return Border(
            Border(
                Image(StoreLogoAssetPath)
                    .AccessibilityHidden()
                    .Width(132)
                    .Height(132)
                    .Set(image => image.Stretch = Stretch.UniformToFill)
            )
            .Width(152)
            .Height(152)
            .CornerRadius(76)
            .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x16, 0xFF, 0xFF, 0xFF)))
            .WithBorder(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x22, 0xFF, 0xFF, 0xFF)))
            .Set(border => ConfigureGlow(border, baseAngle: -8, driftAngle: 12, durationSeconds: 18, useSpotBrush: false))
        )
        .Width(176)
        .Height(176)
        .CornerRadius(88)
        .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x08, 0xFF, 0xFF, 0xFF)))
        .WithBorder(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x3C, 0xFF, 0xFF, 0xFF)))
        .Padding(12)
        .HAlign(HorizontalAlignment.Center)
        .VAlign(VerticalAlignment.Center);
    }

    private static Element BuildLightSpot(
        double width,
        double height,
        double baseAngle,
        double driftAngle,
        double durationSeconds,
        double opacity)
    {
        return Border(null)
            .Width(width)
            .Height(height)
            .CornerRadius(Math.Max(width, height))
            .Opacity(opacity)
            .Set(border => ConfigureGlow(border, baseAngle, driftAngle, durationSeconds, useSpotBrush: true));
    }

    private static void ConfigureGlow(
        Microsoft.UI.Xaml.Controls.Border border,
        double baseAngle,
        double driftAngle,
        double durationSeconds,
        bool useSpotBrush)
    {
        if (border.Tag is Microsoft.UI.Xaml.Media.Animation.Storyboard)
        {
            return;
        }

        var rotateTransform = new RotateTransform
        {
            CenterX = 0.5,
            CenterY = 0.5,
            Angle = baseAngle
        };

        border.Background = useSpotBrush
            ? CreateLightSpotBrush(rotateTransform)
            : CreateHaloBrush(rotateTransform);

        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = baseAngle,
            To = baseAngle + driftAngle,
            Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromSeconds(durationSeconds)),
            AutoReverse = true,
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };

        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, rotateTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "Angle");

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(animation);
        border.Tag = storyboard;
        storyboard.Begin();
    }

    private static Brush CreateBackdropBrush()
    {
        return CreateGradientBrush(
            Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x00, 0x00, 0x00),
            Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x05, 0x05, 0x05),
            Microsoft.UI.ColorHelper.FromArgb(0xFF, 0x11, 0x11, 0x11));
    }

    private static Brush CreateHaloBrush(RotateTransform rotateTransform)
    {
        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.0, 0.5),
            EndPoint = new Windows.Foundation.Point(1.0, 0.5),
            RelativeTransform = rotateTransform,
            GradientStops = new GradientStopCollection
            {
                new() { Color = Microsoft.UI.ColorHelper.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Offset = 0.00 },
                new() { Color = Microsoft.UI.ColorHelper.FromArgb(0x26, 0x50, 0x50, 0x50), Offset = 0.18 },
                new() { Color = Microsoft.UI.ColorHelper.FromArgb(0xD8, 0xFF, 0xFF, 0xFF), Offset = 0.50 },
                new() { Color = Microsoft.UI.ColorHelper.FromArgb(0x74, 0xB8, 0xB8, 0xB8), Offset = 0.82 },
                new() { Color = Microsoft.UI.ColorHelper.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Offset = 1.00 }
            }
        };
    }

    private static Brush CreateLightSpotBrush(RotateTransform rotateTransform)
    {
        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.0, 0.5),
            EndPoint = new Windows.Foundation.Point(1.0, 0.5),
            RelativeTransform = rotateTransform,
            GradientStops = new GradientStopCollection
            {
                new() { Color = Microsoft.UI.ColorHelper.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Offset = 0.00 },
                new() { Color = Microsoft.UI.ColorHelper.FromArgb(0x14, 0x6B, 0x6B, 0x6B), Offset = 0.20 },
                new() { Color = Microsoft.UI.ColorHelper.FromArgb(0xA8, 0xF2, 0xF2, 0xF2), Offset = 0.50 },
                new() { Color = Microsoft.UI.ColorHelper.FromArgb(0x18, 0x7E, 0x7E, 0x7E), Offset = 0.80 },
                new() { Color = Microsoft.UI.ColorHelper.FromArgb(0x00, 0xFF, 0xFF, 0xFF), Offset = 1.00 }
            }
        };
    }

    private static Element BuildProgressIndicator()
    {
        return VStack(12,
            Image(SilverLinkAssetPath)
                .AccessibilityHidden()
                .Width(42)
                .Height(42)
                .Set(ConfigureSilverLinkImage)
                .Opacity(0.92)
                .HAlign(HorizontalAlignment.Center),
            Border(
                Border(null)
                    .Width(110)
                    .Height(6)
                    .CornerRadius(999)
                    .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0xF2, 0xFF, 0xFF, 0xFF)))
                    .HAlign(HorizontalAlignment.Left)
                    .Set(ConfigureProgressIndicator)
            )
            .Width(320)
            .Height(12)
            .Padding(3)
            .CornerRadius(999)
            .Background(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x18, 0xFF, 0xFF, 0xFF)))
            .WithBorder(new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x20, 0xFF, 0xFF, 0xFF)))
            .HAlign(HorizontalAlignment.Center),
            (TextBlock("Loading LinkScape shell…") with
            {
                FontSize = 13,
                TextWrapping = TextWrapping.WrapWholeWords
            })
            .Opacity(0.7)
            .HAlign(HorizontalAlignment.Center)
        )
        .HAlign(HorizontalAlignment.Center);
    }

    private static void ConfigureSilverLinkImage(Microsoft.UI.Xaml.Controls.Image image)
    {
        image.Stretch = Stretch.Uniform;
        image.Source = new SvgImageSource(new Uri(SilverLinkAssetPath));
    }

    private static void ConfigureProgressIndicator(Microsoft.UI.Xaml.Controls.Border progressIndicator)
    {
        if (progressIndicator.Tag is Microsoft.UI.Xaml.Media.Animation.Storyboard)
        {
            return;
        }

        progressIndicator.RenderTransform = new TranslateTransform { X = -88 };

        var animation = new Microsoft.UI.Xaml.Media.Animation.DoubleAnimation
        {
            From = -88,
            To = 194,
            Duration = new Microsoft.UI.Xaml.Duration(TimeSpan.FromSeconds(1.45)),
            RepeatBehavior = Microsoft.UI.Xaml.Media.Animation.RepeatBehavior.Forever,
            EnableDependentAnimation = true
        };

        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTarget(animation, progressIndicator.RenderTransform);
        Microsoft.UI.Xaml.Media.Animation.Storyboard.SetTargetProperty(animation, "X");

        var storyboard = new Microsoft.UI.Xaml.Media.Animation.Storyboard();
        storyboard.Children.Add(animation);
        progressIndicator.Tag = storyboard;
        storyboard.Begin();
    }

    private static Brush CreateGradientBrush(
        Windows.UI.Color start,
        Windows.UI.Color middle,
        Windows.UI.Color end)
    {
        return new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0.5, 0.0),
            EndPoint = new Windows.Foundation.Point(0.5, 1.0),
            GradientStops = new GradientStopCollection
            {
                new() { Color = start, Offset = 0.0 },
                new() { Color = middle, Offset = 0.5 },
                new() { Color = end, Offset = 1.0 }
            }
        };
    }
}
