namespace PhasmaStrap.Utility
{
    public sealed class TunerVariant
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";       // "current", "none", "snapshot"
        public string Snapshot { get; set; } = "";
    }

    public sealed class TunerExperiment
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
        public DateTime StartedLocal { get; set; } = DateTime.Now;
        public string BackupSnapshot { get; set; } = "";
        public List<TunerVariant> Variants { get; set; } = new();
        public int Rounds { get; set; } = 2;
        public int SecondsPerRun { get; set; } = 90;
        public string AppliedVariant { get; set; } = "";
    }

    // The FastFlag auto-tuner: an A/B test of whole flag sets that the PLAYER drives.
    //
    // Roblox reads its flags once, when it starts, so comparing sets means one game launch per run.
    // PhasmaStrap never starts or restarts Roblox for this. A step only (1) puts that set's flags in
    // place and (2) leaves an order for the Watcher to measure the next game session; the player
    // joins the same game as always, plays, and gets a toast when the run is in. Sets are taken in
    // rounds (A B C, A B C) rather than back to back, so that a server getting fuller or the PC
    // warming up does not land on one set only.
    //
    // The flags in place when the experiment started are saved as a snapshot first and can be put
    // back at any point.
    internal static class FlagTuner
    {
        private const string LOG_IDENT = "FlagTuner";
        public const int SettleSeconds = 45;

        private static string StatePath => Path.Combine(Paths.Base, "Diagnostics", "experiment.json");

        public static TunerExperiment? Load()
        {
            try
            {
                return File.Exists(StatePath) ? JsonSerializer.Deserialize<TunerExperiment>(File.ReadAllText(StatePath)) : null;
            }
            catch
            {
                return null;
            }
        }

        private static void Save(TunerExperiment experiment)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
            File.WriteAllText(StatePath, JsonSerializer.Serialize(experiment, new JsonSerializerOptions { WriteIndented = true }));
        }

        public static TunerExperiment Start(List<TunerVariant> variants, int rounds, int secondsPerRun)
        {
            var experiment = new TunerExperiment { Variants = variants, Rounds = Math.Clamp(rounds, 1, 4), SecondsPerRun = Math.Clamp(secondsPerRun, 30, 600) };

            experiment.BackupSnapshot = $"Before auto-tuner {experiment.StartedLocal:yyyy-MM-dd HH.mm}";
            FastFlagSnapshotManager.Save(experiment.BackupSnapshot);

            Save(experiment);
            App.Logger.WriteLine(LOG_IDENT, $"Experiment {experiment.Id}: {string.Join(", ", variants.Select(v => v.Name))}, {experiment.Rounds} round(s) of {experiment.SecondsPerRun}s; current flags kept as '{experiment.BackupSnapshot}'");
            return experiment;
        }

        // every run the experiment wants, in the order they should be done
        public static List<(TunerVariant Variant, int Round)> Plan(TunerExperiment experiment)
        {
            var plan = new List<(TunerVariant, int)>();
            for (int round = 1; round <= experiment.Rounds; round++)
            {
                foreach (TunerVariant variant in experiment.Variants)
                    plan.Add((variant, round));
            }
            return plan;
        }

        public static List<PerformanceReport> RunsOf(TunerExperiment experiment) =>
            PerformanceRuns.List().Where(r => r.Experiment == experiment.Id).ToList();

        // the next run that has not been measured yet, or null when the plan is complete
        public static (TunerVariant Variant, int Round)? Next(TunerExperiment experiment)
        {
            Dictionary<string, int> done = RunsOf(experiment).GroupBy(r => r.Variant).ToDictionary(g => g.Key, g => g.Count());

            foreach ((TunerVariant variant, int round) in Plan(experiment))
            {
                if (done.GetValueOrDefault(variant.Name) < round)
                    return (variant, round);
            }

            return null;
        }

        private static FastFlagSnapshot? Resolve(TunerExperiment experiment, TunerVariant variant)
        {
            return variant.Kind switch
            {
                "none" => new FastFlagSnapshot { Name = variant.Name },
                "current" => FastFlagSnapshotManager.List().FirstOrDefault(s => s.Name == experiment.BackupSnapshot),
                _ => FastFlagSnapshotManager.List().FirstOrDefault(s => s.Name == variant.Snapshot),
            };
        }

        // puts the variant's flags in place and orders the measurement of the next game session
        public static void Arm(TunerExperiment experiment, TunerVariant variant, int round)
        {
            FastFlagSnapshot snapshot = Resolve(experiment, variant)
                ?? throw new InvalidOperationException($"The FastFlag snapshot behind \"{variant.Name}\" no longer exists.");

            FastFlagSnapshotManager.Apply(snapshot);

            experiment.AppliedVariant = variant.Name;
            Save(experiment);

            PerformanceRuns.WriteRequest(new MeasureRequest
            {
                Label = $"Auto-tuner: {variant.Name} (round {round})",
                Seconds = experiment.SecondsPerRun,
                DelayAfterJoinSeconds = SettleSeconds,
                Experiment = experiment.Id,
                Variant = variant.Name,
            });

            App.Logger.WriteLine(LOG_IDENT, $"Armed '{variant.Name}' round {round}: {snapshot.Flags.Count} flag(s) in place, measurement ordered");
        }

        // ends the experiment with the given flags in place (null = the ones from before it started)
        public static void Finish(TunerExperiment experiment, TunerVariant? keep)
        {
            FastFlagSnapshot? snapshot = keep is null
                ? FastFlagSnapshotManager.List().FirstOrDefault(s => s.Name == experiment.BackupSnapshot)
                : Resolve(experiment, keep);

            if (snapshot is not null)
                FastFlagSnapshotManager.Apply(snapshot);

            PerformanceRuns.ClearRequest();

            try { File.Delete(StatePath); } catch { }
            App.Logger.WriteLine(LOG_IDENT, $"Experiment {experiment.Id} finished, flags in place: {(keep is null ? "the original set" : keep.Name)}");
        }
    }
}
