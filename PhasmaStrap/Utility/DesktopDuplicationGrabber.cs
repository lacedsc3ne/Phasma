using System.Drawing;
using System.Drawing.Imaging;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using D3D11 = Vortice.Direct3D11.D3D11;

namespace PhasmaStrap.Utility
{
    // Grabs a rectangle of the desktop through DXGI desktop duplication - the GPU hands over the
    // composed frame, so a 1080p grab costs a few milliseconds instead of the ~25ms that
    // PrintWindow takes. Instant Replay uses it while Roblox is the foreground window (when what
    // is on screen IS the game) to reach 60fps; PrintWindow stays the fallback for everything
    // else. Same technique OverlayCompositor uses, kept separate so the recorder works with
    // overlays switched off and owns its own device.
    //
    // Free of App dependencies so it can run in a console harness.
    public sealed class DesktopDuplicationGrabber : IDisposable
    {
        public enum GrabResult
        {
            Frame,          // `bitmap` holds a new frame
            NoNewFrame,     // nothing on screen changed since the last grab - keep showing the previous frame
            Unavailable,    // duplication can't be used right now (HDR surface, access lost, other GPU...)
        }

        public static Action<string>? Log;

        private static readonly FeatureLevel[] FeatureLevels = { FeatureLevel.Level_11_1, FeatureLevel.Level_11_0, FeatureLevel.Level_10_1, FeatureLevel.Level_10_0 };

        private ID3D11Device? _device;
        private ID3D11DeviceContext? _context;
        private IDXGIOutputDuplication? _duplication;
        private ID3D11Texture2D? _staging;
        private int _stagingWidth, _stagingHeight;
        private int _outputLeft, _outputTop, _outputRight, _outputBottom;

        // Grabs the given screen rectangle. `bitmap` is a new 32bppRgb bitmap the caller owns.
        public GrabResult TryGrab(int left, int top, int width, int height, int timeoutMs, out Bitmap? bitmap)
        {
            bitmap = null;

            try
            {
                if (!EnsureDuplication(left + width / 2, top + height / 2))
                    return GrabResult.Unavailable;

                IDXGIResource? resource = null;
                bool acquired = false;

                try
                {
                    try
                    {
                        _duplication!.AcquireNextFrame(timeoutMs, out OutduplFrameInfo info, out resource);
                        acquired = true;

                        // a mouse-only update carries no new image
                        if (info.LastPresentTime == 0)
                            return GrabResult.NoNewFrame;
                    }
                    catch (SharpGenException ex) when (ex.ResultCode == Vortice.DXGI.ResultCode.WaitTimeout)
                    {
                        return GrabResult.NoNewFrame;
                    }
                    catch (SharpGenException ex) when (ex.ResultCode == Vortice.DXGI.ResultCode.AccessLost)
                    {
                        // resolution / fullscreen change or secure desktop - rebuilt on the next call
                        ReleaseDuplication();
                        return GrabResult.Unavailable;
                    }

                    if (resource is null)
                        return GrabResult.Unavailable;

                    using ID3D11Texture2D desktop = resource.QueryInterface<ID3D11Texture2D>();
                    Texture2DDescription desc = desktop.Description;

                    // HDR desktops duplicate as 16-bit float, which would need a tone-mapping pass
                    if (desc.Format != Format.B8G8R8A8_UNorm)
                    {
                        Log?.Invoke($"Desktop surface is {desc.Format}, not BGRA8 - duplication capture unavailable");
                        return GrabResult.Unavailable;
                    }

                    int srcLeft = Math.Max(0, left - _outputLeft);
                    int srcTop = Math.Max(0, top - _outputTop);
                    int srcRight = Math.Min(srcLeft + width, (int)desc.Width);
                    int srcBottom = Math.Min(srcTop + height, (int)desc.Height);
                    int w = (srcRight - srcLeft) & ~1;
                    int h = (srcBottom - srcTop) & ~1;
                    if (w < 2 || h < 2)
                        return GrabResult.Unavailable;

                    EnsureStaging(w, h);

                    _context!.CopySubresourceRegion(_staging!, 0, 0, 0, 0, desktop, 0, new Vortice.Mathematics.Box(srcLeft, srcTop, 0, srcLeft + w, srcTop + h, 1));

                    MappedSubresource mapped = _context.Map(_staging!, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
                    try
                    {
                        var result = new Bitmap(w, h, PixelFormat.Format32bppRgb);
                        BitmapData data = result.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                        try
                        {
                            unsafe
                            {
                                byte* src = (byte*)mapped.DataPointer;
                                byte* dst = (byte*)data.Scan0;
                                long rowBytes = (long)w * 4;

                                for (int y = 0; y < h; y++)
                                    Buffer.MemoryCopy(src + (long)y * mapped.RowPitch, dst + (long)y * data.Stride, rowBytes, rowBytes);
                            }
                        }
                        finally
                        {
                            result.UnlockBits(data);
                        }

                        bitmap = result;
                        return GrabResult.Frame;
                    }
                    finally
                    {
                        _context.Unmap(_staging!, 0);
                    }
                }
                finally
                {
                    resource?.Dispose();

                    if (acquired)
                    {
                        try { _duplication?.ReleaseFrame(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Duplication grab failed: {ex.Message}");
                ReleaseDuplication();
                return GrabResult.Unavailable;
            }
        }

        private bool EnsureDuplication(int centerX, int centerY)
        {
            bool onOutput = centerX >= _outputLeft && centerX < _outputRight && centerY >= _outputTop && centerY < _outputBottom;
            if (_duplication is not null && onOutput)
                return true;

            ReleaseDuplication();

            if (_device is null)
            {
                D3D11.D3D11CreateDevice((IDXGIAdapter)null!, DriverType.Hardware, DeviceCreationFlags.BgraSupport, FeatureLevels, out _device, out _context).CheckError();
            }

            using IDXGIDevice dxgiDevice = _device!.QueryInterface<IDXGIDevice>();
            dxgiDevice.GetAdapter(out IDXGIAdapter adapter).CheckError();

            try
            {
                for (int i = 0; ; i++)
                {
                    Result res = adapter.EnumOutputs(i, out IDXGIOutput output);
                    if (res.Failure || output is null)
                        break;

                    try
                    {
                        var bounds = output.Description.DesktopCoordinates;
                        if (centerX < bounds.Left || centerX >= bounds.Right || centerY < bounds.Top || centerY >= bounds.Bottom)
                            continue;

                        using IDXGIOutput1 output1 = output.QueryInterface<IDXGIOutput1>();
                        _duplication = output1.DuplicateOutput(_device);
                        _outputLeft = bounds.Left;
                        _outputTop = bounds.Top;
                        _outputRight = bounds.Right;
                        _outputBottom = bounds.Bottom;

                        Log?.Invoke($"Desktop duplication active on the monitor at {bounds.Left},{bounds.Top} - {bounds.Right},{bounds.Bottom}");
                        return true;
                    }
                    finally
                    {
                        output.Dispose();
                    }
                }
            }
            finally
            {
                adapter.Dispose();
            }

            // the window is on a monitor driven by a different GPU than the default one
            Log?.Invoke("No duplicable output contains the game window");
            return false;
        }

        private void EnsureStaging(int width, int height)
        {
            if (_staging is not null && _stagingWidth == width && _stagingHeight == height)
                return;

            _staging?.Dispose();
            _staging = _device!.CreateTexture2D(new Texture2DDescription
            {
                Width = width,
                Height = height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Staging,
                BindFlags = BindFlags.None,
                CpuAccessFlags = CpuAccessFlags.Read,
            });
            _stagingWidth = width;
            _stagingHeight = height;
        }

        private void ReleaseDuplication()
        {
            try { _duplication?.Dispose(); } catch { }
            _duplication = null;
            _outputLeft = _outputTop = _outputRight = _outputBottom = 0;
        }

        public void Dispose()
        {
            ReleaseDuplication();
            _staging?.Dispose();
            _staging = null;
            _context?.Dispose();
            _context = null;
            _device?.Dispose();
            _device = null;
        }
    }
}
