namespace PhasmaStrap.Utility
{
    public sealed class TunerVariant
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "";
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
