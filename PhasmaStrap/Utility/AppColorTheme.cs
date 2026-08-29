using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Xml;
using System.Xml.Linq;

// Ported (with modification) from Voidstrap's Utility/CustomTheme.cs. That class edits and
// applies an XAML colour/brush ResourceDictionary that skins Voidstrap's own settings UI - a
// completely different concept from PhasmaStrap's pre-existing "CustomTheme" feature (see
// CustomThemeException.cs / AddCustomThemeDialog.xaml.cs / Paths.CustomThemes), which is an
// XML *bootstrapper dialog* definition (what the little loading window looks like), not an
// app UI colour scheme. To avoid colliding with that established "CustomTheme" naming and
// vocabulary, this app-UI colour override engine is named AppColorTheme throughout.
//
// The schema below only lists resource keys that PhasmaStrap's own Dark.xaml/Light.xaml
// dictionaries (UI/Style/) actually define, rather than Voidstrap's key names (which belong
// to a differently-skinned base theme and mostly don't exist here).
namespace PhasmaStrap.Utility
{
    public sealed class ThemeKeyInfo
    {
        /// <summary>
        /// Identifier used to key preview/edit state for this row. Matches <see cref="BrushKey"/>
        /// when present, otherwise <see cref="ColorKey"/>.
        /// </summary>
        public string Key => BrushKey ?? ColorKey!;

        public string Label { get; init; } = "";

        public string Group { get; init; } = "";

        /// <summary>Resource key of a Color resource to write, or null if this row has none.</summary>
        public string? ColorKey { get; init; }

        /// <summary>Resource key of a SolidColorBrush resource to write, or null if this row has none.</summary>
        public string? BrushKey { get; init; }

        public string Fallback { get; init; } = "#FF202020";
    }

    public sealed class ThemeValidationResult
    {
        public bool Ok => Errors.Count == 0;

        public List<string> Errors { get; } = new();

        public List<string> Warnings { get; } = new();

        public int ErrorLine { get; set; }

        public ResourceDictionary? Dictionary { get; set; }
    }

    public static class AppColorTheme
    {
        private const string LOG_IDENT = "AppColorTheme";

        private const int MaximumXamlCharacters = 200000;

        public const long MaximumXamlFileBytes = 1048576;

        private const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        private static readonly HashSet<string> AllowedElements = new(StringComparer.Ordinal)
        {
            "ResourceDictionary",
            "Color",
            "SolidColorBrush"
        };

        private static readonly IReadOnlyDictionary<string, HashSet<string>> AllowedAttributes = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        {
            ["ResourceDictionary"] = new HashSet<string>(StringComparer.Ordinal),
            ["Color"] = new HashSet<string>(StringComparer.Ordinal) { "Key" },
            ["SolidColorBrush"] = new HashSet<string>(StringComparer.Ordinal) { "Key", "Color", "Opacity" },
        };

        // Only resource keys that PhasmaStrap's own UI/Style/Dark.xaml and Light.xaml define.
        public static IReadOnlyList<ThemeKeyInfo> Schema { get; } = new List<ThemeKeyInfo>
        {
            new() { Label = "App background", Group = "Window", ColorKey = "ApplicationBackgroundColor", BrushKey = "ApplicationBackgroundBrush", Fallback = "#FF0E0E12" },
            new() { Label = "Base surface", Group = "Surfaces", ColorKey = "SolidBackgroundFillColorBase", BrushKey = "SolidBackgroundFillColorBaseBrush", Fallback = "#FF16161B" },
            new() { Label = "Base surface (alt)", Group = "Surfaces", ColorKey = "SolidBackgroundFillColorBaseAlt", BrushKey = "SolidBackgroundFillColorBaseAltBrush", Fallback = "#FF0A0A0D" },
            new() { Label = "Secondary surface", Group = "Surfaces", ColorKey = "SolidBackgroundFillColorSecondary", BrushKey = "SolidBackgroundFillColorSecondaryBrush", Fallback = "#FF121216" },
            new() { Label = "Tertiary surface", Group = "Surfaces", ColorKey = "SolidBackgroundFillColorTertiary", BrushKey = "SolidBackgroundFillColorTertiaryBrush", Fallback = "#FF1D1D23" },
            new() { Label = "Quaternary surface", Group = "Surfaces", ColorKey = "SolidBackgroundFillColorQuarternary", BrushKey = "SolidBackgroundFillColorQuarternaryBrush", Fallback = "#FF25252C" },
            new() { Label = "Card background", Group = "Cards", ColorKey = "CardBackgroundFillColorDefault", BrushKey = "CardBackgroundFillColorDefaultBrush", Fallback = "#FF16161B" },
            new() { Label = "Card background (secondary)", Group = "Cards", ColorKey = "CardBackgroundFillColorSecondary", BrushKey = "CardBackgroundFillColorSecondaryBrush", Fallback = "#FF1D1D23" },
            new() { Label = "Card border", Group = "Cards", ColorKey = "CardStrokeColorDefaultSolid", BrushKey = "CardStrokeColorDefaultSolidBrush", Fallback = "#FF2A2A30" },
            new() { Label = "Control fill", Group = "Controls", ColorKey = "ControlSolidFillColorDefault", BrushKey = "ControlSolidFillColorDefaultBrush", Fallback = "#FF1D1D23" },
            new() { Label = "Editor background", Group = "Code editor", BrushKey = "NewTextEditorBackground", Fallback = "#FF16161B" },
            new() { Label = "Editor text", Group = "Code editor", BrushKey = "NewTextEditorForeground", Fallback = "#FFF2F2F5" },
            new() { Label = "Editor link / accent", Group = "Code editor", BrushKey = "NewTextEditorLink", Fallback = "#FFF4554B" },
        };

        public static ThemeValidationResult Validate(string xaml)
        {
            ThemeValidationResult result = new();

            if (string.IsNullOrWhiteSpace(xaml))
            {
                result.Errors.Add("The theme is empty.");
                return result;
            }
            if (xaml.Length > MaximumXamlCharacters)
            {
                result.Errors.Add("The theme is too large.");
                return result;
            }

            XDocument doc;
            try
            {
                XmlReaderSettings settings = new()
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = MaximumXamlCharacters,
                    MaxCharactersFromEntities = 0
                };
                using StringReader source = new(xaml);
                using XmlReader reader = XmlReader.Create(source, settings);
                doc = XDocument.Load(reader, LoadOptions.SetLineInfo);
            }
            catch (Exception ex)
            {
                result.Errors.Add("This is not valid XML: " + ex.Message);
                return result;
            }

            if (doc.Root == null || doc.Root.Name.LocalName != "ResourceDictionary" || doc.Root.Name.NamespaceName != PresentationNamespace)
            {
                result.Errors.Add("The outer tag must be a ResourceDictionary.");
                return result;
            }

            foreach (XElement element in doc.Root.DescendantsAndSelf())
            {
                string name = element.Name.LocalName;
                if (!AllowedElements.Contains(name) || element.Name.NamespaceName != PresentationNamespace)
                {
                    result.Errors.Add("The tag <" + name + "> is not allowed in a theme. Themes may only contain colours and brushes.");
                    if (result.Errors.Count > 6)
                        return result;
                    continue;
                }
                if (!AllowedAttributes.TryGetValue(name, out HashSet<string>? allowed))
                {
                    result.Errors.Add("The tag <" + name + "> is not allowed in a theme.");
                    return result;
                }
                foreach (XAttribute attribute in element.Attributes())
                {
                    if (attribute.IsNamespaceDeclaration)
                    {
                        if (attribute.Value != PresentationNamespace && attribute.Value != XamlNamespace)
                        {
                            result.Errors.Add("The theme contains an unsupported XML namespace.");
                            return result;
                        }
                        continue;
                    }
                    bool isKey = attribute.Name.LocalName == "Key" && attribute.Name.NamespaceName == XamlNamespace;
                    bool isPresentationAttribute = string.IsNullOrEmpty(attribute.Name.NamespaceName) || attribute.Name.NamespaceName == PresentationNamespace;
                    bool attributeAllowed = isKey ? allowed.Contains("Key") : isPresentationAttribute && allowed.Contains(attribute.Name.LocalName);
                    if (!attributeAllowed || attribute.Value.Contains('{'))
                    {
                        result.Errors.Add("The attribute " + attribute.Name.LocalName + " is not allowed on <" + name + ">.");
                        return result;
                    }
                }
            }

            if (result.Errors.Count > 0)
                return result;

            ResourceDictionary parsed;
            try
            {
                using MemoryStream stream = new(Encoding.UTF8.GetBytes(xaml));
                parsed = XamlReader.Load(stream) as ResourceDictionary
                    ?? throw new InvalidOperationException("The outer tag must be a ResourceDictionary.");
            }
            catch (XamlParseException ex)
            {
                result.ErrorLine = ex.LineNumber;
                result.Errors.Add(ex.Message);
                return result;
            }
            catch (Exception ex)
            {
                result.Errors.Add(ex.Message);
                return result;
            }

            foreach (ThemeKeyInfo info in Schema)
            {
                if (info.ColorKey != null && parsed.Contains(info.ColorKey) && parsed[info.ColorKey] is not Color)
                    result.Errors.Add(info.Label + " (" + info.ColorKey + ") must be a colour, for example #FF202020.");

                if (info.BrushKey != null && parsed.Contains(info.BrushKey) && parsed[info.BrushKey] is not Brush)
                    result.Errors.Add(info.Label + " (" + info.BrushKey + ") must be a brush, for example a SolidColorBrush.");

                if ((info.ColorKey == null || !parsed.Contains(info.ColorKey)) && (info.BrushKey == null || !parsed.Contains(info.BrushKey)))
                    result.Warnings.Add(info.Label + " is not set, the built in colour will be used.");
            }

            if (result.Ok)
                result.Dictionary = parsed;
            return result;
        }

        /// <summary>
        /// Fills in any schema keys missing from the user dictionary with their fallback colour,
        /// so the returned dictionary can be merged on top of the active base theme wholesale.
        /// </summary>
        public static ResourceDictionary Merge(ResourceDictionary? user)
        {
            ResourceDictionary merged = new();

            if (user != null)
            {
                foreach (object key in user.Keys)
                {
                    try
                    {
                        merged[key] = user[key];
                    }
                    catch (Exception ex)
                    {
                        App.Logger?.WriteLine(LOG_IDENT, "Skipped theme key " + key + ": " + ex.Message);
                    }
                }
            }

            foreach (ThemeKeyInfo info in Schema)
            {
                if (!TryParseColor(info.Fallback, out Color fallback))
                    continue;

                if (info.ColorKey != null && !merged.Contains(info.ColorKey))
                    merged[info.ColorKey] = fallback;

                if (info.BrushKey != null && !merged.Contains(info.BrushKey))
                    merged[info.BrushKey] = new SolidColorBrush(fallback);
            }

            return merged;
        }

        /// <summary>
        /// Loads the saved app colour theme (if any and if enabled) merged over the schema
        /// fallbacks, ready to be added to Application.Current.Resources.MergedDictionaries.
        /// </summary>
        public static ResourceDictionary LoadForApp()
        {
            string path = Paths.CustomColorThemeXaml;
            try
            {
                if (File.Exists(path))
                {
                    ThemeValidationResult result = Validate(ReadFile(path));
                    if (result.Ok)
                        return Merge(result.Dictionary);
                    App.Logger?.WriteLine(LOG_IDENT, "Custom colour theme rejected, using the built in theme instead: " + string.Join(" ", result.Errors.Take(2)));
                }
            }
            catch (Exception ex)
            {
                App.Logger?.WriteLine(LOG_IDENT, "Could not read the custom colour theme: " + ex.Message);
            }
            return Merge(null);
        }

        public static string ReadFile(string path)
        {
            FileInfo file = new(path);
            if (!file.Exists)
                throw new FileNotFoundException("The theme file was not found", path);
            if (file.Length <= 0 || file.Length > MaximumXamlFileBytes)
                throw new InvalidDataException("The theme file size is invalid");
            return File.ReadAllText(path);
        }

        public static void WriteFile(string path, string xaml)
        {
            if (xaml.Length > MaximumXamlCharacters)
                throw new InvalidDataException("The theme is too large");

            string fullPath = Path.GetFullPath(path);
            string? directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidOperationException("The theme file has no parent directory");

            Directory.CreateDirectory(directory);

            string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.SequentialScan))
                using (StreamWriter writer = new(stream, new UTF8Encoding(false)))
                {
                    writer.Write(xaml);
                    writer.Flush();
                    stream.Flush(true);
                }
                File.Move(temporary, fullPath, true);
            }
            finally
            {
                try
                {
                    File.Delete(temporary);
                }
                catch
                {
                }
            }
        }

        public static string BuildXaml(IEnumerable<KeyValuePair<string, Color>> values)
        {
            Dictionary<string, Color> map = new(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Color> pair in values)
                map[pair.Key] = pair.Value;

            StringBuilder sb = new();
            sb.AppendLine("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"");
            sb.AppendLine("                    xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">");

            foreach (ThemeKeyInfo info in Schema)
            {
                string hex = map.TryGetValue(info.Key, out Color c) ? ToHex(c) : info.Fallback;

                if (info.ColorKey != null)
                    sb.AppendLine("  <Color x:Key=\"" + info.ColorKey + "\">" + hex + "</Color>");

                if (info.BrushKey != null)
                    sb.AppendLine("  <SolidColorBrush x:Key=\"" + info.BrushKey + "\" Color=\"" + hex + "\" />");
            }

            sb.Append("</ResourceDictionary>");
            return sb.ToString();
        }

        public static string ToHex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

        public static bool TryParseColor(string? text, out Color color)
        {
            color = Colors.Black;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            try
            {
                object? parsed = ColorConverter.ConvertFromString(text.Trim());
                if (parsed is Color c)
                {
                    color = c;
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        /// <summary>
        /// Reads back the effective colour for every schema row out of a resolved dictionary
        /// (preferring the brush's colour, falling back to the plain Color resource).
        /// </summary>
        public static void ReadColors(ResourceDictionary dict, Dictionary<string, Color> map)
        {
            foreach (ThemeKeyInfo info in Schema)
            {
                if (info.BrushKey != null && dict.Contains(info.BrushKey) && dict[info.BrushKey] is SolidColorBrush brush)
                {
                    map[info.Key] = brush.Color;
                    continue;
                }

                if (info.ColorKey != null && dict.Contains(info.ColorKey) && dict[info.ColorKey] is Color color)
                    map[info.Key] = color;
            }
        }
    }
}
