using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Native;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Windows;

public sealed class Win32WindowFrameCapture : IWindowFrameCapture
{
    public Task<WindowFrameCaptureResult> CaptureAsync(DesktopSessionAttachment attachment, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        cancellationToken.ThrowIfCancellationRequested();

        var windowHandle = new nint(attachment.Window.WindowHandle);
        if (!Win32NativeMethods.IsWindow(windowHandle))
        {
            throw new InvalidOperationException($"The attached window '{attachment.Window.WindowHandle}' is not a valid window handle.");
        }

        if (!Win32NativeMethods.GetWindowRect(windowHandle, out var rect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to read bounds for window '{attachment.Window.WindowHandle}'.");
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException($"The attached window '{attachment.Window.WindowHandle}' has invalid bounds {rect.Left},{rect.Top},{width},{height}.");
        }

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);

        var result = new WindowFrameCaptureResult(
            new UiBounds(rect.Left, rect.Top, width, height),
            width,
            height,
            "image/png",
            bitmap.PixelFormat.ToString(),
            stream.ToArray(),
            "Graphics.CopyFromScreen");

        return Task.FromResult(result);
    }
}
