using System.Xml;
using System.Xml.Linq;

namespace PhasmaStrap.Integrations.Nvidia
{
    // Writes the curated NVIDIA driver settings PhasmaStrap manages (see NvidiaViewModel)
    // out to an NVIDIA Profile Inspector-compatible .nip document, either to disk or as an
    // in-memory string for the clipboard. This is intentionally write-only: PhasmaStrap
    // never needs to parse a .nip file back in, since NvidiaPage always reads its settings
    // straight from the driver via NvidiaProfileInspector.ReadValues - there was previously
    // no .nip writer at all here (Voidstrap's full read/write/apply .nip round-trip and its
    // generic row-editor were deliberately left out when NvidiaProfileInspector.cs was
    // ported, see the comment at the top of that file), only this export path was missing.
    //
    // Ported (write path only) from Voidstrap's Models/ProfileManager.cs (NvidiaProfileManager).
    public static class NvidiaProfileManager
    {
        private static readonly string[] RobloxExecutables = new[] { "RobloxPlayerBeta.exe", "RobloxStudioBeta.exe" };

        private static readonly UnicodeEncoding Utf16Bom = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);

        public static void SaveToNip(string path, IEnumerable<NvidiaSetting> settings, string? profileName = null)
        {
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            XDocument doc = BuildDocument(settings, profileName);

            using XmlWriter writer = XmlWriter.Create(path, new XmlWriterSettings
            {
                Encoding = Utf16Bom,
                Indent = true,
                OmitXmlDeclaration = false,
            });
            doc.Save(writer);
        }

        // Same document as SaveToNip, but returned as a string for clipboard use instead of
        // being written to disk (used by CopyNvidiaSettingsDialog's "NIP profile"/"Base64 NIP" formats).
        public static string BuildNipText(IEnumerable<NvidiaSetting> settings, string? profileName = null)
        {
            XDocument doc = BuildDocument(settings, profileName);

            StringBuilder sb = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(sb, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false }))
                doc.Save(writer);

            return sb.ToString();
        }

        private static XDocument BuildDocument(IEnumerable<NvidiaSetting> settings, string? profileName)
        {
            string name = string.IsNullOrWhiteSpace(profileName) ? NvidiaProfileInspector.DefaultProfileName : profileName.Trim();

            XElement settingsElement = new XElement("Settings");
            foreach (NvidiaSetting setting in settings ?? Enumerable.Empty<NvidiaSetting>())
            {
                settingsElement.Add(new XElement("ProfileSetting",
                    new XElement("SettingNameInfo", string.IsNullOrWhiteSpace(setting.Name) ? "Setting " + setting.Id : setting.Name),
                    new XElement("SettingID", setting.Id.ToString(CultureInfo.InvariantCulture)),
                    new XElement("ValueType", setting.Type == NvSettingType.Dword ? "Dword" : setting.Type.ToString()),
                    new XElement("SettingValue", setting.Value.ToString(CultureInfo.InvariantCulture))));
            }

            XElement executeables = new XElement("Executeables");
            foreach (string executable in RobloxExecutables)
                executeables.Add(new XElement("string", executable));

            return new XDocument(
                new XDeclaration("1.0", "utf-16", null),
                new XElement("ArrayOfProfile",
                    new XElement("Profile",
                        new XElement("ProfileName", name),
                        executeables,
                        settingsElement)));
        }
    }
}
