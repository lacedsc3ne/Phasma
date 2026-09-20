namespace PhasmaStrap.Utility
{
    public static class DownloadConfiguration
    {
        public static IReadOnlyList<int> BufferKbChoices { get; } = new[] { 4, 8, 16, 32, 64, 128, 256, 512, 1024 };

        public static IReadOnlyList<int> ConcurrentDownloadChoices { get; } = Enumerable.Range(1, 16).ToArray();

        public static IReadOnlyList<int> SegmentChoices { get; } = Enumerable.Range(1, 8).ToArray();

        public const long MinSegmentablePackageSize = 1024 * 1024;

        public static int NormalizeBufferKb(int value) =>
            BufferKbChoices.OrderBy(choice => Math.Abs((long)choice - value)).ThenBy(choice => choice).First();

        public static int NormalizeConcurrent(int value) =>
            Math.Clamp(value, ConcurrentDownloadChoices[0], ConcurrentDownloadChoices[^1]);

        public static int NormalizeSegments(int value) =>
            Math.Clamp(value, SegmentChoices[0], SegmentChoices[^1]);
    }
}
