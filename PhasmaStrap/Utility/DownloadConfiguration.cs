namespace PhasmaStrap.Utility
{
    /// <summary>
    /// Normalizes and exposes the choice lists backing the download tuning settings surfaced on
    /// Settings > Roblox > Installer (DownloadBufferKb / MaxConcurrentDownloads / MaxDownloadSegments -
    /// see Models/Persistable/Settings.cs), and read directly by Bootstrapper's package download path.
    ///
    /// Every setting's default reproduces PhasmaStrap's original hardcoded download behaviour exactly
    /// (one package at a time, a 4KB read buffer, no segmentation) - see the defaults on those
    /// properties - so none of this changes anything for a user unless they explicitly raise a value.
    /// </summary>
    public static class DownloadConfiguration
    {
        public static IReadOnlyList<int> BufferKbChoices { get; } = new[] { 4, 8, 16, 32, 64, 128, 256, 512, 1024 };

        public static IReadOnlyList<int> ConcurrentDownloadChoices { get; } = Enumerable.Range(1, 16).ToArray();

        public static IReadOnlyList<int> SegmentChoices { get; } = Enumerable.Range(1, 8).ToArray();

        // packages smaller than this aren't worth the extra ranged-request round trips
        public const long MinSegmentablePackageSize = 1024 * 1024; // 1 MB

        public static int NormalizeBufferKb(int value) =>
            BufferKbChoices.OrderBy(choice => Math.Abs((long)choice - value)).ThenBy(choice => choice).First();

        public static int NormalizeConcurrent(int value) =>
            Math.Clamp(value, ConcurrentDownloadChoices[0], ConcurrentDownloadChoices[^1]);

        public static int NormalizeSegments(int value) =>
            Math.Clamp(value, SegmentChoices[0], SegmentChoices[^1]);
    }
}
