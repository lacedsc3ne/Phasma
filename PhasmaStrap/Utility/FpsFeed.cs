namespace PhasmaStrap.Utility
{
    // The game's current frame rate, for whoever wants it (the session timeline's FPS graph).
    //
    // PhasmaStrap has no way to ask Roblox for its frame rate, but two parts of it see the game's
    // frames go by while they are running: the overlay compositor (the number on the HUD) and the
    // GPU replay recorder (desktop duplication reports how many frames were presented between two
    // grabs). Both report here once a second. The HUD's figure wins when both are alive, since it
    // is the one the user is looking at. When neither runs there simply is no reading.
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

        /// <summary>One source's reading, or 0 when it hasn't reported in the last few seconds.</summary>
        public static double Get(Source source)
        {
            lock (_lock)
                return _stamp[(int)source] != 0 && Environment.TickCount64 - _stamp[(int)source] <= FreshMs ? _value[(int)source] : 0;
        }

        /// <summary>The newest reading, or 0 when nothing has reported in the last few seconds.</summary>
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
