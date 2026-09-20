using System.Globalization;

namespace PhasmaStrap.UI.Elements.Settings.Search
{
    internal enum SettingsSearchEntryKind
    {
        Option,

        Group,

        Tab,

        Section,

        Action,
    }

    internal sealed class SettingsSearchEntry
    {
        public SettingsSearchEntryKind Kind { get; }

        public string Header { get; }

        public string Description { get; }

        public Type PageType { get; }

        public string PageName { get; }

        public string Tab { get; }

        public string Section { get; }

        public string Group { get; }

        public Type? NestedPageType { get; }

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
