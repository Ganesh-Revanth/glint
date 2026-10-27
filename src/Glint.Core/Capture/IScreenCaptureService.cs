namespace Glint.Core.Capture;

public interface IScreenCaptureService
{
    Task<byte[]> CapturePrimaryScreenPngAsync();
}
