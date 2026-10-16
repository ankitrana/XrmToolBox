using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DeploymentDoctor
{
    internal class FormHandler : ComponentHandler
    {
        private static readonly object[] ShownTypes = { 2, 6, 7, 11, 12 };

        public override ComponentKind Kind { get { return ComponentKind.Form; } }
        public override string Label { get { return "Form"; } }
        public override string Table { get { return "systemform"; } }
        public override string Category { get { return "Screens and navigation"; } }
        public override int ComponentType { get { return 60; } }
        public override string LayerName { get { return "SystemForm"; } }
        public override string[] ContentAttributes { get { return new[] { "formxml" }; } }
        public override string[] OtherAttributes { get { return new[] { "name", "formactivationstate", "objecttypecode", "type" }; } }
        public override bool IncludedWithTable(ComponentItem item) { return true; }

        public static string TypeLabel(int type)
        {
            switch (type)
            {
                case 2: return "Main";
                case 5: return "Mobile";
                case 6: return "Quick View";
                case 7: return "Quick Create";
                case 11: return "Card";
                case 12: return "Main - Interactive";
                default: return "Type " + type;
            }
        }

        public override List<ComponentItem> LoadAll(IOrganizationService svc)
        {
            var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("name", "objecttypecode", "type", "formactivationstate") };
            q.Criteria.AddCondition("type", ConditionOperator.In, ShownTypes);
            q.AddOrder("name", OrderType.Ascending);
            return All(svc, q).Select(e =>
            {
                var type = OptionValue(e, "type");
                return new ComponentItem
                {
                    Kind = Kind,
                    Id = e.Id,
                    Name = e.GetAttributeValue<string>("name"),
                    Group = e.GetAttributeValue<string>("objecttypecode"),
                    SubType = TypeLabel(type),
                    SubTypeCode = type,
                    IsActive = OptionValue(e, "formactivationstate") != 0
                };
            }).ToList();
        }

        public override List<string> Compare(Snapshot left, Snapshot right, string leftName, string rightName)
        {
            return FormXmlDiff.Compare(left.Get("formxml"), right.Get("formxml"), leftName, rightName);
        }

        public override string Pretty(Snapshot s) { return FormXmlDiff.Pretty(s.Get("formxml")); }

        public override string State(IOrganizationService svc, Snapshot s)
        {
            return OptionValue(s.Record, "formactivationstate", 1) == 0 ? "Inactive" : "Active";
        }

        public override bool LayerContentUsable(Snapshot layer)
        {
            var xml = layer.Get("formxml");
            return !string.IsNullOrEmpty(xml) && xml.Contains("<tabs");
        }

        public override string WhenIdentical
        {
            get { return "Ask users to hard-refresh (Ctrl+F5) and check they open this form (form selector)."; }
        }

        /// <summary>Other forms of the same type and name on the table, with a different id (recreated or duplicated forms).</summary>
        private List<Entity> SameNameForms(IOrganizationService svc, ComponentItem item)
        {
            var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("name", "formactivationstate") };
            q.Criteria.AddCondition("name", ConditionOperator.Equal, item.Name);
            q.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, item.Group);
            q.Criteria.AddCondition("type", ConditionOperator.Equal, item.SubTypeCode);
            q.Criteria.AddCondition("formid", ConditionOperator.NotEqual, item.Id);
            return svc.RetrieveMultiple(q).Entities.ToList();
        }

        public override void OnMissingInTarget(CheckContext c)
        {
            DoctorService.Try(c.Findings, "Same-name forms", () =>
            {
                var same = SameNameForms(c.Target, c.Item);
                if (same.Count == 0) return;
                c.Findings.Add(new Finding(Severity.Problem, "Same name, different id",
                    "Target has " + (same.Count == 1 ? "another form" : same.Count + " other forms") + " named '" + c.Item.Name + "' on table '" + c.Item.Group + "' with a different id (" +
                    string.Join(", ", same.Select(e => e.Id.ToString("B"))) + "). The form in Dev was probably deleted and recreated, so it is a different form: " +
                    "deployments can't update the old one, they add a new one next to it.",
                    "Make the changes in the original form in Dev (the one with the same id as Target) instead of recreating it. If the form was already recreated, remove the old form from the managed solution with an upgrade so only one remains."));
            });

            if (c.DevSnap != null)
                DoctorService.Try(c.Findings, "Form health", () => FormHealth.Run(c));
        }

        public override void TypeChecks(CheckContext c)
        {
            DoctorService.Try(c.Findings, "Form health", () => FormHealth.Run(c));

            DoctorService.Try(c.Findings, "Same-name forms", () =>
            {
                var same = SameNameForms(c.Target, c.Item);
                if (same.Count > 0)
                    c.Findings.Add(new Finding(Severity.Warning, "Same-name forms",
                        "Target has " + (same.Count == 1 ? "another form" : same.Count + " other forms") + " named '" + c.Item.Name + "' of the same type on this table (" +
                        string.Join(", ", same.Select(e => e.Id.ToString("B"))) + "). Users may open the other one, and its content can differ.",
                        "Check which form your users open (form selector and the app). Rename or remove the duplicate in Dev and deploy as an upgrade."));
            });

            if (OptionValue(c.TargetSnap.Record, "formactivationstate", 1) == 0)
                c.Findings.Add(new Finding(Severity.Problem, "Form is active", "The form is INACTIVE in Target, so users can't open it.",
                    "Activate the form in Dev (Forms list > Activate) and deploy again, so Dev and Target agree."));
            else
                c.Findings.Add(new Finding(Severity.Ok, "Form is active", "The form is active in Target."));

            DoctorService.Try(c.Findings, "Form in apps", () =>
            {
                var q = new QueryExpression(Table) { ColumnSet = new ColumnSet(false) };
                q.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, c.Item.Group);
                q.Criteria.AddCondition("type", ConditionOperator.Equal, c.Item.SubTypeCode);
                CheckApps(c, c.Target.RetrieveMultiple(q).Entities.Select(e => e.Id), TypeLabel(c.Item.SubTypeCode) + " forms");
            });

            DoctorService.Try(c.Findings, "Form access", () => CheckAccess(c));
        }

        private void CheckAccess(CheckContext c)
        {
            var dc = FormXmlDiff.GetDisplayConditions(c.TargetSnap.Get("formxml"));
            if (!dc.Everyone)
            {
                var roles = RoleNames(c.Target, dc.RoleIds);
                c.Findings.Add(new Finding(dc.Fallback ? Severity.Info : Severity.Warning, "Form access",
                    "Only these security roles can use this form: " + (roles.Count > 0 ? string.Join(", ", roles) : "(roles not found in Target)") +
                    ". Users without them see another form" + (dc.Fallback ? "; this form is the fallback form." : "."),
                    "Check the testing user has one of these roles, or change form access in Dev (form designer > Settings > Security roles)."));
            }
            else
            {
                c.Findings.Add(new Finding(Severity.Ok, "Form access", "Enabled for everyone."));
            }

            if (c.Item.SubTypeCode != 2) return;

            // Which main form do users get by default? Lowest form order among forms everyone can use.
            var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("name", "formxml") };
            q.Criteria.AddCondition("objecttypecode", ConditionOperator.Equal, c.Item.Group);
            q.Criteria.AddCondition("type", ConditionOperator.Equal, 2);
            q.Criteria.AddCondition("formactivationstate", ConditionOperator.Equal, 1);
            var mains = c.Target.RetrieveMultiple(q).Entities
                .Select(e => new { e.Id, Name = e.GetAttributeValue<string>("name"), Dc = FormXmlDiff.GetDisplayConditions(e.GetAttributeValue<string>("formxml")) })
                .Where(x => x.Dc.Everyone)
                .OrderBy(x => x.Dc.Order ?? int.MaxValue)
                .ToList();

            if (mains.Count > 1 && mains[0].Id != c.Item.Id)
                c.Findings.Add(new Finding(Severity.Warning, "Form order",
                    "Several main forms are open to everyone; the default is '" + mains[0].Name + "' (form order " + (mains[0].Dc.Order?.ToString() ?? "-") +
                    "). Users only get this form if they switch to it, and the app remembers the last form each user opened.",
                    "Switch to this form in the form selector, or change the form order in Dev (Forms > Form order) and deploy."));
        }

        private static List<string> RoleNames(IOrganizationService svc, List<Guid> ids)
        {
            if (ids.Count == 0) return new List<string>();
            var q = new QueryExpression("role") { ColumnSet = new ColumnSet("name") };
            q.Criteria.FilterOperator = LogicalOperator.Or;
            q.Criteria.AddCondition("roleid", ConditionOperator.In, ids.Cast<object>().ToArray());
            q.Criteria.AddCondition("parentrootroleid", ConditionOperator.In, ids.Cast<object>().ToArray());
            return svc.RetrieveMultiple(q).Entities.Select(e => e.GetAttributeValue<string>("name")).Distinct().OrderBy(n => n).ToList();
        }
    }
}
