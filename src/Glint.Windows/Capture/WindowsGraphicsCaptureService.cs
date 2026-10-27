using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Direct3D;
using Windows.Win32.Graphics.Direct3D11;
using Windows.Win32.Graphics.Dxgi;
using Windows.Win32.Graphics.Gdi;
using WinRT;
using Glint.Core.Capture;

namespace Glint.Windows.Capture;

public sealed class WindowsGraphicsCaptureService : IScreenCaptureService
{
    private static readonly Guid GraphicsCaptureItemGuid = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid D3D11ResourceGuid = new("DC8E63F3-D12B-4C52-B47B-5E45026A862D");
    private static readonly Guid Direct3DDxgiInterfaceAccessGuid = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");

    [ComImport]
    [Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);
        IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
    }

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", SetLastError = true, ExactSpelling = true)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("combase.dll", PreserveSig = false)]
    [return: MarshalAs(UnmanagedType.IUnknown)]
    private static extern object RoGetActivationFactory(
        [MarshalAs(UnmanagedType.HString)] string activatableClassId,
        [In] ref Guid iid);

    public async Task<byte[]> CapturePrimaryScreenPngAsync()
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            throw new NotSupportedException("Windows.Graphics.Capture is not supported on this OS.");
        }

        HMONITOR hMonitor;
        unsafe
        {
            hMonitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(0, 0), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTOPRIMARY);
            if (hMonitor.IsNull)
            {
                throw new InvalidOperationException("Failed to get primary monitor handle.");
            }
        }

        var interopGuid = typeof(IGraphicsCaptureItemInterop).GUID;
        var interopObj = RoGetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem", ref interopGuid);
        var interop = (IGraphicsCaptureItemInterop)interopObj;

        var itemGuid = GraphicsCaptureItemGuid;
        IntPtr itemPtr;
        unsafe
        {
            itemPtr = interop.CreateForMonitor((nint)hMonitor.Value, ref itemGuid);
        }

        if (itemPtr == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to create GraphicsCaptureItem for primary monitor.");
        }

        var captureItem = MarshalInterface<GraphicsCaptureItem>.FromAbi(itemPtr);

        // Initialize D3D11
        var featureLevels = new[] { D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL.D3D_FEATURE_LEVEL_10_1 };
        ID3D11Device d3dDevice;
        ID3D11DeviceContext d3dContext;

        unsafe
        {
            fixed (D3D_FEATURE_LEVEL* pLevels = featureLevels)
            {
                var hr = PInvoke.D3D11CreateDevice(
                    default,
                    D3D_DRIVER_TYPE.D3D_DRIVER_TYPE_HARDWARE,
                    default,
                    D3D11_CREATE_DEVICE_FLAG.D3D11_CREATE_DEVICE_BGRA_SUPPORT,
                    pLevels,
                    (uint)featureLevels.Length,
                    PInvoke.D3D11_SDK_VERSION,
                    out d3dDevice,
                    null,
                    out d3dContext);

                if (hr.Failed)
                {
                    throw new InvalidOperationException($"D3D11CreateDevice failed with HR: 0x{hr.Value:X}");
                }
            }
        }

        var dxgiDeviceUnknown = Marshal.GetIUnknownForObject(d3dDevice);
        var createDevHr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDeviceUnknown, out var winrtDevicePtr);
        Marshal.Release(dxgiDeviceUnknown);

        if (createDevHr != 0 || winrtDevicePtr == IntPtr.Zero)
        {
            throw new InvalidOperationException($"CreateDirect3D11DeviceFromDXGIDevice failed: 0x{createDevHr:X}");
        }

        var winrtDevice = MarshalInterface<IDirect3DDevice>.FromAbi(winrtDevicePtr);

        using var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            winrtDevice,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            1,
            captureItem.Size);

        using var session = framePool.CreateCaptureSession(captureItem);
        try
        {
            var isBorderRequiredProp = session.GetType().GetProperty("IsBorderRequired");
            isBorderRequiredProp?.SetValue(session, false);
        }
        catch
        {
            // Ignore if OS does not support disabling border
        }

        var tcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        cts.Token.Register(() => tcs.TrySetCanceled());

        framePool.FrameArrived += (sender, _) =>
        {
            try
            {
                using var frame = sender.TryGetNextFrame();
                if (frame == null) return;

                var accessPtr = Marshal.GetComInterfaceForObject(frame.Surface, typeof(IDirect3DDxgiInterfaceAccess));
                var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(accessPtr);
                Marshal.Release(accessPtr);

                var d3dResGuid = D3D11ResourceGuid;
                var sourceResPtr = access.GetInterface(ref d3dResGuid);
                if (sourceResPtr == IntPtr.Zero)
                {
                    tcs.TrySetException(new InvalidOperationException("Failed to get D3D11Resource from frame."));
                    return;
                }

                var sourceRes = (ID3D11Resource)Marshal.GetObjectForIUnknown(sourceResPtr);
                Marshal.Release(sourceResPtr);

                int width = captureItem.Size.Width;
                int height = captureItem.Size.Height;

                var stagingDesc = new D3D11_TEXTURE2D_DESC
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    MipLevels = 1,
                    ArraySize = 1,
                    Format = global::Windows.Win32.Graphics.Dxgi.Common.DXGI_FORMAT.DXGI_FORMAT_B8G8R8A8_UNORM,
                    SampleDesc = new global::Windows.Win32.Graphics.Dxgi.Common.DXGI_SAMPLE_DESC { Count = 1, Quality = 0 },
                    Usage = D3D11_USAGE.D3D11_USAGE_STAGING,
                    BindFlags = (D3D11_BIND_FLAG)0,
                    CPUAccessFlags = D3D11_CPU_ACCESS_FLAG.D3D11_CPU_ACCESS_READ,
                    MiscFlags = (D3D11_RESOURCE_MISC_FLAG)0
                };

                unsafe
                {
                    ID3D11Texture2D_unmanaged* stagingTexUnmanaged = null;
                    d3dDevice.CreateTexture2D(&stagingDesc, null, &stagingTexUnmanaged);
                    if (stagingTexUnmanaged == null)
                    {
                        tcs.TrySetException(new InvalidOperationException("Failed to create staging texture."));
                        return;
                    }

                    var stagingRes = (ID3D11Resource)Marshal.GetObjectForIUnknown((IntPtr)stagingTexUnmanaged);
                    try
                    {
                        d3dContext.CopyResource(stagingRes, sourceRes);

                        D3D11_MAPPED_SUBRESOURCE mapped;
                        d3dContext.Map(stagingRes, 0, D3D11_MAP.D3D11_MAP_READ, 0, &mapped);
                        try
                        {
                            int rowPitch = (int)mapped.RowPitch;
                            byte[] pixelData = new byte[width * height * 4];

                            byte* srcScan = (byte*)mapped.pData;
                            for (int y = 0; y < height; y++)
                            {
                                Marshal.Copy((IntPtr)(srcScan + (y * rowPitch)), pixelData, y * width * 4, width * 4);
                            }

                            var bitmapSource = BitmapSource.Create(
                                width,
                                height,
                                96,
                                96,
                                PixelFormats.Bgra32,
                                null,
                                pixelData,
                                width * 4);

                            bitmapSource.Freeze();

                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));

                            using var ms = new MemoryStream();
                            encoder.Save(ms);
                            tcs.TrySetResult(ms.ToArray());
                        }
                        finally
                        {
                            d3dContext.Unmap(stagingRes, 0);
                        }
                    }
                    finally
                    {
                        stagingTexUnmanaged->Release();
                    }
                }
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        };

        session.StartCapture();

        return await tcs.Task;
    }
}
