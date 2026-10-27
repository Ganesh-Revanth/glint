using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.WindowsAndMessaging;
using Glint.Core.Capture;

namespace Glint.Windows.Capture;

public sealed class GdiScreenCaptureService : IScreenCaptureService
{
    public Task<byte[]> CapturePrimaryScreenPngAsync()
    {
        return Task.FromResult(CapturePrimaryScreenPng());
    }

    public static unsafe byte[] CapturePrimaryScreenPng()
    {
        int width = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CXSCREEN);
        int height = PInvoke.GetSystemMetrics(SYSTEM_METRICS_INDEX.SM_CYSCREEN);

        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("Failed to determine screen metrics.");
        }

        HDC screenDc = PInvoke.GetDC(HWND.Null);
        if (screenDc.IsNull)
        {
            throw new InvalidOperationException("Unable to get desktop DC.");
        }

        HDC memDc = PInvoke.CreateCompatibleDC(screenDc);
        if (memDc.IsNull)
        {
            PInvoke.ReleaseDC(HWND.Null, screenDc);
            throw new InvalidOperationException("Unable to create compatible memory DC.");
        }

        HBITMAP hBitmap = PInvoke.CreateCompatibleBitmap(screenDc, width, height);
        if (hBitmap.IsNull)
        {
            PInvoke.DeleteDC(memDc);
            PInvoke.ReleaseDC(HWND.Null, screenDc);
            throw new InvalidOperationException("Unable to create compatible bitmap.");
        }

        HGDIOBJ oldObj = PInvoke.SelectObject(memDc, (HGDIOBJ)hBitmap.Value);

        try
        {
            PInvoke.BitBlt(memDc, 0, 0, width, height, screenDc, 0, 0, ROP_CODE.SRCCOPY);

            var bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(
                (IntPtr)hBitmap.Value,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            bitmapSource.Freeze();

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        finally
        {
            PInvoke.SelectObject(memDc, oldObj);
            PInvoke.DeleteObject(hBitmap);
            PInvoke.DeleteDC(memDc);
            PInvoke.ReleaseDC(HWND.Null, screenDc);
        }
    }
}
