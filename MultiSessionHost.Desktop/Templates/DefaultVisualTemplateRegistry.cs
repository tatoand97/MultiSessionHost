using System.Drawing;
using System.Drawing.Imaging;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Targets;

namespace MultiSessionHost.Desktop.Templates;

public sealed class DefaultVisualTemplateRegistry : IVisualTemplateRegistry
{
    private const string DefaultSetName = "DefaultGenericMarkers";

    private static readonly IReadOnlyList<VisualTemplateDefinition> DefaultTemplates =
    [
        new(
            "marker.cross.3x3",
            "marker",
            DefaultSetName,
            ["threshold", "high-contrast", "grayscale", "raw"],
            ["window.top", "window.center"],
            0.98d,
            "image/png",
            CreateCrossTemplate(),
            ProviderReference: "builtin:marker.cross.3x3",
            Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["builtin"] = true.ToString(),
                ["category"] = "synthetic"
            }),
        new(
            "marker.block.4x4",
            "marker",
            DefaultSetName,
            ["threshold", "high-contrast", "grayscale", "raw"],
            ["window.top", "window.center", "window.left", "window.right"],
            0.95d,
            "image/png",
            CreateBlockTemplate(),
            ProviderReference: "builtin:marker.block.4x4",
            Metadata: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["builtin"] = true.ToString(),
                ["category"] = "synthetic"
            })
    ];

    public VisualTemplateSet Resolve(ResolvedDesktopTargetContext context, TemplateDetectionProfile profile)
    {
        var selectedSetName = DesktopTargetMetadata.GetValue(context.Target.Metadata, DesktopTargetMetadata.TemplateSet, profile.TemplateSetName).Trim();
        var templateSetName = string.IsNullOrWhiteSpace(selectedSetName) ? DefaultSetName : selectedSetName;

        if (!string.Equals(templateSetName, DefaultSetName, StringComparison.OrdinalIgnoreCase))
        {
            return new VisualTemplateSet(
                templateSetName,
                profile.ProfileName,
                [],
                new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["isBuiltin"] = false.ToString(),
                    ["resolution"] = "empty"
                });
        }

        return new VisualTemplateSet(
            DefaultSetName,
            profile.ProfileName,
            DefaultTemplates,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["isBuiltin"] = true.ToString(),
                ["resolution"] = "default"
            });
    }

    private static byte[] CreateCrossTemplate()
    {
        using var bitmap = new Bitmap(3, 3, PixelFormat.Format32bppArgb);
        var white = Color.FromArgb(255, 255, 255, 255);
        var black = Color.FromArgb(255, 0, 0, 0);

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                bitmap.SetPixel(x, y, black);
            }
        }

        bitmap.SetPixel(1, 0, white);
        bitmap.SetPixel(0, 1, white);
        bitmap.SetPixel(1, 1, white);
        bitmap.SetPixel(2, 1, white);
        bitmap.SetPixel(1, 2, white);

        return EncodePng(bitmap);
    }

    private static byte[] CreateBlockTemplate()
    {
        using var bitmap = new Bitmap(4, 4, PixelFormat.Format32bppArgb);

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var value = x is 0 or 3 || y is 0 or 3 ? 255 : 0;
                bitmap.SetPixel(x, y, Color.FromArgb(255, value, value, value));
            }
        }

        return EncodePng(bitmap);
    }

    private static byte[] EncodePng(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
