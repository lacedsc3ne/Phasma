namespace PhasmaStrap
{
    // https://stackoverflow.com/a/53873141/11852173

    public class Logger
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private FileStream? _filestream;

        public readonly List<string> History = new();
        public bool Initialized = false;
        public bool NoWriteMode = false;
        public string? FileLocation;

        public string AsDocument => String.Join('\n', History);

        public void Initialize(bool useTempDir = false)
        {
            const string LOG_IDENT = "Logger::Initialize";

            string directory = useTempDir ? Path.Combine(Paths.TempLogs) : Path.Combine(Paths.Base, "Logs");
            string timestamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");

            // process ID keeps this unique even when two real PhasmaStrap processes start within the same
            // UTC second (e.g. two near-simultaneous browser Play clicks) - without it, the second process's
            // File.Exists check below would see the first process's just-created log file at the identical
            // path, take that as "another instance beat me to it", and silently self-terminate below without
            // ever processing its own launch request (dropping that Play click with no error, no fallback)
            string filename = $"{App.ProjectName}_{timestamp}_{Environment.ProcessId}.log";
            string location = Path.Combine(directory, filename);

            WriteLine(LOG_IDENT, $"Initializing at {location}");

            if (Initialized)
            {
                WriteLine(LOG_IDENT, "Failed to initialize because logger is already initialized");
                return;
            }

            Directory.CreateDirectory(directory);

            if (File.Exists(location))
            {
                WriteLine(LOG_IDENT, "Failed to initialize because log file already exists");
                return;
            }

            try
            {
                _filestream = File.Open(location, FileMode.Create, FileAccess.Write, FileShare.Read);
            }
            catch (IOException)
            {
                WriteLine(LOG_IDENT, "Failed to initialize because log file already exists");
                return;
            }
            catch (UnauthorizedAccessException)
            {
                if (NoWriteMode)
                    return;

                WriteLine(LOG_IDENT, $"Failed to initialize because PhasmaStrap cannot write to {directory}");

                Frontend.ShowMessageBox(
                    String.Format(Strings.Logger_NoWriteMode, directory), 
                    System.Windows.MessageBoxImage.Warning, 
                    System.Windows.MessageBoxButton.OK
                );

                NoWriteMode = true;

                return;
            }
            

            Initialized = true;

            if (History.Count > 0)
                WriteToLog(string.Join("\r\n", History));

            WriteLine(LOG_IDENT, "Finished initializing!");

            FileLocation = location;

            // delete older logs if there are more than 15
            if (Paths.Initialized && Directory.Exists(Paths.Logs))
            {
                const int maxLogs = 15;
                FileInfo[] logs = new DirectoryInfo(Paths.Logs).GetFiles();

                if (logs.Length <= maxLogs)
                    return;

                foreach (FileInfo log in logs.OrderByDescending(log => log.LastWriteTimeUtc).Skip(maxLogs))
                {
                    WriteLine(LOG_IDENT, $"Cleaning up old log file '{log.Name}'");

                    try
                    {
                       log.Delete();
                    }
                    catch (Exception ex)
                    {
                        WriteLine(LOG_IDENT, "Failed to delete log!");
                        WriteException(LOG_IDENT, ex);
                    }
                }
            }
        }

        private void WriteLine(string message)
        {
            string timestamp = DateTime.UtcNow.ToString("s") + "Z";
            string outcon = $"{timestamp} {message}";
            string outlog = outcon.Replace(Paths.UserProfile, "%UserProfile%", StringComparison.InvariantCultureIgnoreCase);

            Debug.WriteLine(outcon);
            WriteToLog(outlog);

            History.Add(outlog);
        }

        public void WriteLine(string identifier, string message) => WriteLine($"[{identifier}] {message}");

        public void WriteException(string identifier, Exception ex)
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;

            string hresult = "0x" + ex.HResult.ToString("X8");

            WriteLine($"[{identifier}] ({hresult}) {ex}");

            Thread.CurrentThread.CurrentUICulture = Locale.CurrentCulture;
        }

        private async void WriteToLog(string message)
        {
            if (!Initialized)
                return;

            try
            {
                await _semaphore.WaitAsync();
                await _filestream!.WriteAsync(Encoding.UTF8.GetBytes($"{message}\r\n"));

                _ = _filestream.FlushAsync();
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
