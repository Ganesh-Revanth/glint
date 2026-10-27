using System;
using System.Threading.Tasks;
using Glint.Core.Capture;

namespace Glint.Windows.Capture;

public sealed class ScreenCaptureService : IScreenCaptureService
{
    private readonly WindowsGraphicsCaptureService _wgc = new();
    private readonly GdiScreenCaptureService _gdi = new();

    public async Task<byte[]> CapturePrimaryScreenPngAsync()
    {
        try
        {
            return await _wgc.CapturePrimaryScreenPngAsync();
        }
        catch (Exception)
        {
            // Fallback cleanly to high-speed GDI capture if Windows.Graphics.Capture times out or encounters driver limitations
            return await _gdi.CapturePrimaryScreenPngAsync();
        }
    }
}
