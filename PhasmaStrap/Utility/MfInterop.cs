using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace PhasmaStrap.Utility
{
    internal static class MfInterop
    {
        public static readonly Guid MF_READWRITE_ENABLE_HARDWARE_TRANSFORMS = new("a634a91c-822b-41b9-a494-4de4643612b0");
        public static readonly Guid MF_SINK_WRITER_D3D_MANAGER = new("ec822da2-e1e9-4b29-a0d8-563c719f5269");
        public static readonly Guid MF_SINK_WRITER_DISABLE_THROTTLING = new("08b845d8-2b74-4afe-9d53-be16d2d5ae4f");
        public static readonly Guid MF_LOW_LATENCY = new("9c27891a-ed7a-40e1-88e8-b22727a024ee");
        public static readonly Guid MF_TRANSCODE_CONTAINERTYPE = new("150ff23f-4abc-478b-ac4f-e1916fba1cca");
        public static readonly Guid MFTranscodeContainerType_MPEG4 = new("dc6cd05d-b9d0-40ef-bd35-fa622c1ab28a");
        public static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateDXGIDeviceManager(out uint resetToken, out IntPtr manager);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateDXGISurfaceBuffer(ref Guid riid, IntPtr surface, uint subresource, [MarshalAs(UnmanagedType.Bool)] bool bottomUpWhenLinear, out IntPtr buffer);

        [DllImport("mfplat.dll", ExactSpelling = true)]
        private static extern int MFCreateMFByteStreamOnStream(IntPtr stream, out IntPtr byteStream);

        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int MFCreateSinkWriterFromURL(string? url, IntPtr byteStream, IntPtr attributes, out IntPtr writer);

        [DllImport("mfreadwrite.dll", ExactSpelling = true)]
        private static extern int MFCreateSourceReaderFromByteStream(IntPtr byteStream, IntPtr attributes, out IntPtr reader);

        private static void Check(int hr)
        {
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
        }

        private static T Slot<T>(IntPtr self, int slot) where T : Delegate =>
            Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(self), slot * IntPtr.Size));

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetUInt32Fn(IntPtr self, ref Guid key, uint value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetUInt64Fn(IntPtr self, ref Guid key, ulong value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetGuidFn(IntPtr self, ref Guid key, ref Guid value);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetUnknownFn(IntPtr self, ref Guid key, IntPtr value);

        public static void SetUInt32(IMFAttributes attributes, Guid key, uint value) =>
            Check(Slot<SetUInt32Fn>(attributes.NativePointer, 21)(attributes.NativePointer, ref key, value));

        public static void SetUInt64(IMFAttributes attributes, Guid key, ulong value) =>
            Check(Slot<SetUInt64Fn>(attributes.NativePointer, 22)(attributes.NativePointer, ref key, value));

        public static void SetGuid(IMFAttributes attributes, Guid key, Guid value) =>
            Check(Slot<SetGuidFn>(attributes.NativePointer, 24)(attributes.NativePointer, ref key, ref value));

        public static void SetUnknown(IMFAttributes attributes, Guid key, IntPtr value) =>
            Check(Slot<SetUnknownFn>(attributes.NativePointer, 27)(attributes.NativePointer, ref key, value));

        public static ulong Pack(uint high, uint low) => ((ulong)high << 32) | low;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int ResetDeviceFn(IntPtr self, IntPtr device, uint token);

        public static IntPtr CreateDeviceManager(IntPtr device)
        {
            Check(MFCreateDXGIDeviceManager(out uint token, out IntPtr manager));

            try
            {
                Check(Slot<ResetDeviceFn>(manager, 7)(manager, device, token));
                return manager;
            }
            catch
            {
                Marshal.Release(manager);
                throw;
            }
        }

        public static IMFMediaBuffer CreateSurfaceBuffer(IntPtr texture)
        {
            Guid iid = IID_ID3D11Texture2D;
            Check(MFCreateDXGISurfaceBuffer(ref iid, texture, 0, false, out IntPtr buffer));
            return new IMFMediaBuffer(buffer);
        }

        public static int RefCount(IntPtr unknown)
        {
            Marshal.AddRef(unknown);
            return Marshal.Release(unknown);
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetLengthFn(IntPtr self, out ulong length);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int SetPositionFn(IntPtr self, ulong position);

        public static IntPtr CreateMemoryByteStream()
        {
            IntPtr stream = Marshal.GetComInterfaceForObject(new MemoryComStream(), typeof(System.Runtime.InteropServices.ComTypes.IStream));

            try
            {
                Check(MFCreateMFByteStreamOnStream(stream, out IntPtr byteStream));
                return byteStream;
            }
            finally
            {
                Marshal.Release(stream);
            }
        }

        public static long ByteStreamLength(IntPtr byteStream)
        {
            Check(Slot<GetLengthFn>(byteStream, 4)(byteStream, out ulong length));
            return (long)length;
        }

        public static void ByteStreamRewind(IntPtr byteStream) =>
            Check(Slot<SetPositionFn>(byteStream, 7)(byteStream, 0));

        public static IMFSinkWriter CreateSinkWriter(string? url, IntPtr byteStream, IMFAttributes? attributes)
        {
            Check(MFCreateSinkWriterFromURL(url, byteStream, attributes?.NativePointer ?? IntPtr.Zero, out IntPtr writer));
            return new IMFSinkWriter(writer);
        }

        public static IMFSourceReader CreateSourceReader(IntPtr byteStream)
        {
            Check(MFCreateSourceReaderFromByteStream(byteStream, IntPtr.Zero, out IntPtr reader));
            return new IMFSourceReader(reader);
        }
    }
}
