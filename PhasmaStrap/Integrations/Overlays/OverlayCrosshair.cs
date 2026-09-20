using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;
using MapFlags = Vortice.Direct3D11.MapFlags;

namespace PhasmaStrap.Integrations.Overlays
{
    internal sealed class OverlayCrosshair
    {
        public const int TexWidth = 256;
        public const int TexHeight = 256;

        private ID3D11Device _device = null!;
        private ID3D11Texture2D? _tex;
        private ID3D11ShaderResourceView? _srv;
        private Bitmap? _bitmap;
        private Graphics? _graphics;
        private string _last = "";

        public ID3D11ShaderResourceView? Srv => _srv;

        public void Init(ID3D11Device device)
        {
            Dispose();
            _device = device;
            _tex = _device.CreateTexture2D(new Texture2DDescription
            {
                Width = TexWidth,
                Height = TexHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.B8G8R8A8_UNorm,
                SampleDescription = new SampleDescription(1, 0),
                Usage = ResourceUsage.Dynamic,
                BindFlags = BindFlags.ShaderResource,
                CpuAccessFlags = CpuAccessFlags.Write,
            });
            _srv = _device.CreateShaderResourceView(_tex);
            _bitmap = new Bitmap(TexWidth, TexHeight, PixelFormat.Format32bppArgb);
            _graphics = Graphics.FromImage(_bitmap);
            _graphics.SmoothingMode = SmoothingMode.AntiAlias;
            _last = "";
        }

        public static bool IsEnabled()
        {
            try
            {
                return OverlayHub.InGame && OverlaySettings.CrosshairEnabled && CrosshairStyles.Current.HasVisibleParts;
            }
            catch
            {
                return false;
            }
        }

        public void Update(ID3D11DeviceContext context)
        {
            if (_tex == null || _bitmap == null || _graphics == null)
                return;

            CrosshairStyle style = CrosshairStyles.Current;
            string signature = style.Signature;
            if (signature == _last)
                return;
            _last = signature;

            _graphics.Clear(Color.Transparent);
            CrosshairRenderer.Draw(_graphics, style, TexWidth / 2, TexHeight / 2);

            Upload(context);
        }

        private void Upload(ID3D11DeviceContext context)
        {
            var locked = _bitmap!.LockBits(new Rectangle(0, 0, TexWidth, TexHeight), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var mapped = context.Map(_tex!, 0, MapMode.WriteDiscard, MapFlags.None);
                try
                {
                    int rowBytes = TexWidth * 4;
                    unsafe
                    {
                        byte* src = (byte*)locked.Scan0;
                        byte* dst = (byte*)mapped.DataPointer;
                        for (int py = 0; py < TexHeight; py++)
                            Buffer.MemoryCopy(src + py * locked.Stride, dst + py * (int)mapped.RowPitch, rowBytes, rowBytes);
                    }
                }
                finally
                {
                    context.Unmap(_tex!, 0);
                }
            }
            finally
            {
                _bitmap.UnlockBits(locked);
            }
        }

        public void Dispose()
        {
            _srv?.Dispose();
            _tex?.Dispose();
            _graphics?.Dispose();
            _bitmap?.Dispose();
            _srv = null;
            _tex = null;
            _graphics = null;
            _bitmap = null;
            _last = "";
        }
    }
}
