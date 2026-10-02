using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace DeploymentDoctor
{
    /// <summary>
    /// Compares two form XMLs by what a maker would recognise (tabs, sections, fields, field order,
    /// libraries, event handlers, form access) instead of raw text, so attribute order and
    /// formatting don't show up as differences.
    /// </summary>
    internal static class FormXmlDiff
    {
        public class DisplayConditions
        {
            public bool Everyone;
            public List<Guid> RoleIds = new List<Guid>();
            public int? Order;
            public bool Fallback;
        }

        public static List<string> Compare(string leftXml, string rightXml, string leftName, string rightName)
        {
            return CompareMaps(Describe(leftXml), Describe(rightXml), leftName, rightName);
        }

        /// <summary>Compare two "item -> description" maps (used by every structured comparer).</summary>
        public static List<string> CompareMaps(Dictionary<string, string> left, Dictionary<string, string> right, string leftName, string rightName)
        {
            var result = new List<string>();

            foreach (var k in left.Keys.Except(right.Keys))
                result.Add(string.Format("Only in {0}: {1}{2}", leftName, k, Suffix(left[k])));
            foreach (var k in right.Keys.Except(left.Keys))
                result.Add(string.Format("Only in {0}: {1}{2}", rightName, k, Suffix(right[k])));
            foreach (var k in left.Keys.Intersect(right.Keys))
                if (!string.Equals(left[k], right[k], StringComparison.OrdinalIgnoreCase))
                    result.Add(string.Format("Changed: {0}\r\n      {1}: {2}\r\n      {3}: {4}", k, leftName, left[k], rightName, right[k]));

            return result.OrderBy(Rank).ThenBy(s => s).ToList();
        }

        /// <summary>True when the XML differs in ways Describe() doesn't capture (control properties etc.).</summary>
        public static bool RawDiffers(string a, string b)
        {
            return !string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        public static string Normalize(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return "";
            try { return XElement.Parse(xml, LoadOptions.None).ToString(SaveOptions.DisableFormatting); }
            catch (Exception) { return xml.Trim(); }
        }

        public static string Pretty(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return "";
            try { return XElement.Parse(xml).ToString(); }
            catch (Exception) { return xml; }
        }

        public static DisplayConditions GetDisplayConditions(string xml)
        {
            var dc = new DisplayConditions { Everyone = true };
            var root = Parse(xml);
            var node = root == null ? null : root.Descendants("DisplayConditions").FirstOrDefault();
            if (node == null) return dc;

            int order;
            if (int.TryParse((string)node.Attribute("Order"), out order)) dc.Order = order;
            dc.Fallback = string.Equals((string)node.Attribute("FallbackForm"), "true", StringComparison.OrdinalIgnoreCase);
            dc.Everyone = node.Element("Everyone") != null;
            foreach (var r in node.Elements("Role"))
            {
                Guid id;
                if (Guid.TryParse((string)r.Attribute("Id"), out id)) dc.RoleIds.Add(id);
            }
            if (!dc.Everyone && dc.RoleIds.Count == 0) dc.Everyone = true; // no conditions = everyone
            return dc;
        }

        // ---------------------------------------------------------------- internals

        private static Dictionary<string, string> Describe(string xml)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var root = Parse(xml);
            if (root == null) return d;

            foreach (var tab in root.Descendants("tab"))
            {
                var tabName = NameOf(tab);
                Add(d, "Tab '" + tabName + "'", string.Format("label '{0}', visible={1}, expanded={2}",
                    LabelOf(tab), Flag(tab, "visible", "true"), Flag(tab, "expanded", "true")));

                foreach (var section in tab.Descendants("section"))
                {
                    var secPath = tabName + "/" + NameOf(section);
                    Add(d, "Section '" + secPath + "'", string.Format("label '{0}', visible={1}, showlabel={2}",
                        LabelOf(section), Flag(section, "visible", "true"), Flag(section, "showlabel", "true")));

                    var fields = new List<string>();
                    foreach (var control in section.Descendants("control"))
                    {
                        var key = ControlKey(control);
                        fields.Add(key);
                        var cell = control.Parent;
                        Add(d, "Field '" + key + "'", string.Format("in section '{0}', label '{1}', visible={2}, disabled={3}",
                            secPath, cell == null ? "" : LabelOf(cell), cell == null ? "true" : Flag(cell, "visible", "true"),
                            Flag(control, "disabled", "false")));
                    }
                    if (fields.Count > 1)
                        Add(d, "Field order in '" + secPath + "'", string.Join(", ", fields));
                }
            }

            foreach (var part in new[] { "header", "footer" })
            {
                var el = root.Element(part);
                if (el == null) continue;
                foreach (var control in el.Descendants("control"))
                    Add(d, char.ToUpper(part[0]) + part.Substring(1) + " field '" + ControlKey(control) + "'",
                        "disabled=" + Flag(control, "disabled", "false"));
            }

            foreach (var lib in root.Descendants("Library"))
                Add(d, "Script library '" + (string)lib.Attribute("name") + "'", "present");

            var events = root.Element("events");
            if (events != null)
            {
                foreach (var ev in events.Elements("event"))
                {
                    var on = (string)ev.Attribute("attribute");
                    var evName = (string)ev.Attribute("name") + (string.IsNullOrEmpty(on) ? "" : " of '" + on + "'");
                    foreach (var h in ev.Descendants("Handler"))
                        Add(d, string.Format("Event handler {0}: {1}.{2}", evName, (string)h.Attribute("libraryName"), (string)h.Attribute("functionName")),
                            "enabled=" + Flag(h, "enabled", "true") + ", parameters='" + (string)h.Attribute("parameters") + "'");
                }
            }

            var dc = GetDisplayConditions(xml);
            Add(d, "Form access (security roles)", dc.Everyone ? "Everyone" : string.Join(", ", dc.RoleIds.Select(g => g.ToString("B").ToUpperInvariant()).OrderBy(s => s)));
            Add(d, "Form order", (dc.Order.HasValue ? dc.Order.Value.ToString() : "(none)") + ", fallback=" + dc.Fallback.ToString().ToLowerInvariant());

            return d;
        }

        private static XElement Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            try { return XElement.Parse(xml); }
            catch (Exception) { return null; }
        }

        public static void Add(Dictionary<string, string> d, string key, string value)
        {
            // Same field placed twice on a form: keep both
            var k = key; int n = 2;
            while (d.ContainsKey(k)) k = key + " #" + n++;
            d[k] = value;
        }

        private static string NameOf(XElement e)
        {
            return (string)e.Attribute("name") ?? (string)e.Attribute("id") ?? "?";
        }

        private static string ControlKey(XElement control)
        {
            return (string)control.Attribute("datafieldname") ?? (string)control.Attribute("id") ?? "?";
        }

        private static string LabelOf(XElement e)
        {
            var labels = e.Element("labels");
            if (labels == null) return "";
            var label = labels.Elements("label").FirstOrDefault(l => (string)l.Attribute("languagecode") == "1033")
                        ?? labels.Elements("label").FirstOrDefault();
            return label == null ? "" : (string)label.Attribute("description") ?? "";
        }

        private static string Flag(XElement e, string attr, string dflt)
        {
            return ((string)e.Attribute(attr) ?? dflt).ToLowerInvariant();
        }

        private static string Suffix(string sig)
        {
            return string.IsNullOrEmpty(sig) || sig == "present" ? "" : "  (" + sig + ")";
        }

        private static int Rank(string line)
        {
            // Fields first: they are what people usually report as "not showing"
            if (line.Contains("Field '") || line.Contains("Column '") || line.Contains("Step '") || line.Contains("Filter: ")) return 0;
            if (line.Contains("Field order") || line.Contains("Column order") || line.Contains("Sort order")) return 1;
            if (line.Contains("Section '") || line.Contains("Tab '")) return 2;
            return 3;
        }
    }
}
