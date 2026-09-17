using System.Globalization;

namespace PhasmaStrap.UI.Elements.Settings.Search
{
    internal enum SettingsSearchEntryKind
    {
        /// <summary>A single option (OptionControl, standalone toggle/checkbox).</summary>
        Option,

        /// <summary>A collapsible group of options (CardExpander).</summary>
        Group,

        /// <summary>A tab on a tabbed page.</summary>
        Tab,

        /// <summary>A titled section of a page.</summary>
        Section,

        /// <summary>A one-off button that does something (open folder, take screenshot...).</summary>
        Action,
    }

    /// <summary>
    /// One thing a user can search for in the settings window. Instances come from the generated
    /// <see cref="SettingsSearchIndex"/>; the normalized fields are precomputed once so searching
    /// 500+ entries on every keystroke stays instant.
    /// </summary>
    internal sealed class SettingsSearchEntry
    {
        public SettingsSearchEntryKind Kind { get; }

        public string Header { get; }

        public string Description { get; }

        /// <summary>The sidebar page to navigate to.</summary>
        public Type PageType { get; }

        /// <summary>The sidebar label of <see cref="PageType"/>.</summary>
        public string PageName { get; }

        /// <summary>Header text of the tab the entry lives on (empty when the page has no tabs).</summary>
        public string Tab { get; }

        /// <summary>Title of the section the entry lives under (empty if none).</summary>
        public string Section { get; }

        /// <summary>Header of the CardExpander the entry lives in (empty if none).</summary>
        public string Group { get; }

        /// <summary>
        /// When the entry lives on a page that's embedded inside <see cref="PageType"/> via a Frame
        /// (Engine Settings hosts FastFlagsPage/NvidiaPage/FastFlagEditorPage that way), the embedded
        /// page's type - the navigator waits for that frame to load before looking for the control.
        /// </summary>
        public Type? NestedPageType { get; }

        /// <summary>
        /// When the entry lives on an embedded page, the tab of the HOST page that holds the
        /// frame (e.g. "Roblox channel" on the PhasmaStrap page); <see cref="Tab"/> is then the
        /// tab inside the embedded page itself.
        /// </summary>
        public string HostTab { get; }

        public string Breadcrumb { get; }

        internal string NormalizedHeader { get; }

        internal string[] HeaderWords { get; }

        internal string NormalizedDescription { get; }

        internal string NormalizedBreadcrumb { get; }

        internal string[] BreadcrumbWords { get; }

        public SettingsSearchEntry(SettingsSearchEntryKind kind, string header, string description, Type pageType, string pageName, string tab, string section, string group, Type? nestedPageType, string hostTab = "")
        {
            Kind = kind;
            Header = (header ?? "").Trim();
            Description = (description ?? "").Trim();
            PageType = pageType;
            PageName = (pageName ?? "").Trim();
            Tab = (tab ?? "").Trim();
            Section = (section ?? "").Trim();
            Group = (group ?? "").Trim();
            NestedPageType = nestedPageType;
            HostTab = (hostTab ?? "").Trim();

            var crumbs = new List<string>(5) { PageName };
            if (HostTab.Length > 0) crumbs.Add(HostTab);
            if (Tab.Length > 0) crumbs.Add(Tab);
            if (Section.Length > 0 && Section != Header) crumbs.Add(Section);
            if (Group.Length > 0 && Group != Header) crumbs.Add(Group);
            Breadcrumb = string.Join("  ›  ", crumbs);

            NormalizedHeader = Normalize(Header);
            HeaderWords = NormalizedHeader.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            NormalizedDescription = Normalize(Description);
            NormalizedBreadcrumb = Normalize(Breadcrumb);
            BreadcrumbWords = NormalizedBreadcrumb.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>Lowercase, diacritics stripped, punctuation turned into spaces, whitespace collapsed.</summary>
        public static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            string decomposed = value.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            bool lastWasSpace = true;

            foreach (char c in decomposed)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark)
                    continue;

                char lower = char.ToLowerInvariant(c);
                bool keep = char.IsLetterOrDigit(lower);

                if (keep)
                {
                    sb.Append(lower);
                    lastWasSpace = false;
                }
                else if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }

            return sb.ToString().Trim();
        }
    }
}
