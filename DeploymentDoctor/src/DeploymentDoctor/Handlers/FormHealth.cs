using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace DeploymentDoctor
{
    /// <summary>
    /// What the form health check needs to know about one environment. A real implementation reads Dataverse
    /// (DataverseLookups); keeping it an interface lets the logic be tested without an environment.
    /// </summary>
    public interface IFormHealthLookups
    {
        /// <summary>Logical names of the columns of a table (case-insensitive set).</summary>
        HashSet<string> AttributeNames(string table);
        HashSet<string> ExistingWebResources(IEnumerable<string> names);
        HashSet<Guid> ExistingViews(IEnumerable<Guid> ids);
        HashSet<Guid> ExistingForms(IEnumerable<Guid> ids);
        HashSet<string> ExistingCustomControls(IEnumerable<string> names);
    }

    /// <summary>What the site map and ribbon health checks need to know about one environment.</summary>
    public interface IComponentLookups
    {
        HashSet<string> ExistingTables(IEnumerable<string> names);
        HashSet<string> ExistingWebResources(IEnumerable<string> names);
    }

    internal class HealthIssue
    {
        public bool Damage;             // true = the form XML itself is damaged
        public Severity Severity;
        public string Category;
        public string Text;
    }

    internal enum TestStatus { Passed, Problem, Warning, Skipped, NotUsed }

    /// <summary>One test the health check ran, so the user can see exactly what was looked at.</summary>
    internal class HealthTest
    {
        public string Name;
        public string Detail;
        public TestStatus Status;
    }

    internal class FormHealthResult
    {
        public string Env;
        public List<HealthTest> Tests = new List<HealthTest>();
        public List<HealthIssue> Issues = new List<HealthIssue>();
        public List<string> NotChecked = new List<string>();     // checks that could not run (e.g. no permission)
        public bool Damaged { get { return Issues.Any(i => i.Damage); } }
        public bool HasBroken { get { return Issues.Any(i => !i.Damage && i.Severity == Severity.Problem); } }
        public bool HasWarnings { get { return Issues.Any(i => !i.Damage && i.Severity == Severity.Warning); } }
    }

    internal class FormReferences
    {
        public List<string> Fields = new List<string>();
        public List<string> Libraries = new List<string>();
        public List<Tuple<string, string>> Handlers = new List<Tuple<string, string>>();   // library, function
        public List<Guid> ViewIds = new List<Guid>();
        public List<Guid> QuickFormIds = new List<Guid>();
        public List<string> CustomControls = new List<string>();
    }

    /// <summary>
    /// "Is this form really corrupted?" Reads the form XML and answers in three levels:
    /// damaged XML (unreadable, wrong structure, duplicate ids), broken references (fields, scripts, views,
    /// quick view forms or controls that don't exist in that environment), or healthy.
    /// Read-only: it never changes anything.
    /// </summary>
    internal static class FormHealth
    {
        private static readonly Regex GuidRx = new Regex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
        private static readonly string[] PlatformControlPrefixes = { "MscrmControls.", "Mscrm", "Microsoft." };

        public const string FixRepair =
            "Repair it in Dev, never in Target. Export the solution first as a backup. Then either (1) restore a good copy of the form, e.g. import it from another environment's solution " +
            "(in an environment where someone edited it, 'Remove active customizations' returns the managed version), or (2) fix the XML in customizations.xml of an unmanaged export " +
            "(give duplicates unique ids) and import that into Dev, or (3) as a last resort rebuild the form (it gets a new form id).";

        // ------------------------------------------------------------------ run inside a diagnosis

        public static void Run(CheckContext c)
        {
            var results = new List<FormHealthResult>();
            if (c.TargetSnap != null)
                results.Add(Check(new DataverseLookups(c.Target), "Target", c.Item.Group, c.TargetSnap.Get("formxml")));
            if (c.Dev != null && c.DevSnap != null)
                results.Add(Check(new DataverseLookups(c.Dev), "Dev", c.Item.Group, c.DevSnap.Get("formxml")));
            if (results.Count == 0) return;

            PutFirst(c, results, Build(results));
        }

        // ------------------------------------------------------------------------- the checks

        public static FormHealthResult Check(IFormHealthLookups lookups, string env, string table, string formXml)
        {
            var r = new FormHealthResult { Env = env };

            if (string.IsNullOrWhiteSpace(formXml))
            {
                r.Issues.Add(Damage("Form XML", "The form XML is empty."));
                r.Tests.Add(new HealthTest { Name = "Readable XML", Detail = "the form XML is empty", Status = TestStatus.Problem });
                return r;
            }

            XElement root;
            try { root = XElement.Parse(formXml); }
            catch (Exception ex)
            {
                r.Issues.Add(Damage("Form XML", "The form XML can't be read as XML: " + ex.Message));
                r.Tests.Add(new HealthTest { Name = "Readable XML", Detail = "the form XML can't be read as XML", Status = TestStatus.Problem });
                return r;
            }

            if (root.Name.LocalName != "form")
            {
                r.Issues.Add(Damage("Form XML", "The root element is <" + root.Name.LocalName + "> instead of <form>."));
                r.Tests.Add(new HealthTest { Name = "Readable XML", Detail = "the root element is <" + root.Name.LocalName + "> instead of <form>", Status = TestStatus.Problem });
            }
            else r.Tests.Add(new HealthTest { Name = "Readable XML", Detail = "valid XML with <form> as the root element", Status = TestStatus.Passed });

            Structure(r, root);
            References(r, lookups, table, Extract(root));
            return r;
        }

        private static HealthIssue Damage(string category, string text)
        {
            return new HealthIssue { Damage = true, Severity = Severity.Problem, Category = category, Text = text };
        }

        private static string CellLabel(XElement cell)
        {
            var control = cell.Descendants("control").FirstOrDefault();
            if (control == null) return "empty";
            return "field " + ((string)control.Attribute("datafieldname") ?? (string)control.Attribute("id") ?? "?");
        }

        private static string NameOf(XElement e)
        {
            var name = (string)e.Attribute("name");
            if (!string.IsNullOrEmpty(name)) return name;
            var label = e.Element("labels") == null ? null : e.Element("labels").Elements("label").Select(l => (string)l.Attribute("description")).FirstOrDefault(s => !string.IsNullOrEmpty(s));
            return label ?? "?";
        }

        /// <summary>Duplicate ids and names: the typical result of a form copied or merged badly.</summary>
        private static void Structure(FormHealthResult r, XElement root)
        {
            var byId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            Action<XElement, string> track = (e, kind) =>
            {
                var id = (string)e.Attribute("id");
                if (string.IsNullOrEmpty(id)) return;
                List<string> list;
                if (!byId.TryGetValue(id, out list)) byId[id] = list = new List<string>();
                list.Add(kind + " '" + (kind == "cell" ? CellLabel(e) : NameOf(e)) + "'");
            };
            foreach (var e in root.Descendants("tab")) track(e, "tab");
            foreach (var e in root.Descendants("section")) track(e, "section");
            foreach (var e in root.Descendants("cell")) track(e, "cell");

            int issues = r.Issues.Count;
            foreach (var kv in byId.Where(x => x.Value.Count > 1))
                r.Issues.Add(Damage("Duplicate ids", "The id " + kv.Key + " is used by " + string.Join(" and ", kv.Value) + "."));
            Record(r, "Unique ids", root.Descendants("tab").Count() + " tab(s), " + root.Descendants("section").Count() + " section(s) and " + root.Descendants("cell").Count() + " cell(s) each have their own id", issues, 0);

            // Names: scripts address tabs and sections by name, so repeats cause wrong or missing behaviour
            issues = r.Issues.Count;
            foreach (var g in root.Descendants("tab").GroupBy(t => (string)t.Attribute("name") ?? "", StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0 && g.Count() > 1))
                r.Issues.Add(new HealthIssue { Severity = Severity.Warning, Category = "Duplicate names", Text = "The tab name '" + g.Key + "' is used " + g.Count() + " times." });
            foreach (var tab in root.Descendants("tab"))
                foreach (var g in tab.Descendants("section").GroupBy(s => (string)s.Attribute("name") ?? "", StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0 && g.Count() > 1))
                    r.Issues.Add(new HealthIssue { Severity = Severity.Warning, Category = "Duplicate names", Text = "The section name '" + g.Key + "' is used " + g.Count() + " times in tab '" + NameOf(tab) + "'." });

            foreach (var g in root.Descendants("control").GroupBy(x => (string)x.Attribute("id") ?? "", StringComparer.OrdinalIgnoreCase).Where(g => g.Key.Length > 0 && g.Count() > 1))
                r.Issues.Add(new HealthIssue { Severity = Severity.Warning, Category = "Duplicate names", Text = "The control id '" + g.Key + "' is used " + g.Count() + " times." });
            Record(r, "Unique names", "tab and section names and control ids are not repeated", issues, 0);
        }
        /// <summary>Everything the form points to: columns, script libraries, views, quick view forms, custom controls.</summary>
        public static FormReferences Extract(XElement root)
        {
            var x = new FormReferences();

            x.Fields = root.Descendants("control").Select(c => (string)c.Attribute("datafieldname"))
                .Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            x.Libraries = root.Descendants("Library").Select(l => (string)l.Attribute("name"))
                .Where(s => !string.IsNullOrEmpty(s)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            x.Handlers = root.Descendants("Handler")
                .Select(h => Tuple.Create((string)h.Attribute("libraryName") ?? "", (string)h.Attribute("functionName") ?? ""))
                .Where(t => t.Item1.Length > 0).Distinct().ToList();

            x.ViewIds = root.Descendants("ViewId").Concat(root.Descendants("ViewIds")).SelectMany(e => GuidRx.Matches(e.Value).Cast<Match>())
                .Select(m => Guid.Parse(m.Value)).Distinct().ToList();

            x.QuickFormIds = root.Descendants("QuickFormId").SelectMany(e => GuidRx.Matches(e.Value).Cast<Match>())
                .Select(m => Guid.Parse(m.Value)).Distinct().ToList();

            x.CustomControls = root.Descendants("customControl").Select(c => (string)c.Attribute("name"))
                .Where(s => !string.IsNullOrEmpty(s) && !PlatformControlPrefixes.Any(p => s.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return x;
        }

        private static void References(FormHealthResult r, IFormHealthLookups lk, string table, FormReferences x)
        {
            Probe(r, "fields", "Fields", x.Fields.Count, x.Fields.Count + " field(s) on the form exist as columns of table '" + table + "' in " + r.Env, "no fields on the form", () =>
            {
                var have = lk.AttributeNames(table);
                var missing = x.Fields.Where(f => !have.Contains(f)).ToList();
                if (missing.Count > 0)
                    Broken(r, "Missing fields", missing.Count + " field(s) on the form are not columns of table '" + table + "' in " + r.Env + ": " + Cap(missing));
            });

            Probe(r, "scripts", "Script libraries", x.Libraries.Count, x.Libraries.Count + (x.Libraries.Count == 1 ? " script library added to the form exists as a web resource in " : " script libraries added to the form exist as web resources in ") + r.Env, "no script libraries on the form", () =>
            {
                var have = lk.ExistingWebResources(x.Libraries);
                var missing = x.Libraries.Where(l => !have.Contains(l)).ToList();
                if (missing.Count > 0)
                    Broken(r, "Missing scripts", missing.Count + " script librar" + (missing.Count == 1 ? "y added to the form doesn't" : "ies added to the form don't") + " exist as a web resource in " + r.Env + ": " + Cap(missing));
            });

            // Handlers call functions from libraries; a handler whose library isn't added to the form can't run
            var added = new HashSet<string>(x.Libraries, StringComparer.OrdinalIgnoreCase);
            var orphan = x.Handlers.Where(h => !added.Contains(h.Item1)).ToList();
            if (x.Handlers.Count == 0) NotUsed(r, "Event handlers", "no event handlers on the form");
            else
            {
                int before = r.Issues.Count;
                if (orphan.Count > 0)
                    r.Issues.Add(new HealthIssue
                    {
                        Severity = Severity.Warning,
                        Category = "Handlers without library",
                        Text = orphan.Count + " event handler(s) call a library that is not added to the form: " + Cap(orphan.Select(h => h.Item2 + " (" + h.Item1 + ")").ToList())
                    });
                Record(r, "Event handlers", x.Handlers.Count + " event handler(s) each call a library that is added to the form", before, 0);
            }

            Probe(r, "views", "Subgrid views", x.ViewIds.Count, x.ViewIds.Count + " view(s) used by subgrids exist in " + r.Env, "no subgrid views on the form", () =>
            {
                var have = lk.ExistingViews(x.ViewIds);
                var missing = x.ViewIds.Where(v => !have.Contains(v)).ToList();
                if (missing.Count > 0)
                    Broken(r, "Missing views", missing.Count + " view(s) used by subgrids don't exist in " + r.Env + ": " + Cap(missing.Select(g => g.ToString("B")).ToList()));
            });

            Probe(r, "quick view forms", "Quick view forms", x.QuickFormIds.Count, x.QuickFormIds.Count + " quick view form(s) exist in " + r.Env, "no quick view forms on the form", () =>
            {
                var have = lk.ExistingForms(x.QuickFormIds);
                var missing = x.QuickFormIds.Where(f => !have.Contains(f)).ToList();
                if (missing.Count > 0)
                    Broken(r, "Missing quick view forms", missing.Count + " quick view form(s) don't exist in " + r.Env + ": " + Cap(missing.Select(g => g.ToString("B")).ToList()));
            });

            Probe(r, "custom controls", "Custom controls (PCF)", x.CustomControls.Count, x.CustomControls.Count + " custom control(s) used on the form are installed in " + r.Env, "no custom controls on the form", () =>
            {
                var have = lk.ExistingCustomControls(x.CustomControls);
                var missing = x.CustomControls.Where(n => !have.Contains(n)).ToList();
                if (missing.Count > 0)
                    Broken(r, "Missing custom controls", missing.Count + " custom control(s) (PCF) used on the form are not installed in " + r.Env + ": " + Cap(missing));
            });
        }
        private static void Broken(FormHealthResult r, string category, string text)
        {
            r.Issues.Add(new HealthIssue { Severity = Severity.Problem, Category = category, Text = text });
        }

        /// <summary>Record what a test found, from the issues it added since it started.</summary>
        internal static void Record(FormHealthResult r, string name, string detail, int issuesBefore, int skippedBefore)
        {
            var added = r.Issues.Skip(issuesBefore).ToList();
            var status = r.NotChecked.Count > skippedBefore ? TestStatus.Skipped
                       : added.Any(i => i.Severity == Severity.Problem) ? TestStatus.Problem
                       : added.Count > 0 ? TestStatus.Warning : TestStatus.Passed;
            r.Tests.Add(new HealthTest { Name = name, Detail = detail, Status = status });
        }

        internal static void NotUsed(FormHealthResult r, string name, string detail)
        {
            r.Tests.Add(new HealthTest { Name = name, Detail = detail, Status = TestStatus.NotUsed });
        }

        /// <summary>Run one reference test: nothing to check when count is 0, otherwise record the outcome.</summary>
        internal static void Probe(FormHealthResult r, string what, string name, int count, string detail, string noneText, Action action)
        {
            if (count == 0) { NotUsed(r, name, noneText); return; }
            int b = r.Issues.Count, s = r.NotChecked.Count;
            Step(r, what, action);
            Record(r, name, detail, b, s);
        }

        private static void Step(FormHealthResult r, string what, Action action)
        {
            try { action(); }
            catch (Exception ex) { r.NotChecked.Add(what + " (" + TextDiff.Shorten(ex.Message, 80) + ")"); }
        }

        private static string Cap(List<string> items, int max = 12)
        {
            var shown = string.Join(", ", items.Take(max));
            return items.Count > max ? shown + " ... and " + (items.Count - max) + " more" : shown;
        }

        // ------------------------------------------------------------------------ the findings

        /// <summary>Summary first (the answer to "is it corrupted?"), then one row per kind of problem.</summary>
        public static List<Finding> Build(List<FormHealthResult> results)
        {
            return BuildFor(results, "Form", "form", FixRepair, FixFor);
        }

        /// <summary>The same summary for any component type: title "Form", noun "form" gives "Form health" / "the form XML".</summary>
        public static List<Finding> BuildFor(List<FormHealthResult> results, string title, string noun, string repair, Func<string, string, bool, string> fixFor)
        {
            var findings = new List<Finding>();
            var check = title + " health";
            var envs = string.Join(" and ", results.Select(r => r.Env));
            var damaged = results.Where(r => r.Damaged).ToList();
            var warned = results.Any(r => r.HasWarnings);
            var skipped = results.SelectMany(r => r.NotChecked.Select(n => r.Env + ": " + n)).ToList();
            var skippedText = skipped.Count == 0 ? "" : " Not checked: " + string.Join("; ", skipped) + ".";

            if (damaged.Count > 0)
            {
                findings.Add(new Finding(Severity.Problem, check,
                    "The " + noun + " XML is DAMAGED in " + string.Join(" and ", damaged.Select(r => r.Env)) + ": it is really corrupted (" +
                    damaged.Sum(r => r.Issues.Count(i => i.Damage)) + " structure problem(s), listed below)." + skippedText,
                    repair));
            }
            else if (results.Any(r => r.HasBroken))
            {
                findings.Add(new Finding(Severity.Problem, check,
                    "The " + noun + " XML is healthy, so the " + noun + " is NOT corrupted, but it has broken references in " + string.Join(" and ", results.Where(r => r.HasBroken).Select(r => r.Env)) +
                    ": it points to things that don't exist there (listed below). That produces errors or missing parts." + skippedText,
                    "Add the missing items to the solution in Dev and deploy again (or remove them from the " + noun + " in Dev). The " + noun + " itself doesn't need repairing."));
            }
            else if (warned)
            {
                findings.Add(new Finding(Severity.Warning, check,
                    "The " + noun + " XML is healthy and everything it references exists in " + envs + ": the " + noun + " is NOT corrupted. There are a few structure warnings (listed below)." + skippedText,
                    "Fix the warnings in Dev. They don't stop the " + noun + " from working."));
            }
            else
            {
                findings.Add(new Finding(Severity.Ok, check,
                    "The " + noun + " XML is healthy and everything it references exists in " + envs + ": the " + noun + " is NOT corrupted. If the change doesn't show, look at the other findings for the real cause." + skippedText));
            }

            findings[0].Details = DetailsText(results);

            foreach (var r in results)
                foreach (var g in r.Issues.GroupBy(i => i.Category + "|" + i.Severity + "|" + i.Damage))
                {
                    var first = g.First();
                    findings.Add(new Finding(first.Severity, check + ": " + first.Category + " (" + r.Env + ")",
                        string.Join(" ", g.Select(i => i.Text)), fixFor(first.Category, r.Env, first.Damage)));
                }
            return findings;
        }

        /// <summary>One line per test and environment: what was looked at and whether it passed.</summary>
        internal static string DetailsText(List<FormHealthResult> results)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var r in results)
            {
                if (r.Tests.Count == 0) continue;
                if (sb.Length > 0) sb.Append("\r\n");
                sb.Append(r.Env).Append(":");
                foreach (var t in r.Tests)
                {
                    string mark;
                    switch (t.Status)
                    {
                        case TestStatus.Passed: mark = "\u2714"; break;
                        case TestStatus.Problem: mark = "\u2716"; break;
                        case TestStatus.Warning: mark = "\u26A0"; break;
                        default: mark = "\u2013"; break;
                    }
                    var text = t.Status == TestStatus.Skipped ? "could not be checked"
                             : t.Status == TestStatus.Problem ? "problem found, see below"
                             : t.Status == TestStatus.Warning ? "warning, see below"
                             : t.Detail;
                    sb.Append("\r\n  ").Append(mark).Append("  ").Append(t.Name).Append(": ").Append(text);
                }
            }
            return sb.ToString();
        }
        // Shared by the health checks of the other component types
        internal static HealthIssue DamageIssue(string category, string text) { return Damage(category, text); }
        internal static void BrokenIssue(FormHealthResult r, string category, string text) { Broken(r, category, text); }
        internal static void RunStep(FormHealthResult r, string what, Action action) { Step(r, what, action); }
        internal static string Capped(List<string> items) { return Cap(items); }
        internal static void PutFirst(CheckContext c, List<FormHealthResult> results, List<Finding> findings)
        {
            // Damaged or healthy answers go first: they settle the "is it corrupted?" question before anything else
            if (results.Any(r => r.Damaged) || findings[0].Severity == Severity.Ok) c.Findings.InsertRange(0, findings);
            else c.Findings.AddRange(findings);
        }
        private static string FormRepair() { return FixRepair; }

        private static string FixFor(string category, string env, bool damage)
        {
            if (damage) return FixRepair;
            var inTarget = env == "Target";
            switch (category)
            {
                case "Missing fields":
                    return inTarget
                        ? "The column isn't in Target: add it to the solution in Dev and deploy again, or remove the field from the form in Dev."
                        : "Remove the field from the form in Dev, or restore the column.";
                case "Missing scripts":
                    return inTarget
                        ? "Add the web resource to the solution in Dev and deploy again, or remove the library from the form in Dev."
                        : "Restore the web resource in Dev, or remove the library from the form.";
                case "Handlers without library":
                    return "In the form designer in Dev, add the library to the form or remove the handler.";
                case "Missing views":
                    return "Add the view to the solution in Dev, or change the subgrid to a view that exists, then deploy again.";
                case "Missing quick view forms":
                    return "Add the quick view form to the solution in Dev, or remove the quick view control, then deploy again.";
                case "Missing custom controls":
                    return "Import the solution that contains the control into " + env + " first (it must be installed before this form is imported).";
                case "Duplicate names":
                    return "Rename the repeated tabs, sections or controls in the form designer in Dev so each has its own name; scripts address them by name.";
                default:
                    return null;
            }
        }
    }

    /// <summary>Reads what the form health check needs from a Dataverse connection.</summary>
    internal class DataverseLookups : IFormHealthLookups, IComponentLookups
    {
        private readonly IOrganizationService svc;
        private HashSet<string> allTables;
        public DataverseLookups(IOrganizationService svc) { this.svc = svc; }

        public HashSet<string> ExistingTables(IEnumerable<string> names)
        {
            if (allTables == null)
            {
                var r = (RetrieveAllEntitiesResponse)svc.Execute(new RetrieveAllEntitiesRequest { EntityFilters = EntityFilters.Entity, RetrieveAsIfPublished = false });
                allTables = new HashSet<string>(r.EntityMetadata.Select(m => m.LogicalName), StringComparer.OrdinalIgnoreCase);
            }
            return new HashSet<string>(names.Where(n => allTables.Contains(n)), StringComparer.OrdinalIgnoreCase);
        }
        public HashSet<string> AttributeNames(string table)
        {
            var r = (RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
            {
                LogicalName = table,
                EntityFilters = EntityFilters.Attributes,
                RetrieveAsIfPublished = false
            });
            return new HashSet<string>(r.EntityMetadata.Attributes.Select(a => a.LogicalName), StringComparer.OrdinalIgnoreCase);
        }

        public HashSet<string> ExistingWebResources(IEnumerable<string> names) { return Names("webresource", "name", names); }
        public HashSet<string> ExistingCustomControls(IEnumerable<string> names) { return Names("customcontrol", "name", names); }
        public HashSet<Guid> ExistingViews(IEnumerable<Guid> ids) { return Ids("savedquery", "savedqueryid", ids); }
        public HashSet<Guid> ExistingForms(IEnumerable<Guid> ids) { return Ids("systemform", "formid", ids); }

        private HashSet<string> Names(string table, string attr, IEnumerable<string> values)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = values.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < list.Count; i += 100)
            {
                var q = new QueryExpression(table) { ColumnSet = new ColumnSet(attr) };
                q.Criteria.AddCondition(attr, ConditionOperator.In, list.Skip(i).Take(100).Cast<object>().ToArray());
                foreach (var e in svc.RetrieveMultiple(q).Entities) set.Add(e.GetAttributeValue<string>(attr));
            }
            return set;
        }

        private HashSet<Guid> Ids(string table, string idAttr, IEnumerable<Guid> values)
        {
            var set = new HashSet<Guid>();
            var list = values.Distinct().ToList();
            for (int i = 0; i < list.Count; i += 100)
            {
                var q = new QueryExpression(table) { ColumnSet = new ColumnSet(false) };
                q.Criteria.AddCondition(idAttr, ConditionOperator.In, list.Skip(i).Take(100).Cast<object>().ToArray());
                foreach (var e in svc.RetrieveMultiple(q).Entities) set.Add(e.Id);
            }
            return set;
        }
    }
}
