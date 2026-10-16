using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DeploymentDoctor
{
    /// <summary>
    /// Ribbons (command bars): the buttons of a table (grid, form, subgrid) or of the whole application.
    /// Note: the "remove unmanaged layer" fix is not offered for ribbons; the tool explains the manual way.
    /// </summary>
    internal class RibbonHandler : ComponentHandler
    {
        private static readonly Regex LibraryRx = new Regex(@"\$webresource:([^\s&?""']+)", RegexOptions.IgnoreCase);

        public override ComponentKind Kind { get { return ComponentKind.Ribbon; } }
        public override string Label { get { return "Ribbon"; } }
        public override string DisplayName { get { return "Ribbon (command bar)"; } }
        public override string Table { get { return "ribboncustomization"; } }
        public override string Category { get { return "Screens and navigation"; } }
        public override int ComponentType { get { return 50; } }
        public override string LayerName { get { return "RibbonCustomization"; } }
        public override string[] ContentAttributes { get { return new[] { "ribbondiffxml" }; } }
        public override string[] OtherAttributes { get { return new[] { "entity" }; } }
        public override bool IncludedWithTable(ComponentItem item) { return item.Group != ProcessHandler.NoTable; }

        public override bool SupportsRemoveActive { get { return false; } }
        public override string RemoveActiveAdvice(ComponentItem item)
        {
            return "Command bar changes made directly in Target sit in an unmanaged layer. Make the change in Dev and deploy it. To remove the unmanaged change in Target, " +
                   "export the unmanaged ribbon from the Default solution, or use 'See solution layers' on the table in the maker portal and delete the Active layer. " +
                   "Deployment Doctor does not remove ribbon layers itself yet.";
        }

        public override string PublishScope(ComponentItem item)
        {
            return item.Group == ProcessHandler.NoTable ? "the application ribbon" : "table '" + item.Group + "'";
        }
        public override string PublishWarning(ComponentItem item)
        {
            return item.Group == ProcessHandler.NoTable ? "" : " Other unpublished changes on that table in Dev are published too.";
        }
        public override string PublishAdvice(ComponentItem item)
        {
            return item.Group == ProcessHandler.NoTable
                ? "Make sure the application ribbon in Dev is how you want it."
                : base.PublishAdvice(item);
        }

        public override void Publish(IOrganizationService svc, ComponentItem item)
        {
            if (item.Group == ProcessHandler.NoTable) PublishXml(svc, "<ribbons />");
            else base.Publish(svc, item);
        }

        public override List<ComponentItem> LoadAll(IOrganizationService svc)
        {
            var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("entity") };
            return All(svc, q).Select(e =>
            {
                var table = e.GetAttributeValue<string>("entity");
                var app = string.IsNullOrEmpty(table);
                return new ComponentItem
                {
                    Kind = Kind,
                    Id = e.Id,
                    Name = app ? "Application ribbon" : "Command bar of " + table,
                    Group = app ? ProcessHandler.NoTable : table,
                    SubType = app ? "Application ribbon" : "Table command bar",
                    IsActive = true
                };
            }).OrderBy(i => i.Name).ToList();
        }

        // ------------------------------------------------------------ compare

        public override List<string> Compare(Snapshot left, Snapshot right, string leftName, string rightName)
        {
            return FormXmlDiff.CompareMaps(Describe(left.Get("ribbondiffxml")), Describe(right.Get("ribbondiffxml")), leftName, rightName);
        }

        public override string Pretty(Snapshot s) { return FormXmlDiff.Pretty(s.Get("ribbondiffxml")); }

        public override bool LayerContentUsable(Snapshot layer)
        {
            var xml = layer.Get("ribbondiffxml");
            return !string.IsNullOrEmpty(xml) && xml.Contains("<RibbonDiffXml");
        }

        public override string WhenIdentical
        {
            get { return "Ask users to hard-refresh (Ctrl+F5). A button also depends on its enable/display rules (security role, record state, form type) and on the script it calls loading."; }
        }

        private static XElement Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            try { return XElement.Parse(xml); }
            catch (Exception) { return null; }
        }

        /// <summary>Hash of the element content, so any change inside is noticed even when it is not listed in words.</summary>
        private static string Hash(XElement e)
        {
            return "#" + TextDiff.Hash(Encoding.UTF8.GetBytes(FormXmlDiff.Normalize(e.ToString())));
        }

        private static string Labels(XElement root, string text)
        {
            if (string.IsNullOrEmpty(text) || !text.StartsWith("$LocLabels:", StringComparison.OrdinalIgnoreCase)) return text;
            var id = text.Substring("$LocLabels:".Length);
            var label = root.Descendants("LocLabel").FirstOrDefault(l => string.Equals((string)l.Attribute("Id"), id, StringComparison.OrdinalIgnoreCase));
            if (label == null) return text;
            var titles = label.Elements("Titles").Elements("Title").ToList();
            var t = titles.FirstOrDefault(x => (string)x.Attribute("languagecode") == "1033") ?? titles.FirstOrDefault();
            var title = t == null ? null : (string)t.Attribute("description");
            return string.IsNullOrEmpty(title) ? text : "\"" + title + "\"";
        }

        /// <summary>Buttons added or hidden, commands (with the scripts they call), rules.</summary>
        internal static Dictionary<string, string> Describe(string xml)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var root = Parse(xml);
            if (root == null) return d;

            foreach (var a in root.Descendants("CustomAction"))
                FormXmlDiff.Add(d, "Custom action '" + (string)a.Attribute("Id") + "'",
                    "location=" + (string)a.Attribute("Location") + ", sequence=" + ((string)a.Attribute("Sequence") ?? "-") + ", " + Hash(a));

            foreach (var a in root.Descendants("CustomAction"))
                foreach (var b in a.Descendants().Where(x => x.Attribute("Id") != null && x.Attribute("Command") != null))
                    FormXmlDiff.Add(d, "Button '" + (string)b.Attribute("Id") + "'",
                        "label=" + (Labels(root, (string)b.Attribute("LabelText")) ?? "-") + ", command=" + (string)b.Attribute("Command") +
                        ", sequence=" + ((string)b.Attribute("Sequence") ?? "-"));

            foreach (var h in root.Descendants("HideCustomAction"))
                FormXmlDiff.Add(d, "Hidden button '" + ((string)h.Attribute("HideActionId") ?? (string)h.Attribute("Id")) + "'", "location=" + (string)h.Attribute("Location"));

            foreach (var c in root.Descendants("CommandDefinition"))
            {
                var parts = new List<string>();
                foreach (var js in c.Descendants("JavaScriptFunction"))
                    parts.Add("runs " + (string)js.Attribute("FunctionName") + " from " + (string)js.Attribute("Library"));
                foreach (var u in c.Descendants("Url")) parts.Add("opens " + (string)u.Attribute("Address"));
                foreach (var r in c.Descendants("EnableRule")) parts.Add("enabled by " + (string)r.Attribute("Id"));
                foreach (var r in c.Descendants("DisplayRule")) parts.Add("shown by " + (string)r.Attribute("Id"));
                FormXmlDiff.Add(d, "Command '" + (string)c.Attribute("Id") + "'", (parts.Count == 0 ? "" : string.Join("; ", parts) + ", ") + Hash(c));
            }

            foreach (var kind in new[] { "EnableRule", "DisplayRule", "TabDisplayRule" })
                foreach (var r in root.Descendants("RuleDefinitions").Descendants(kind))
                    FormXmlDiff.Add(d, kind.Replace("Rule", " rule") + " '" + ((string)r.Attribute("Id") ?? (string)r.Attribute("TabCommand")) + "'", Hash(r));

            foreach (var l in root.Descendants("LocLabel"))
                FormXmlDiff.Add(d, "Label '" + (string)l.Attribute("Id") + "'", string.Join(" | ",
                    l.Elements("Titles").Elements("Title").Select(t => (string)t.Attribute("languagecode") + "=" + (string)t.Attribute("description"))));
            return d;
        }

        // -------------------------------------------------------------- checks

        public override void TypeChecks(CheckContext c)
        {
            DoctorService.Try(c.Findings, "Ribbon health", () => RunHealth(c));
        }

        public override void OnMissingInTarget(CheckContext c)
        {
            if (c.DevSnap != null) DoctorService.Try(c.Findings, "Ribbon health", () => RunHealth(c));
        }

        private static void RunHealth(CheckContext c)
        {
            var results = new List<FormHealthResult>();
            if (c.TargetSnap != null) results.Add(Check(new DataverseLookups(c.Target), "Target", c.TargetSnap.Get("ribbondiffxml")));
            if (c.Dev != null && c.DevSnap != null) results.Add(Check(new DataverseLookups(c.Dev), "Dev", c.DevSnap.Get("ribbondiffxml")));
            if (results.Count == 0) return;
            FormHealth.PutFirst(c, results, FormHealth.BuildFor(results, "Ribbon", "ribbon", Repair, FixFor));
        }

        private const string Repair =
            "Repair it in Dev, never in Target. Export the solution first as a backup. Then fix the RibbonDiffXml (give duplicates unique ids) in an unmanaged export and import it into Dev, " +
            "or remove the customization and redo it with a ribbon editor.";

        private static string FixFor(string category, string env, bool damage)
        {
            if (damage) return Repair;
            switch (category)
            {
                case "Missing scripts":
                    return "Add the web resource to the solution in Dev and deploy again, or point the command at a script that exists.";
                case "Undefined commands":
                    return "Define the command in CommandDefinitions, or point the button at an existing command. (Commands that start with Mscrm are built in.)";
                case "Undefined rules":
                    return "Define the rule in RuleDefinitions, or remove it from the command. (Rules that start with Mscrm are built in.)";
                default:
                    return null;
            }
        }

        public static FormHealthResult Check(IComponentLookups lookups, string env, string xml)
        {
            var r = new FormHealthResult { Env = env };
            if (string.IsNullOrWhiteSpace(xml))
            {
                r.Issues.Add(new HealthIssue { Severity = Severity.Warning, Category = "Ribbon XML", Text = "The ribbon XML is empty: there are no command bar customizations." });
                r.Tests.Add(new HealthTest { Name = "Readable XML", Detail = "the ribbon XML is empty", Status = TestStatus.Warning });
                return r;
            }
            XElement root;
            try { root = XElement.Parse(xml); }
            catch (Exception ex)
            {
                r.Issues.Add(FormHealth.DamageIssue("Ribbon XML", "The ribbon XML can't be read as XML: " + ex.Message));
                r.Tests.Add(new HealthTest { Name = "Readable XML", Detail = "the ribbon XML can't be read as XML", Status = TestStatus.Problem });
                return r;
            }
            if (root.Name.LocalName != "RibbonDiffXml")
            {
                r.Issues.Add(FormHealth.DamageIssue("Ribbon XML", "The root element is <" + root.Name.LocalName + "> instead of <RibbonDiffXml>."));
                r.Tests.Add(new HealthTest { Name = "Readable XML", Detail = "the root element is <" + root.Name.LocalName + "> instead of <RibbonDiffXml>", Status = TestStatus.Problem });
            }
            else r.Tests.Add(new HealthTest { Name = "Readable XML", Detail = "valid XML with <RibbonDiffXml> as the root element", Status = TestStatus.Passed });

            // Duplicate ids of the same kind: the platform can't tell the items apart
            int issues = r.Issues.Count;
            var dup = new List<string>();
            foreach (var kind in new[] { "CustomAction", "CommandDefinition", "EnableRule", "DisplayRule", "LocLabel" })
                // rules are defined under RuleDefinitions; the same id inside a command is only a reference to it
                dup.AddRange((kind.EndsWith("Rule") ? root.Descendants("RuleDefinitions").Descendants(kind) : root.Descendants(kind)).Select(e => (string)e.Attribute("Id")).Where(i => i != null)
                                 .GroupBy(i => i, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => kind + " '" + g.Key + "' (" + g.Count() + "x)"));
            if (dup.Count > 0)
                r.Issues.Add(FormHealth.DamageIssue("Duplicate ids", "These ids are used more than once: " + FormHealth.Capped(dup) + "."));
            FormHealth.Record(r, "Unique ids", root.Descendants("CustomAction").Count() + " custom action(s), " + root.Descendants("CommandDefinition").Count() + " command(s) and the rules each have their own id", issues, 0);

            var libraries = root.Descendants("JavaScriptFunction").Select(j => (string)j.Attribute("Library")).Where(l => !string.IsNullOrEmpty(l))
                                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var scriptNames = libraries.Select(l => LibraryRx.Match(l)).Where(m => m.Success).Select(m => System.Net.WebUtility.UrlDecode(m.Groups[1].Value))
                                       .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            FormHealth.Probe(r, "scripts", "Scripts", scriptNames.Count, scriptNames.Count + " script(s) called by commands exist as web resources in " + env, "no command calls a script", () =>
            {
                var found = lookups.ExistingWebResources(scriptNames);
                var missing = scriptNames.Where(n => !found.Contains(n)).ToList();
                if (missing.Count > 0)
                    FormHealth.BrokenIssue(r, "Missing scripts", "Commands call scripts that don't exist in " + env + ": " + FormHealth.Capped(missing) + ". The button does nothing or shows a script error.");
            });
            // Buttons and rules that point at something not defined here (platform ones start with Mscrm)
            Func<string, bool> builtIn = id => id.IndexOf("Mscrm", StringComparison.OrdinalIgnoreCase) >= 0;
            var commands = new HashSet<string>(root.Descendants("CommandDefinition").Select(x => (string)x.Attribute("Id")).Where(i => i != null), StringComparer.OrdinalIgnoreCase);
            var usedCommands = root.Descendants("CustomAction").SelectMany(a => a.Descendants()).Select(x => (string)x.Attribute("Command"))
                                   .Where(i => !string.IsNullOrEmpty(i) && !builtIn(i)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var noCommand = usedCommands.Where(i => !commands.Contains(i)).ToList();
            issues = r.Issues.Count;
            if (noCommand.Count > 0)
                r.Issues.Add(new HealthIssue { Severity = Severity.Warning, Category = "Undefined commands", Text = "Buttons use commands that this ribbon doesn't define: " + FormHealth.Capped(noCommand) + "." });
            FormHealth.Record(r, "Commands", "every button uses a command that is defined here or built in", issues, 0);
            issues = r.Issues.Count;

            var enable = new HashSet<string>(root.Descendants("RuleDefinitions").Descendants("EnableRule").Select(x => (string)x.Attribute("Id")).Where(i => i != null), StringComparer.OrdinalIgnoreCase);
            var display = new HashSet<string>(root.Descendants("RuleDefinitions").Descendants("DisplayRule").Select(x => (string)x.Attribute("Id")).Where(i => i != null), StringComparer.OrdinalIgnoreCase);
            var noRule = root.Descendants("CommandDefinition").Descendants("EnableRule").Select(x => (string)x.Attribute("Id"))
                             .Where(i => !string.IsNullOrEmpty(i) && !builtIn(i) && !enable.Contains(i))
                             .Concat(root.Descendants("CommandDefinition").Descendants("DisplayRule").Select(x => (string)x.Attribute("Id"))
                             .Where(i => !string.IsNullOrEmpty(i) && !builtIn(i) && !display.Contains(i)))
                             .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (noRule.Count > 0)
                r.Issues.Add(new HealthIssue { Severity = Severity.Warning, Category = "Undefined rules", Text = "Commands use rules that this ribbon doesn't define: " + FormHealth.Capped(noRule) + "." });
            FormHealth.Record(r, "Rules", "every enable and display rule used by a command is defined here or built in", issues, 0);
            return r;
        }
    }
}
