namespace PhasmaStrap.Utility
{
    public sealed class TunerVariantResult
    {
        public string Variant = "";
        public int Runs;
        public double AverageFps, Low1Fps, StuttersPerMinute;
        public double SpreadPercent;        // how far this variant's own runs lie apart (1 % lows)
        public bool Capped;
    }

    public sealed class TunerComparison
    {
        public List<TunerVariantResult> Results = new();     // best first
        public string Winner = "";                           // "" = no real difference
        public string Verdict = "";
        public List<string> Notes = new();
    }

    // The auto-tuner's arithmetic: which flag set really ran better, and is the difference bigger
    // than what two runs of the SAME flags differ by anyway?
    //
    // Ranked by the 1 % lows, not the average: the average is what a frame cap pins, and the lows
    // are what is felt. A winner is only declared when it beats the runner-up by more than the
    // run-to-run noise measured in this very experiment (and at least 5 %) - otherwise the honest
    // answer is "no real difference", which for most FastFlag sets it is.
    //
    // Pure functions, no App dependencies.
    public static class FlagTunerStats
    {
        public static TunerComparison Compare(IEnumerable<PerformanceReport> runs)
        {
            var comparison = new TunerComparison();

            foreach (IGrouping<string, PerformanceReport> group in runs.Where(r => r.Variant.Length > 0).GroupBy(r => r.Variant))
            {
                List<PerformanceReport> list = group.ToList();
                double low = list.Average(r => r.Low1Fps);

                comparison.Results.Add(new TunerVariantResult
                {
                    Variant = group.Key,
                    Runs = list.Count,
                    AverageFps = list.Average(r => r.AverageFps),
                    Low1Fps = low,
                    StuttersPerMinute = list.Average(r => r.StuttersPerMinute),
                    SpreadPercent = list.Count > 1 && low > 0 ? (list.Max(r => r.Low1Fps) - list.Min(r => r.Low1Fps)) / low * 100 : -1,
                    Capped = list.All(r => r.CapFps > 0),
                });
            }

            comparison.Results = comparison.Results.OrderByDescending(r => r.Low1Fps).ThenByDescending(r => r.AverageFps).ToList();

            if (comparison.Results.Count < 2)
            {
                comparison.Verdict = "Not enough to compare yet - at least two flag sets need a finished run.";
                return comparison;
            }

            List<double> spreads = comparison.Results.Where(r => r.SpreadPercent >= 0).Select(r => r.SpreadPercent).ToList();
            bool repeated = spreads.Count == comparison.Results.Count;

            // 10 % is what identical runs of a live game typically differ by; only when EVERY set
            // has been repeated is this experiment's own (possibly smaller) figure trusted instead
            double noise = repeated ? Math.Max(5, spreads.Max()) : Math.Max(10, spreads.Count > 0 ? spreads.Max() : 10);

            TunerVariantResult best = comparison.Results[0], second = comparison.Results[1];
            double lead = second.Low1Fps > 0 ? (best.Low1Fps - second.Low1Fps) / second.Low1Fps * 100 : 0;

            if (lead > noise)
            {
                comparison.Winner = best.Variant;
                comparison.Verdict = $"\"{best.Variant}\" ran better: its slowest 1 % of frames averaged {best.Low1Fps:0} fps against {second.Low1Fps:0} fps for \"{second.Variant}\" - {lead:0} % ahead, more than the {noise:0} % that runs of the same flags differ by.";
            }
            else
            {
                comparison.Verdict = $"No real difference. \"{best.Variant}\" came out on top, but only {lead:0.#} % ahead of \"{second.Variant}\" - within the {noise:0} % that two runs of the very same flags differ by. Keep whichever set you prefer for other reasons.";
            }

            if (!repeated)
                comparison.Notes.Add("Not every flag set has been run twice, so the run-to-run noise is partly an assumption (10 %). A second round makes the verdict trustworthy.");

            if (comparison.Results.All(r => r.Capped))
                comparison.Notes.Add("Every run sat at a frame cap, so the average fps cannot differ. The 1 % lows and the stutter counts still can - but for a real comparison, raise or remove the cap first.");

            TunerVariantResult calmest = comparison.Results.OrderBy(r => r.StuttersPerMinute).First();
            if (calmest.Variant != best.Variant && best.StuttersPerMinute - calmest.StuttersPerMinute >= 3)
                comparison.Notes.Add($"\"{calmest.Variant}\" stuttered least ({calmest.StuttersPerMinute:0.#} a minute against {best.StuttersPerMinute:0.#}).");

            comparison.Notes.Add("This only means something if every run was the same game, the same place in it and the same kind of action - a quiet lobby against a full fight is not a flag comparison.");

            return comparison;
        }
    }
}
