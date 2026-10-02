using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DeploymentDoctor
{
    /// <summary>System views (savedquery).</summary>
    internal class ViewHandler : ComponentHandler
    {
        private static readonly object[] ShownTypes = { 0, 1, 2, 4, 64 };

        public override ComponentKind Kind { get { return ComponentKind.View; } }
        public override string Label { get { return "View"; } }
        public override string Table { get { return "savedquery"; } }
        public override int ComponentType { get { return 26; } }
        public override string LayerName { get { return "SavedQuery"; } }
        public override string[] ContentAttributes { get { return new[] { "fetchxml", "layoutxml" }; } }
        public override string[] OtherAttributes { get { return new[] { "name", "returnedtypecode", "querytype", "isdefault" }; } }
        public override bool IncludedWithTable(ComponentItem item) { return true; }

        public static string TypeLabel(int type)
        {
            switch (type)
            {
                case 0: return "Public view";
                case 1: return "Advanced Find view";
                case 2: return "Associated view";
                case 4: return "Quick Find view";
                case 64: return "Lookup view";
                default: return "View type " + type;
            }
        }

        public override List<ComponentItem> LoadAll(IOrganizationService svc)
        {
            var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("name", "returnedtypecode", "querytype", "isdefault") };
            q.Criteria.AddCondition("querytype", ConditionOperator.In, ShownTypes);
            q.AddOrder("name", OrderType.Ascending);
            return All(svc, q).Select(e =>
            {
                var type = e.GetAttributeValue<int>("querytype");
                return new ComponentItem
                {
                    Kind = Kind,
                    Id = e.Id,
                    Name = e.GetAttributeValue<string>("name"),
                    Group = e.GetAttributeValue<string>("returnedtypecode"),
                    SubType = TypeLabel(type) + (e.GetAttributeValue<bool>("isdefault") ? ", default" : ""),
                    SubTypeCode = type,
                    IsActive = true
                };
            }).ToList();
        }

        public override List<string> Compare(Snapshot left, Snapshot right, string leftName, string rightName)
        {
            return FormXmlDiff.CompareMaps(Describe(left), Describe(right), leftName, rightName);
        }

        public override string Pretty(Snapshot s)
        {
            return "FetchXML:\r\n" + FormXmlDiff.Pretty(s.Get("fetchxml")) + "\r\n\r\nLayoutXML:\r\n" + FormXmlDiff.Pretty(s.Get("layoutxml"));
        }

        public override bool LayerContentUsable(Snapshot layer)
        {
            var fetch = layer.Get("fetchxml");
            return !string.IsNullOrEmpty(fetch) && fetch.Contains("<fetch");
        }

        public override string WhenIdentical
        {
            get { return "Ask users to refresh, and to check they picked this view: a personal default view or a different system default view hides it."; }
        }

        /// <summary>Columns (with width), column order, filters, sort order and joins.</summary>
        private static Dictionary<string, string> Describe(Snapshot s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var layout = Parse(s.Get("layoutxml"));
            if (layout != null)
            {
                var cells = layout.Descendants("cell").ToList();
                foreach (var cell in cells)
                    FormXmlDiff.Add(d, "Column '" + (string)cell.Attribute("name") + "'", "width=" + ((string)cell.Attribute("width") ?? "-"));
                if (cells.Count > 1)
                    FormXmlDiff.Add(d, "Column order", string.Join(", ", cells.Select(c => (string)c.Attribute("name"))));
                var grid = layout.DescendantsAndSelf("grid").FirstOrDefault();
                if (grid != null && grid.Attribute("jump") != null)
                    FormXmlDiff.Add(d, "Link column (opens the record)", (string)grid.Attribute("jump"));
            }

            var fetch = Parse(s.Get("fetchxml"));
            var entity = fetch == null ? null : fetch.Element("entity");
            if (entity != null)
            {
                Walk(d, entity, "");
                var orders = entity.Elements("order").Select(o =>
                    (string)o.Attribute("attribute") + (string.Equals((string)o.Attribute("descending"), "true", StringComparison.OrdinalIgnoreCase) ? " desc" : " asc")).ToList();
                FormXmlDiff.Add(d, "Sort order", orders.Count == 0 ? "(none)" : string.Join(", ", orders));
                var top = (string)fetch.Attribute("top") ?? (string)fetch.Attribute("count");
                if (!string.IsNullOrEmpty(top)) FormXmlDiff.Add(d, "Row limit", top);
                if (fetch.Attribute("distinct") != null) FormXmlDiff.Add(d, "Distinct", ((string)fetch.Attribute("distinct")).ToLowerInvariant());
            }
            return d;
        }

        private static void Walk(Dictionary<string, string> d, XElement node, string path)
        {
            foreach (var filter in node.Elements("filter"))
                Filters(d, filter, path, (string)filter.Attribute("type") ?? "and");

            foreach (var link in node.Elements("link-entity"))
            {
                var name = (string)link.Attribute("name");
                var alias = (string)link.Attribute("alias");
                FormXmlDiff.Add(d, "Related table '" + path + name + "'", string.Format("{0}.{1} -> {2}, {3}",
                    path.TrimEnd('/'), (string)link.Attribute("to"), (string)link.Attribute("from"), (string)link.Attribute("link-type") ?? "inner"));
                Walk(d, link, path + (alias ?? name) + "/");
            }
        }

        private static void Filters(Dictionary<string, string> d, XElement filter, string path, string groupType)
        {
            foreach (var c in filter.Elements("condition"))
            {
                var values = c.Elements("value").Select(v => v.Value).ToList();
                var value = (string)c.Attribute("value") ?? (values.Count > 0 ? "(" + string.Join(", ", values) + ")" : "");
                FormXmlDiff.Add(d, string.Format("Filter: {0}{1} {2} {3}", path, (string)c.Attribute("attribute"), (string)c.Attribute("operator"), value).TrimEnd(),
                    "in an '" + groupType + "' group");
            }
            foreach (var sub in filter.Elements("filter"))
                Filters(d, sub, path, groupType + " > " + ((string)sub.Attribute("type") ?? "and"));
        }

        private static XElement Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml)) return null;
            try { return XElement.Parse(xml); }
            catch (Exception) { return null; }
        }

        public override void TypeChecks(CheckContext c)
        {
            // State: savedquery.statecode exists in current Dataverse; read separately so an older org doesn't break Load()
            DoctorService.Try(c.Findings, "View is active", () =>
            {
                var e = c.Target.Retrieve(Table, c.Item.Id, new ColumnSet("statecode"));
                if (OptionValue(e, "statecode", 0) != 0)
                    c.Findings.Add(new Finding(Severity.Problem, "View is active", "The view is INACTIVE in Target, so users can't pick it.",
                        "Activate the view in Dev and deploy again."));
                else
                    c.Findings.Add(new Finding(Severity.Ok, "View is active", "The view is active in Target."));
            });

            if (c.Item.SubTypeCode != 0) return;

            DoctorService.Try(c.Findings, "Default view", () =>
            {
                var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("name") };
                q.Criteria.AddCondition("returnedtypecode", ConditionOperator.Equal, c.Item.Group);
                q.Criteria.AddCondition("querytype", ConditionOperator.Equal, 0);
                q.Criteria.AddCondition("isdefault", ConditionOperator.Equal, true);
                var targetDefault = c.Target.RetrieveMultiple(q).Entities.FirstOrDefault();
                bool devDefault = c.DevSnap != null && c.DevSnap.Record.GetAttributeValue<bool>("isdefault");
                bool isDefault = targetDefault != null && targetDefault.Id == c.Item.Id;

                if (devDefault && !isDefault)
                    c.Findings.Add(new Finding(Severity.Warning, "Default view",
                        "This is the default view in Dev, but in Target the default is '" + (targetDefault == null ? "?" : targetDefault.GetAttributeValue<string>("name")) +
                        "' (probably changed directly in Target). Users open that view first.",
                        "Check the unmanaged layer on the other view, or set the default again in Dev and deploy."));
                else if (!isDefault)
                    c.Findings.Add(new Finding(Severity.Info, "Default view",
                        "The default view for '" + c.Item.Group + "' is '" + (targetDefault == null ? "?" : targetDefault.GetAttributeValue<string>("name")) +
                        "'. Users see this view only after picking it in the view selector (or as their personal default)."));
                else
                    c.Findings.Add(new Finding(Severity.Ok, "Default view", "This is the default view in Target."));
            });

            DoctorService.Try(c.Findings, "View in apps", () =>
            {
                var q = new QueryExpression(Table) { ColumnSet = new ColumnSet(false) };
                q.Criteria.AddCondition("returnedtypecode", ConditionOperator.Equal, c.Item.Group);
                q.Criteria.AddCondition("querytype", ConditionOperator.Equal, 0);
                CheckApps(c, c.Target.RetrieveMultiple(q).Entities.Select(e => e.Id), "views");
            });
        }
    }
}
