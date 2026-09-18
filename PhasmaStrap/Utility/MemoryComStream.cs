using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace PhasmaStrap.Utility
{
    // A COM IStream over managed memory, for Media Foundation to write an MP4 segment into
    // (through MFCreateMFByteStreamOnStream) and read it back from later. The stock
    // CreateStreamOnHGlobal stream is refused by the MPEG-4 sink at BeginWriting.
    //
    // Storage is a list of fixed blocks rather than one array: a segment grows to a few MB in
    // small writes, and doubling-and-copying a contiguous buffer would put every segment on the
    // large object heap several times over.
    internal sealed class MemoryComStream : IStream
    {
        private const int BlockSize = 256 * 1024;

        private readonly object _lock = new();
        private readonly List<byte[]> _blocks = new();
        private long _length;
        private long _position;

        public long Length
        {
            get { lock (_lock) return _length; }
        }

        private void EnsureCapacity(long size)
        {
            while ((long)_blocks.Count * BlockSize < size)
                _blocks.Add(new byte[BlockSize]);
        }

        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            lock (_lock)
            {
                int total = (int)Math.Max(0, Math.Min(cb, _length - _position));
                int done = 0;

                while (done < total)
                {
                    int block = (int)(_position / BlockSize), at = (int)(_position % BlockSize);
                    int take = Math.Min(total - done, BlockSize - at);
                    Buffer.BlockCopy(_blocks[block], at, pv, done, take);
                    done += take;
                    _position += take;
                }

                if (pcbRead != IntPtr.Zero)
                    Marshal.WriteInt32(pcbRead, total);
            }
        }

        public void Write(byte[] pv, int cb, IntPtr pcbWritten)
        {
            lock (_lock)
            {
                EnsureCapacity(_position + cb);
                int done = 0;

                while (done < cb)
                {
                    int block = (int)(_position / BlockSize), at = (int)(_position % BlockSize);
                    int take = Math.Min(cb - done, BlockSize - at);
                    Buffer.BlockCopy(pv, done, _blocks[block], at, take);
                    done += take;
                    _position += take;
                }

                if (_position > _length)
                    _length = _position;

                if (pcbWritten != IntPtr.Zero)
                    Marshal.WriteInt32(pcbWritten, cb);
            }
        }

        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            lock (_lock)
            {
                long target = dwOrigin switch
                {
                    0 => dlibMove,              // STREAM_SEEK_SET
                    1 => _position + dlibMove,  // STREAM_SEEK_CUR
                    2 => _length + dlibMove,    // STREAM_SEEK_END
                    _ => throw new ArgumentException("origin"),
                };

                if (target < 0)
                    throw new IOException("seek before the start of the stream");

                _position = target;

                if (plibNewPosition != IntPtr.Zero)
                    Marshal.WriteInt64(plibNewPosition, _position);
            }
        }

        public void SetSize(long libNewSize)
        {
            lock (_lock)
            {
                EnsureCapacity(libNewSize);
                _length = libNewSize;
            }
        }

        public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag)
        {
            lock (_lock)
            {
                pstatstg = new System.Runtime.InteropServices.ComTypes.STATSTG
                {
                    type = 2, // STGTY_STREAM
                    cbSize = _length,
                    grfMode = 2, // STGM_READWRITE
                };
            }
        }

        public void Commit(int grfCommitFlags) { }

        public void Revert() { }

        public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) => throw new NotSupportedException();

        public void LockRegion(long libOffset, long cb, int dwLockType) => throw new COMException("not supported", unchecked((int)0x80030001)); // STG_E_INVALIDFUNCTION

        public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new COMException("not supported", unchecked((int)0x80030001));

        public void Clone(out IStream ppstm) => throw new NotSupportedException();
    }
}
