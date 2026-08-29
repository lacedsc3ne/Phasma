using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PhasmaStrap.Server.Common;

// Polyfills for Stream.ReadExactly/ReadExactlyAsync (introduced in .NET 7), since
// PhasmaStrap.Server intentionally targets net6.0 to match PhasmaStrap's TFM major version.
internal static class StreamCompat
{
	public static void ReadExactly(this Stream stream, byte[] buffer)
	{
		int offset = 0;
		while (offset < buffer.Length)
		{
			int read = stream.Read(buffer, offset, buffer.Length - offset);
			if (read <= 0)
				throw new EndOfStreamException();
			offset += read;
		}
	}

	public static async Task ReadExactlyAsync(this Stream stream, byte[] buffer, CancellationToken cancellationToken)
	{
		int offset = 0;
		while (offset < buffer.Length)
		{
			int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
			if (read <= 0)
				throw new EndOfStreamException();
			offset += read;
		}
	}
}
