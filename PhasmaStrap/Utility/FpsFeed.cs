namespace PhasmaStrap.Utility
{
    public static class FpsFeed
    {
        public enum Source { Hud = 0, Recorder = 1 }

        private static readonly object _lock = new();
        private static readonly double[] _value = new double[2];
        private static readonly long[] _stamp = new long[2];

        private const long FreshMs = 3000;

        public static void Report(Source source, double fps)
        {
            if (double.IsNaN(fps) || fps < 0 || fps > 2000)
                return;

            lock (_lock)
            {
                _value[(int)source] = fps;
                _stamp[(int)source] = Environment.TickCount64;
            }
        }

        public static double Get(Source source)
        {
            lock (_lock)
                return _stamp[(int)source] != 0 && Environment.TickCount64 - _stamp[(int)source] <= FreshMs ? _value[(int)source] : 0;
        }

        public static double Latest
        {
            get
            {
                long now = Environment.TickCount64;

                lock (_lock)
                {
                    for (int source = 0; source < _value.Length; source++)
                    {
                        if (_stamp[source] != 0 && now - _stamp[source] <= FreshMs)
                            return _value[source];
                    }
                }

                return 0;
            }
        }
    }
}
