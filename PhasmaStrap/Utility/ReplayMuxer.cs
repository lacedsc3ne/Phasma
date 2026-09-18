using System.Runtime.InteropServices;
using Vortice.MediaFoundation;

namespace PhasmaStrap.Utility
{
    // Turns the newest in-memory segments of GpuReplayRecorder into one MP4.
    //
    // The video is NOT re-encoded: every segment is read back as the H.264 it already is and
    // written on with shifted timestamps, which is why saving takes a fraction of a second. Sound
    // is different - it is kept as plain PCM (ReplayAudio) exactly so that it can be encoded here
    // in one pass; AAC encoded per segment would click at every join.
    //
    // Segments are laid end to end. Where the game was out of sight between two of them, the clip
    // simply cuts - and the sound is taken per segment too, so it cuts in the same places.
    internal static class ReplayMuxer
    {
        public static Action<string>? Log;

        private const int FirstVideoStream = unchecked((int)0xFFFFFFFC);
        private const int FlagEndOfStream = 0x2;

        public const int AudioRate = 48000;
        public const int AudioChannels = 2;
        public const int AudioBytesPerFrame = AudioChannels * 2; // 16 bit

        // pcm(startTicks, endTicks) -> interleaved 16-bit 48 kHz stereo covering exactly that span, or null for a silent clip
        public static void Mux(GpuReplayRecorder.Cut cut, string path, Func<long, long, byte[]?>? pcm)
        {
            MediaFactory.MFStartup(false);

            IMFSinkWriter? writer = null;
            bool finished = false;

            try
            {
                using IMFAttributes attributes = MediaFactory.MFCreateAttributes(1);
                MfInterop.SetUInt32(attributes, MfInterop.MF_SINK_WRITER_DISABLE_THROTTLING, 1);

                writer = MfInterop.CreateSinkWriter(path, IntPtr.Zero, attributes);

                int videoStream = -1, audioStream = -1;
                long offset = 0;
                int samples = 0;

                foreach (GpuReplayRecorder.Segment segment in cut.Segments)
                {
                    MfInterop.ByteStreamRewind(segment.ByteStream);
                    using IMFSourceReader reader = MfInterop.CreateSourceReader(segment.ByteStream);

                    if (videoStream < 0)
                    {
                        // same type in and out = the sink writer passes the H.264 through untouched
                        using IMFMediaType native = reader.GetNativeMediaType(FirstVideoStream, 0);
                        videoStream = writer.AddStream(native);
                        writer.SetInputMediaType(videoStream, native, null);

                        if (pcm is not null)
                            audioStream = AddAudioStream(writer);

                        writer.BeginWriting();
                    }

                    long length = segment.EndTicks - segment.StartTicks;

                    while (true)
                    {
                        reader.ReadSample(FirstVideoStream, 0, out int _, out int flags, out long timestamp, out IMFSample? sample);

                        if (sample is not null)
                        {
                            using (sample)
                            {
                                sample.SampleTime = offset + timestamp;
                                writer.WriteSample(videoStream, sample);
                                samples++;
                            }
                        }

                        if ((flags & FlagEndOfStream) != 0)
                            break;
                    }

                    if (audioStream >= 0)
                        WriteAudio(writer, audioStream, pcm!(segment.StartTicks, segment.EndTicks), offset);

                    offset += length;
                }

                if (samples == 0)
                    throw new InvalidOperationException("the buffered segments held no frames");

                writer.Finalize();
                finished = true;
            }
            finally
            {
                writer?.Dispose();

                if (!finished)
                {
                    try { if (File.Exists(path)) File.Delete(path); } catch { }
                }

                try { MediaFactory.MFShutdown(); } catch { }
            }
        }

        private static int AddAudioStream(IMFSinkWriter writer)
        {
            using IMFMediaType output = MediaFactory.MFCreateMediaType();
            output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            output.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
            MfInterop.SetUInt32(output, MediaTypeAttributeKeys.AudioSamplesPerSecond, AudioRate);
            MfInterop.SetUInt32(output, MediaTypeAttributeKeys.AudioNumChannels, AudioChannels);
            MfInterop.SetUInt32(output, MediaTypeAttributeKeys.AudioBitsPerSample, 16);
            MfInterop.SetUInt32(output, MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 24000); // 192 kbps

            using IMFMediaType input = MediaFactory.MFCreateMediaType();
            input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            input.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
            MfInterop.SetUInt32(input, MediaTypeAttributeKeys.AudioSamplesPerSecond, AudioRate);
            MfInterop.SetUInt32(input, MediaTypeAttributeKeys.AudioNumChannels, AudioChannels);
            MfInterop.SetUInt32(input, MediaTypeAttributeKeys.AudioBitsPerSample, 16);
            MfInterop.SetUInt32(input, MediaTypeAttributeKeys.AudioBlockAlignment, AudioBytesPerFrame);
            MfInterop.SetUInt32(input, MediaTypeAttributeKeys.AudioAvgBytesPerSecond, AudioRate * AudioBytesPerFrame);

            int stream = writer.AddStream(output);
            writer.SetInputMediaType(stream, input, null);
            return stream;
        }

        private static void WriteAudio(IMFSinkWriter writer, int stream, byte[]? pcm, long offset)
        {
            if (pcm is null || pcm.Length < AudioBytesPerFrame)
                return;

            const int ChunkFrames = AudioRate / 10; // 100 ms
            int totalFrames = pcm.Length / AudioBytesPerFrame;

            for (int frame = 0; frame < totalFrames; frame += ChunkFrames)
            {
                int frames = Math.Min(ChunkFrames, totalFrames - frame);
                int bytes = frames * AudioBytesPerFrame;

                using IMFMediaBuffer buffer = MediaFactory.MFCreateMemoryBuffer(bytes);
                buffer.Lock(out IntPtr pointer, out int _, out int _);
                Marshal.Copy(pcm, frame * AudioBytesPerFrame, pointer, bytes);
                buffer.Unlock();
                buffer.CurrentLength = bytes;

                using IMFSample sample = MediaFactory.MFCreateSample();
                sample.AddBuffer(buffer);
                sample.SampleTime = offset + frame * 10_000_000L / AudioRate;
                sample.SampleDuration = frames * 10_000_000L / AudioRate;

                writer.WriteSample(stream, sample);
            }
        }
    }
}
