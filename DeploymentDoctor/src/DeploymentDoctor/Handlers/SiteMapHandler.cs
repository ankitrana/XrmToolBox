using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DeploymentDoctor
{
    /// <summary>Site maps: the left navigation (areas, groups, sub areas) of a model-driven app.</summary>
    internal class SiteMapHandler : ComponentHandler
    {
        public const string Everything = "all";

        private static readonly Regex WebResourceRx = new Regex(@"\$webresource:([^\s&?""']+)|/WebResources/([^\s?""']+)", RegexOptions.IgnoreCase);

        public override ComponentKind Kind { get { return ComponentKind.SiteMap; } }
        public override string Label { get { return "Site map"; } }
        public override string Table { get { return "sitemap"; } }
        public override string Category { get { return "Screens and navigation"; } }
        public override int ComponentType { get { return 62; } }
        public override string LayerName { get { return "SiteMap"; } }
        public override string[] ContentAttributes { get { return new[] { "sitemapxml" }; } }
        public override string[] OtherAttributes { get { return new[] { "sitemapname" }; } }
        public override bool HasGroups { get { return false; } }

        public override string PublishScope(ComponentItem item) { return "all customizations"; }
        public override string PublishWarning(ComponentItem item)
        {
            return " A site map can only be published together with everything else, so ALL unpublished changes in Dev are published (PublishAllXml).";
        }
        public override string PublishAdvice(ComponentItem item)
        {
            return "This publishes every unpublished customization in Dev, not only this site map. Other developers' unfinished work goes live in Dev too. Check with your team first.";
        }

        public override void Publish(IOrganizationService svc, ComponentItem item)
        {
            svc.Execute(new PublishAllXmlRequest());
        }

        public override List<ComponentItem> LoadAll(IOrganizationService svc)
        {
            var apps = AppNames(svc);
            var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("sitemapname") };
            return All(svc, q).Select(e =>
            {
                string app;
                var name = e.GetAttributeValue<string>("sitemapname");
                if (apps.TryGetValue(e.Id, out app)) name = string.IsNullOrEmpty(name) ? "Site map of " + app : name + " (" + app + ")";
                else if (string.IsNullOrEmpty(name)) name = "Site map " + e.Id.ToString("D").Substring(0, 8);
                return new ComponentItem { Kind = Kind, Id = e.Id, Name = name, Group = Everything, SubType = "Site map", IsActive = true };
            }).OrderBy(i => i.Name).ToList();
        }

        /// <summary>sitemap id -> names of the apps that use it.</summary>
        private static Dictionary<Guid, string> AppNames(IOrganizationService svc)
        {
            var d = new Dictionary<Guid, string>();
            try
            {
                var q = new QueryExpression("appmodulecomponent") { ColumnSet = new ColumnSet("objectid") };
                q.Criteria.AddCondition("componenttype", ConditionOperator.Equal, 62);
                var app = q.AddLink("appmodule", "appmoduleidunique", "appmoduleidunique");
                app.EntityAlias = "app";
                app.Columns = new ColumnSet("name");
                foreach (var g in All(svc, q).GroupBy(e => e.GetAttributeValue<Guid>("objectid")))
                    d[g.Key] = string.Join(", ", g.Select(e => { var av = e.GetAttributeValue<AliasedValue>("app.name"); return av == null ? "?" : (string)av.Value; }).Distinct());
            }
            catch (Exception) { /* names are a convenience */ }
            return d;
        }

        // ------------------------------------------------------------ compare

        public override List<string> Compare(Snapshot left, Snapshot right, string leftName, string rightName)
        {
            return FormXmlDiff.CompareMaps(Describe(left.Get("sitemapxml")), Describe(right.Get("sitemapxml")), leftName, rightName);
        }

        public override string Pretty(Snapshot s) { return FormXmlDiff.Pretty(s.Get("sitemapxml")); }

        public override bool LayerContentUsable(Snapshot layer)
        {
            var xml = layer.Get("sitemapxml");
            return !string.IsNullOrEmpty(xml) && xml.Contains("<SiteMap");
        }

        public override string WhenIdentical
        {
            get { return "Ask users to refresh the app (Ctrl+F5). The app also needs to be published and its navigation may be hidden by security roles (sub areas with a Privilege) or by the app's own settings."; }
        }

        private static string TitleOf(XElement e)
        {
            var titles = e.Elements("Titles").Elements("Title").ToList();
            var t = titles.FirstOrDefault(x => (string)x.Attribute("LCID") == "1033") ?? titles.FirstOrDefault();
            return t == null ? null : (string)t.Attribute("Title");
        }

        /// <summary>Areas, groups and sub areas with where they open, their icon, privilege and order.</summary>
        internal static Dictionary<string, string> Describe(string xml)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var root = Parse(xml);
            if (root == null) return d;

            foreach (var area in root.Elements("Area"))
            {
                var areaId = (string)area.Attribute("Id") ?? "?";
                FormXmlDiff.Add(d, "Area '" + areaId + "'", Join("title", TitleOf(area), "icon", (string)area.Attribute("Icon"), "show groups", (string)area.Attribute("ShowGroups")));
                Order(d, "areas", root.Elements("Area"), "the site map");

                foreach (var group in area.Elements("Group"))
                {
                    var groupId = (string)group.Attribute("Id") ?? "?";
                    FormXmlDiff.Add(d, "Group '" + groupId + "'", Join("title", TitleOf(group), "in area", areaId, "icon", (string)group.Attribute("Icon")));
                    Order(d, "groups in area '" + areaId + "'", area.Elements("Group"), "area " + areaId);

                    foreach (var sub in group.Elements("SubArea"))
                    {
                        var subId = (string)sub.Attribute("Id") ?? "?";
                        var privileges = sub.Elements("Privilege").Select(p => (string)p.Attribute("Entity") + ":" + (string)p.Attribute("Privilege")).ToList();
                        FormXmlDiff.Add(d, "Sub area '" + subId + "'", Join(
                            "title", TitleOf(sub),
                            "table", (string)sub.Attribute("Entity"),
                            "url", (string)sub.Attribute("Url"),
                            "dashboard", (string)sub.Attribute("DefaultDashboard"),
                            "icon", (string)sub.Attribute("Icon"),
                            "needs privilege", privileges.Count == 0 ? null : string.Join("/", privileges),
                            "in group", groupId));
                    }
                    Order(d, "sub areas in group '" + groupId + "'", group.Elements("SubArea"), "group " + groupId);
                }
            }
            return d;
        }

        private static void Order(Dictionary<string, string> d, string what, IEnumerable<XElement> items, string where)
        {
            var ids = items.Select(x => (string)x.Attribute("Id") ?? "?").ToList();
            if (ids.Count > 1 && !d.ContainsKey("Order of " + what))
                d["Order of " + what] = string.Join(", ", ids);
        }

        private static string Join(params string[] pairs)
        {
            var parts = new List<string>();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
                if (!string.IsNullOrEmpty(pairs[i + 1])) parts.Add(pairs[i] + "=" + pairs[i + 1]);
            return parts.Count == 0 ? "-" : string.Join(", ", parts);
        }

        private static XElement Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            try { return XElement.Parse(xml); }
            catch (Exception) { return null; }
        }

        // -------------------------------------------------------------- checks

        public override void TypeChecks(CheckContext c)
        {
            DoctorService.Try(c.Findings, "Site map health", () => RunHealth(c));

            DoctorService.Try(c.Findings, "Site map in apps", () =>
            {
                var q = new QueryExpression("appmodulecomponent") { ColumnSet = new ColumnSet("objectid") };
                q.Criteria.AddCondition("componenttype", ConditionOperator.Equal, ComponentType);
                q.Criteria.AddCondition("objectid", ConditionOperator.Equal, c.Item.Id);
                var app = q.AddLink("appmodule", "appmoduleidunique", "appmoduleidunique");
                app.EntityAlias = "app";
                app.Columns = new ColumnSet("name");
                var names = c.Target.RetrieveMultiple(q).Entities
                    .Select(e => { var av = e.GetAttributeValue<AliasedValue>("app.name"); return av == null ? "?" : (string)av.Value; }).Distinct().ToList();

                if (names.Count == 0)
                    c.Findings.Add(new Finding(Severity.Warning, "Site map in apps",
                        "No model-driven app in Target uses this site map, so users never see its navigation.",
                        "Check in Dev that the app that owns this site map is in the solution and is deployed with it."));
                else
                    c.Findings.Add(new Finding(Severity.Ok, "Site map in apps", "Used by: " + string.Join(", ", names) + "."));
            });

            c.Findings.Add(new Finding(Severity.Info, "Security roles",
                "Sub areas that have a Privilege, or that open a table, are hidden from users whose security role has no read access to that table. A missing menu item is often a role, not a deployment problem."));
        }

        public override void OnMissingInTarget(CheckContext c)
        {
            if (c.DevSnap != null) DoctorService.Try(c.Findings, "Site map health", () => RunHealth(c));
        }

        private static void RunHealth(CheckContext c)
        {
            var results = new List<FormHealthResult>();
            if (c.TargetSnap != null) results.Add(Check(new DataverseLookups(c.Target), "Target", c.TargetSnap.Get("sitemapxml")));
            if (c.Dev != null && c.DevSnap != null) results.Add(Check(new DataverseLookups(c.Dev), "Dev", c.DevSnap.Get("sitemapxml")));
            if (results.Count == 0) return;
            FormHealth.PutFirst(c, results, Build(results));
        }

        public static List<Finding> Build(List<FormHealthResult> results)
        {
            return FormHealth.BuildFor(results, "Site map", "site map", Repair, FixFor);
        }

        private const string Repair =
            "Repair it in Dev, never in Target. Export the solution first as a backup. Then restore a good copy of the site map (for example import it from another environment's solution), " +
            "or fix the XML in customizations.xml of an unmanaged export (give duplicate ids unique values) and import that into Dev, or rebuild the navigation in the app designer.";

        private static string FixFor(string category, string env, bool damage)
        {
            if (damage) return Repair;
            switch (category)
            {
                case "Missing tables":
                    return env == "Target"
                        ? "Add the table to the solution in Dev and deploy again, or remove the sub area from the site map in Dev."
                        : "Remove the sub area from the site map in Dev, or restore the table.";
                case "Missing web resources":
                    return "Add the web resource to the solution in Dev and deploy again, or change the sub area to something that exists.";
                case "Sub areas that open nothing":
                    return "Give the sub area a table, a URL or a dashboard in the app designer in Dev, or remove it.";
                default:
                    return null;
            }
        }

        public static FormHealthResult Check(IComponentLookups lookups, string env, string xml)
        {
            var r = new FormHealthResult { Env = env };
            if (string.IsNullOrWhiteSpace(xml))
            {
                r.Issues.Add(FormHealth.DamageIssue("Site map XML", "The site map XML is empty."));
                r.Tests.Add(new HealthTest { Name = "Readable XML", Detail = "the site map XML is empty", Status = TestStatus.Problem });
                return r;
            }
            XElement root;
            try { root = XElement.Parse(xml); }
            catch (Exception ex)
            {
                r.Issues.Add(FormHealth.DamageIssue("Site map XML", "The site map XML can't be read as XML: " + ex.Message));
                r.Tests.Add(new HealthTest { Name = "Readable XML", Detail = "the site map XML can't be read as XML", Status = TestStatus.Problem });
                return r;
            }
            if (root.Name.LocalName != "SiteMap")
            {
                r.Issues.Add(FormHealth.DamageIssue("Site map XML", "The root element is <" + root.Name.LocalName + "> instead of <SiteMap>."));
                r.Tests.Add(new HealthTest { Name = "Readable XML", Detail = "the root element is <" + root.Name.LocalName + "> instead of <SiteMap>", Status = TestStatus.Problem });
            }
            else r.Tests.Add(new HealthTest { Name = "Readable XML", Detail = "valid XML with <SiteMap> as the root element", Status = TestStatus.Passed });

            int issues = r.Issues.Count;

            // Every Area, Group and SubArea needs its own Id: navigation and scripts address them by it
            var dup = root.Descendants().Where(e => e.Name.LocalName == "Area" || e.Name.LocalName == "Group" || e.Name.LocalName == "SubArea")
                          .Select(e => new { Kind = e.Name.LocalName, Id = (string)e.Attribute("Id") })
                          .Where(x => x.Id != null).GroupBy(x => x.Kind + " '" + x.Id + "'").Where(g => g.Count() > 1).Select(g => g.Key + " (" + g.Count() + "x)").ToList();
            if (dup.Count > 0)
                r.Issues.Add(FormHealth.DamageIssue("Duplicate ids", "These ids are used more than once: " + FormHealth.Capped(dup) + "."));

            var noId = root.Descendants().Count(e => (e.Name.LocalName == "Area" || e.Name.LocalName == "Group" || e.Name.LocalName == "SubArea") && e.Attribute("Id") == null);
            if (noId > 0)
                r.Issues.Add(FormHealth.DamageIssue("Missing ids", noId + " area, group or sub area element(s) have no Id."));
            FormHealth.Record(r, "Unique ids", root.Elements("Area").Count() + " area(s), " + root.Descendants("Group").Count() + " group(s) and " + root.Descendants("SubArea").Count() + " sub area(s) each have their own id", issues, 0);

            var subs = root.Descendants("SubArea").ToList();
            issues = r.Issues.Count;
            var empty = subs.Where(s => s.Attribute("Entity") == null && s.Attribute("Url") == null && s.Attribute("DefaultDashboard") == null)
                            .Select(s => (string)s.Attribute("Id") ?? "?").ToList();
            if (empty.Count > 0)
                r.Issues.Add(new HealthIssue { Severity = Severity.Warning, Category = "Sub areas that open nothing", Text = "These sub areas have no table, URL or dashboard: " + FormHealth.Capped(empty) + "." });
            FormHealth.Record(r, "Sub areas open something", subs.Count + " sub area(s) each open a table, URL or dashboard", issues, 0);

            var tableNames = subs.Select(s => (string)s.Attribute("Entity")).Where(n => !string.IsNullOrEmpty(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            FormHealth.Probe(r, "tables", "Tables", tableNames.Count, tableNames.Count + " table(s) opened by sub areas exist in " + env, "no sub area opens a table", () =>
            {
                var found = lookups.ExistingTables(tableNames);
                var missing = tableNames.Where(n => !found.Contains(n)).ToList();
                if (missing.Count > 0)
                    FormHealth.BrokenIssue(r, "Missing tables", "Sub areas open tables that don't exist in " + env + ": " + FormHealth.Capped(missing) + ".");
            });

            var resNames = root.Descendants().SelectMany(e => new[] { (string)e.Attribute("Url"), (string)e.Attribute("Icon") })
                               .Where(v => !string.IsNullOrEmpty(v)).Select(v => WebResourceRx.Match(v))
                               .Where(m => m.Success).Select(m => System.Net.WebUtility.UrlDecode(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value))
                               .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            FormHealth.Probe(r, "web resources", "Web resources", resNames.Count, resNames.Count + " web resource(s) used as URL or icon exist in " + env, "no web resources used", () =>
            {
                var found = lookups.ExistingWebResources(resNames);
                var missing = resNames.Where(n => !found.Contains(n)).ToList();
                if (missing.Count > 0)
                    FormHealth.BrokenIssue(r, "Missing web resources", "The site map points to web resources that don't exist in " + env + ": " + FormHealth.Capped(missing) + ".");
            });            return r;
        }
    }
}
