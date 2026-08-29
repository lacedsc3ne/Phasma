using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;

namespace PhasmaStrap.Utility
{
    /// <summary>
    /// Runtime machine translation for user-facing text (GameChat overlay messages, Discord Rich
    /// Presence strings), ported from Voidstrap's TranslationService.
    ///
    /// Translation calls hit Google's own unofficial "gtx" translate endpoint
    /// (translate.googleapis.com) - the same undocumented endpoint the well-known Python
    /// `googletrans` library talks to. It isn't Voidstrap-owned infrastructure, so it's ported
    /// unchanged, URLs and all.
    ///
    /// Voidstrap's original also pushed/pulled translated strings to/from a Voidstrap-owned
    /// "shared translation cache" web service (App.WebsiteBaseUrl + "/api/translations", see
    /// RemoteApi/EnsureRemoteFetched/FetchLanguageAsync/PushRemoteAsync/LoadRemoteMeta/
    /// SaveRemoteMeta/_remoteSeen/_remoteFetchStarted/RemoteMetaPath in the source). That whole
    /// mechanism has been removed here - PhasmaStrap has no such backend and shouldn't depend on
    /// one. Only the local on-disk JSON cache remains, so every install builds up its own cache
    /// locally over time instead of pulling a community-shared one.
    ///
    /// Voidstrap also called `Voidstrap.UI.LiveLanguageRefresher.TranslateOpenWindows()` after new
    /// translations landed, to live-refresh already-open windows. PhasmaStrap has no equivalent
    /// UI-refresh subsystem, so that call is replaced with a lightweight <see cref="CacheUpdated"/>
    /// event that anything interested (e.g. the GameChat overlay) can subscribe to instead of this
    /// class reaching into UI code directly.
    /// </summary>
    public static class TranslationService
    {
        private const int BatchCount = 48;
        private const int BatchChars = 4000;

        private static readonly string CacheDirectory = Path.Combine(Paths.LocalAppData, "PhasmaStrap", "Translations");
        private static string CachePath => Path.Combine(CacheDirectory, "Translations.json");

        private static ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _cache = new();
        private static readonly ConcurrentDictionary<string, byte> _outputs = new();
        private static readonly ConcurrentDictionary<string, string> _sources = new();
        private static readonly ConcurrentDictionary<string, byte> _pending = new();
        private static readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingTranslations = new();
        private static readonly ConcurrentQueue<(string Text, string TargetLang)> _queue = new();
        private static readonly SemaphoreSlim _net = new SemaphoreSlim(6, 6);

        private static bool _initialized;
        private static int _processing;
        private static DispatcherTimer? _saveTimer;

        /// <summary>
        /// Raised (off the UI thread) whenever newly-arrived background translations were merged
        /// into the cache. Anything that renders already-translated text (e.g. the GameChat
        /// overlay) can subscribe and re-render on the dispatcher thread if it wants live updates;
        /// nothing subscribes by default.
        /// </summary>
        public static event Action? CacheUpdated;

        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            LoadCache();

            try
            {
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
                    _saveTimer.Tick += OnSaveTimerTick;
                }));
            }
            catch { }
        }

        public static void Shutdown()
        {
            var timer = _saveTimer;
            _saveTimer = null;
            if (timer != null)
            {
                try
                {
                    timer.Stop();
                    timer.Tick -= OnSaveTimerTick;
                }
                catch { }
            }
            SaveCacheBlocking();
        }

        private static void OnSaveTimerTick(object? sender, EventArgs e)
        {
            _saveTimer?.Stop();
            SaveCache();
        }

        private static string Key(string lang, string text) => lang + "\x01" + text;

        /// <summary>
        /// Non-blocking lookup - returns the original text immediately if no cached translation
        /// exists yet, and kicks off a background translation for next time. Safe to call from the
        /// UI thread (e.g. while rendering a chat message).
        /// </summary>
        public static string Translate(string text, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            targetLanguage = NormalizeLang(targetLanguage);
            if (string.IsNullOrEmpty(targetLanguage) || targetLanguage == "en") return text;
            if (LooksLikeUrl(text) || !ContainsTranslatableText(text)) return text;

            if (!_initialized) Initialize();

            var bucket = _cache.GetOrAdd(targetLanguage, _ => new ConcurrentDictionary<string, string>());
            if (bucket.TryGetValue(text, out string? cached))
            {
                RememberTranslation(targetLanguage, text, cached);
                return cached;
            }

            if (_pending.TryAdd(Key(targetLanguage, text), 0))
            {
                _queue.Enqueue((text, targetLanguage));
                StartProcessor();
            }

            return text;
        }

        /// <summary>
        /// Awaits a translation (with a bounded timeout, falling back to the original text on
        /// failure/timeout). Only call this from a background/async context that's fine waiting -
        /// never from a path that must not stall the UI thread.
        /// </summary>
        public static async Task<string> TranslateAsync(string text, string targetLanguage)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            targetLanguage = NormalizeLang(targetLanguage);
            if (string.IsNullOrEmpty(targetLanguage) || targetLanguage == "en") return text;
            if (LooksLikeUrl(text) || !ContainsTranslatableText(text)) return text;

            if (!_initialized) Initialize();

            var bucket = _cache.GetOrAdd(targetLanguage, _ => new ConcurrentDictionary<string, string>());
            if (bucket.TryGetValue(text, out string? cached))
            {
                RememberTranslation(targetLanguage, text, cached);
                return cached;
            }

            string key = Key(targetLanguage, text);
            var tcs = _pendingTranslations.GetOrAdd(key, _ => new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously));

            if (bucket.TryGetValue(text, out cached))
            {
                _pendingTranslations.TryRemove(key, out _);
                RememberTranslation(targetLanguage, text, cached);
                return cached;
            }

            if (_pending.TryAdd(key, 0))
            {
                _queue.Enqueue((text, targetLanguage));
                StartProcessor();
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            try
            {
                return await tcs.Task.WaitAsync(cts.Token).ConfigureAwait(false);
            }
            catch
            {
                return text;
            }
        }

        public static bool IsTranslated(string? text, string targetLanguage)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            return _outputs.ContainsKey(Key(NormalizeLang(targetLanguage), text));
        }

        public static bool TryGetOriginal(string? text, string targetLanguage, out string original)
        {
            original = "";
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }
            return _sources.TryGetValue(Key(NormalizeLang(targetLanguage), text), out original!);
        }

        private static void RememberTranslation(string lang, string source, string translated)
        {
            string outputKey = Key(NormalizeLang(lang), translated);
            _outputs.TryAdd(outputKey, 0);
            _sources.TryAdd(outputKey, source);
        }

        private static bool LooksLikeUrl(string text)
        {
            return text.Contains("://", StringComparison.OrdinalIgnoreCase)
                || text.Contains("www.", StringComparison.OrdinalIgnoreCase)
                || text.Contains(".com", StringComparison.OrdinalIgnoreCase)
                || text.Contains(".net", StringComparison.OrdinalIgnoreCase)
                || text.Contains(".org", StringComparison.OrdinalIgnoreCase)
                || text.Contains(".gg", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsTranslatableText(string text)
        {
            bool containsLetter = false;
            for (int index = 0; index < text.Length; index++)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(text, index);
                if (category == UnicodeCategory.PrivateUse)
                {
                    return false;
                }
                if (category is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter)
                {
                    containsLetter = true;
                }
                if (char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
                {
                    index++;
                }
            }
            return containsLetter;
        }

        private static string NormalizeLang(string lang)
        {
            if (string.IsNullOrWhiteSpace(lang)) return "";
            switch (lang)
            {
                case "nil": return "";
                case "en-US": return "en";
                case "es-ES": return "es";
                case "pt-BR": return "pt";
                case "sv-SE": return "sv";
                case "fil": return "tl";
                case "zh-CN": return "zh-CN";
                case "zh-TW": return "zh-TW";
            }
            int dash = lang.IndexOf('-');
            if (dash > 0 && !lang.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return lang.Substring(0, dash);
            return lang;
        }

        private static void StartProcessor()
        {
            if (Interlocked.CompareExchange(ref _processing, 1, 0) != 0) return;
            _ = Task.Run(ProcessQueueAsync);
        }

        private static async Task ProcessQueueAsync()
        {
            try
            {
                while (!_queue.IsEmpty)
                {
                    var byLang = new Dictionary<string, List<string>>();
                    int taken = 0;
                    while (taken < BatchCount * 6 && _queue.TryDequeue(out var item))
                    {
                        if (!byLang.TryGetValue(item.TargetLang, out var list))
                            byLang[item.TargetLang] = list = new List<string>();
                        list.Add(item.Text);
                        taken++;
                    }

                    if (taken == 0) break;

                    var jobs = new List<Task<bool>>();
                    foreach (var kv in byLang)
                        foreach (var chunk in Chunk(kv.Value))
                            jobs.Add(TranslateChunkAsync(chunk, kv.Key));

                    bool any = false;
                    foreach (bool changed in await Task.WhenAll(jobs).ConfigureAwait(false))
                        any |= changed;

                    if (any)
                    {
                        QueueSave();
                        try { CacheUpdated?.Invoke(); }
                        catch (Exception ex) { App.Logger.WriteLine("TranslationService::ProcessQueue", $"CacheUpdated subscriber threw: {ex.Message}"); }
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("TranslationService::ProcessQueue", $"Error: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _processing, 0);
                if (!_queue.IsEmpty) StartProcessor();
            }
        }

        private static IEnumerable<List<string>> Chunk(List<string> texts)
        {
            var current = new List<string>();
            int chars = 0;

            foreach (string text in texts)
            {
                if (current.Count > 0 && (current.Count >= BatchCount || chars + text.Length > BatchChars))
                {
                    yield return current;
                    current = new List<string>();
                    chars = 0;
                }
                current.Add(text);
                chars += text.Length;
            }

            if (current.Count > 0)
                yield return current;
        }

        private static async Task<bool> TranslateChunkAsync(List<string> texts, string lang)
        {
            var bucket = _cache.GetOrAdd(lang, _ => new ConcurrentDictionary<string, string>());
            var need = texts.Where(t => !bucket.ContainsKey(t)).Distinct().ToList();

            bool any = false;
            bool stored = false;

            try
            {
                if (need.Count > 0)
                {
                    string?[] results = await TranslateBatchAsync(need, lang).ConfigureAwait(false);
                    for (int i = 0; i < need.Count; i++)
                    {
                        string? translated = results[i];
                        if (string.IsNullOrEmpty(translated))
                            continue;

                        bucket[need[i]] = translated!;
                        stored = true;

                        if (translated == need[i])
                            continue;

                        RememberTranslation(lang, need[i], translated!);
                        any = true;
                    }
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("TranslationService::TranslateChunk", $"Error: {ex.Message}");
            }
            finally
            {
                foreach (string text in texts)
                {
                    string key = Key(lang, text);
                    bucket.TryGetValue(text, out string? done);
                    if (_pendingTranslations.TryRemove(key, out var tcs))
                        tcs.TrySetResult(done ?? text);
                    _pending.TryRemove(key, out _);
                }
            }

            if (stored)
                QueueSave();

            return any;
        }

        private static async Task<string?[]> TranslateBatchAsync(List<string> texts, string targetLanguage)
        {
            var results = new string?[texts.Count];

            await _net.WaitAsync().ConfigureAwait(false);
            try
            {
                string url = $"https://translate.googleapis.com/translate_a/t?client=gtx&sl=auto&tl={Uri.EscapeDataString(targetLanguage)}";

                var form = new List<KeyValuePair<string, string>>(texts.Count);
                foreach (string text in texts)
                    form.Add(new KeyValuePair<string, string>("q", text));

                using var content = new FormUrlEncodedContent(form);
                using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
                using var response = await App.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                string body = await Http.ReadStringBoundedAsync(response.Content, 4 * 1024 * 1024, CancellationToken.None).ConfigureAwait(false);
                var parsed = ParseBatch(body, texts.Count);
                if (parsed != null)
                    return parsed;

                App.Logger.WriteLine("TranslationService::TranslateBatch", $"Unexpected batch shape for {texts.Count} entries, falling back");
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("TranslationService::TranslateBatch", $"Error: {ex.Message}");
            }
            finally
            {
                _net.Release();
            }

            var singles = await Task.WhenAll(texts.Select(t => TranslateOneAsync(t, targetLanguage))).ConfigureAwait(false);
            for (int i = 0; i < singles.Length; i++)
                results[i] = singles[i];
            return results;
        }

        private static string?[]? ParseBatch(string json, int expected)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                    return null;

                if (expected == 1 && root.GetArrayLength() >= 1 && root[0].ValueKind == JsonValueKind.String)
                    return new[] { root[0].GetString() };

                if (root.GetArrayLength() != expected)
                    return null;

                var results = new string?[expected];
                int index = 0;
                foreach (var element in root.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.String)
                        results[index] = element.GetString();
                    else if (element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > 0 && element[0].ValueKind == JsonValueKind.String)
                        results[index] = element[0].GetString();
                    else
                        return null;
                    index++;
                }
                return results;
            }
            catch
            {
                return null;
            }
        }

        private static async Task<string> TranslateOneAsync(string text, string targetLanguage)
        {
            await _net.WaitAsync().ConfigureAwait(false);
            try
            {
                string url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={Uri.EscapeDataString(targetLanguage)}&dt=t&q={Uri.EscapeDataString(text)}";
                string response = await Http.GetStringBoundedAsync(App.HttpClient, url, maxBytes: 1024 * 1024).ConfigureAwait(false);

                using var doc = JsonDocument.Parse(response);
                var segments = doc.RootElement[0];
                var builder = new StringBuilder();
                foreach (var item in segments.EnumerateArray())
                {
                    if (item.GetArrayLength() > 0 && item[0].ValueKind == JsonValueKind.String)
                        builder.Append(item[0].GetString());
                }
                return builder.ToString();
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("TranslationService::TranslateOneAsync", $"Error: {ex.Message}");
                return text;
            }
            finally
            {
                _net.Release();
            }
        }

        private static void LoadCache()
        {
            string cachePath = CachePath;
            if (!File.Exists(cachePath)) return;
            try
            {
                string json = File.ReadAllText(cachePath);
                var dict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(json);
                if (dict != null)
                {
                    var rebuilt = new ConcurrentDictionary<string, ConcurrentDictionary<string, string>>();
                    foreach (var outer in dict)
                    {
                        rebuilt[outer.Key] = new ConcurrentDictionary<string, string>(outer.Value);
                        foreach (var pair in outer.Value)
                            if (!string.IsNullOrEmpty(pair.Value)) RememberTranslation(outer.Key, pair.Key, pair.Value);
                    }
                    _cache = rebuilt;
                }
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("TranslationService::LoadCache", $"Error: {ex.Message}");
                _cache = new();
            }
        }

        private static void QueueSave()
        {
            try { Application.Current?.Dispatcher.BeginInvoke(new Action(() => { _saveTimer?.Stop(); _saveTimer?.Start(); })); }
            catch { SaveCache(); }
        }

        private static int _saving;

        private static void SaveCache()
        {
            if (Interlocked.CompareExchange(ref _saving, 1, 0) != 0)
                return;

            var snapshot = _cache.ToDictionary(k => k.Key, v => v.Value.ToDictionary(ik => ik.Key, iv => iv.Value));

            _ = Task.Run(() =>
            {
                try
                {
                    WriteCacheFile(snapshot);
                }
                catch (Exception ex)
                {
                    App.Logger.WriteLine("TranslationService::SaveCache", $"Error: {ex.Message}");
                }
                finally
                {
                    Interlocked.Exchange(ref _saving, 0);
                }
            });
        }

        private static void SaveCacheBlocking()
        {
            try
            {
                var snapshot = _cache.ToDictionary(k => k.Key, v => v.Value.ToDictionary(ik => ik.Key, iv => iv.Value));
                WriteCacheFile(snapshot);
            }
            catch (Exception ex)
            {
                App.Logger.WriteLine("TranslationService::SaveCacheBlocking", $"Error: {ex.Message}");
            }
        }

        // atomic-ish write: serialize to a temp file in the same directory, then replace the real
        // file in one filesystem operation so a crash mid-write can't leave a truncated cache
        private static void WriteCacheFile(Dictionary<string, Dictionary<string, string>> snapshot)
        {
            if (!Directory.Exists(CacheDirectory))
                Directory.CreateDirectory(CacheDirectory);

            string cachePath = CachePath;
            string tempPath = cachePath + ".tmp";
            string json = JsonSerializer.Serialize(snapshot);
            File.WriteAllText(tempPath, json);

            if (File.Exists(cachePath))
                File.Replace(tempPath, cachePath, null);
            else
                File.Move(tempPath, cachePath);
        }

        public static readonly Dictionary<string, string> AvailableLanguages = new()
        {
            { "af", "Afrikaans" },
            { "sq", "Albanian" },
            { "am", "Amharic" },
            { "ar", "Arabic" },
            { "hy", "Armenian" },
            { "az", "Azerbaijani" },
            { "eu", "Basque" },
            { "be", "Belarusian" },
            { "bn", "Bengali" },
            { "bs", "Bosnian" },
            { "bg", "Bulgarian" },
            { "ca", "Catalan" },
            { "ceb", "Cebuano" },
            { "ny", "Chichewa" },
            { "zh-CN", "Chinese (Simplified)" },
            { "zh-TW", "Chinese (Traditional)" },
            { "co", "Corsican" },
            { "hr", "Croatian" },
            { "cs", "Czech" },
            { "da", "Danish" },
            { "nl", "Dutch" },
            { "en", "English" },
            { "eo", "Esperanto" },
            { "et", "Estonian" },
            { "tl", "Filipino" },
            { "fi", "Finnish" },
            { "fr", "French" },
            { "fy", "Frisian" },
            { "gl", "Galician" },
            { "ka", "Georgian" },
            { "de", "German" },
            { "el", "Greek" },
            { "gu", "Gujarati" },
            { "ht", "Haitian Creole" },
            { "ha", "Hausa" },
            { "haw", "Hawaiian" },
            { "iw", "Hebrew" },
            { "hi", "Hindi" },
            { "hmn", "Hmong" },
            { "hu", "Hungarian" },
            { "is", "Icelandic" },
            { "ig", "Igbo" },
            { "id", "Indonesian" },
            { "ga", "Irish" },
            { "it", "Italian" },
            { "ja", "Japanese" },
            { "jw", "Javanese" },
            { "kn", "Kannada" },
            { "kk", "Kazakh" },
            { "km", "Khmer" },
            { "ko", "Korean" },
            { "ku", "Kurdish (Kurmanji)" },
            { "ky", "Kyrgyz" },
            { "lo", "Lao" },
            { "la", "Latin" },
            { "lv", "Latvian" },
            { "lt", "Lithuanian" },
            { "lb", "Luxembourgish" },
            { "mk", "Macedonian" },
            { "mg", "Malagasy" },
            { "ms", "Malay" },
            { "ml", "Malayalam" },
            { "mt", "Maltese" },
            { "mi", "Maori" },
            { "mr", "Marathi" },
            { "mn", "Mongolian" },
            { "my", "Myanmar (Burmese)" },
            { "ne", "Nepali" },
            { "no", "Norwegian" },
            { "or", "Odia" },
            { "ps", "Pashto" },
            { "fa", "Persian" },
            { "pl", "Polish" },
            { "pt", "Portuguese" },
            { "pa", "Punjabi" },
            { "ro", "Romanian" },
            { "ru", "Russian" },
            { "rw", "Kinyarwanda" },
            { "sm", "Samoan" },
            { "gd", "Scots Gaelic" },
            { "sr", "Serbian" },
            { "st", "Sesotho" },
            { "sn", "Shona" },
            { "sd", "Sindhi" },
            { "si", "Sinhala" },
            { "sk", "Slovak" },
            { "sl", "Slovenian" },
            { "so", "Somali" },
            { "es", "Spanish" },
            { "su", "Sundanese" },
            { "sw", "Swahili" },
            { "sv", "Swedish" },
            { "tg", "Tajik" },
            { "ta", "Tamil" },
            { "te", "Telugu" },
            { "tt", "Tatar" },
            { "th", "Thai" },
            { "tr", "Turkish" },
            { "tk", "Turkmen" },
            { "uk", "Ukrainian" },
            { "ur", "Urdu" },
            { "ug", "Uyghur" },
            { "uz", "Uzbek" },
            { "vi", "Vietnamese" },
            { "cy", "Welsh" },
            { "xh", "Xhosa" },
            { "yi", "Yiddish" },
            { "yo", "Yoruba" },
            { "zu", "Zulu" }
        };
    }
}
