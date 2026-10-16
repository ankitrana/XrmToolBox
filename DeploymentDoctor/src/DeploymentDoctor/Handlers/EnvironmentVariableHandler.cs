using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DeploymentDoctor
{
    /// <summary>
    /// Environment variables: the definition (type, default value) travels with the solution, the current value is set per environment.
    /// Note: the "remove unmanaged layer" fix is not offered for this type; the tool explains the manual way.
    /// </summary>
    internal class EnvironmentVariableHandler : ComponentHandler
    {
        private const int SecretType = 100000005;

        public const string ValueKey = "currentvalue";

        public override ComponentKind Kind { get { return ComponentKind.EnvironmentVariable; } }
        public override string Label { get { return "Environment variable"; } }
        public override string Table { get { return "environmentvariabledefinition"; } }
        public override string Category { get { return "Settings and connections"; } }
        public override int ComponentType { get { return 380; } }
        public override string LayerName { get { return "EnvironmentVariableDefinition"; } }
        public override string[] ContentAttributes { get { return new[] { "defaultvalue" }; } }
        public override string[] OtherAttributes { get { return new[] { "schemaname", "displayname", "type" }; } }
        public override bool HasGroups { get { return false; } }
        public override bool Publishable { get { return false; } }

        public override bool SupportsRemoveActive { get { return false; } }
        public override string RemoveActiveAdvice(ComponentItem item)
        {
            return "Someone changed this environment variable's definition directly in Target. Make the change in Dev and deploy it. " +
                   "To remove the unmanaged change, use 'See solution layers' on the variable in the maker portal and delete the Active layer. " +
                   "Deployment Doctor does not remove this layer itself yet. (Changing only the current value does not create an unmanaged layer on the definition.)";
        }

        public static string TypeLabel(int type)
        {
            switch (type)
            {
                case 100000000: return "Text";
                case 100000001: return "Number";
                case 100000002: return "Yes/No";
                case 100000003: return "JSON";
                case 100000004: return "Data source";
                case SecretType: return "Secret";
                default: return "Type " + type;
            }
        }

        public override List<ComponentItem> LoadAll(IOrganizationService svc)
        {
            var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("schemaname", "displayname", "type") };
            q.AddOrder("schemaname", OrderType.Ascending);
            return All(svc, q).Select(e =>
            {
                var type = OptionValue(e, "type");
                var schema = e.GetAttributeValue<string>("schemaname");
                var display = e.GetAttributeValue<string>("displayname");
                return new ComponentItem
                {
                    Kind = Kind,
                    Id = e.Id,
                    Name = string.IsNullOrEmpty(display) || display == schema ? schema : display + " (" + schema + ")",
                    Group = SiteMapHandler.Everything,
                    SubType = TypeLabel(type),
                    SubTypeCode = type,
                    IsActive = true
                };
            }).ToList();
        }

        /// <summary>The definition plus the current value of this environment (not for secrets).</summary>
        public override Snapshot Load(IOrganizationService svc, Guid id)
        {
            var s = base.Load(svc, id);
            if (s == null) return null;
            if (OptionValue(s.Record, "type") == SecretType) return s;
            try
            {
                var q = new QueryExpression("environmentvariablevalue") { ColumnSet = new ColumnSet("value") };
                q.Criteria.AddCondition("environmentvariabledefinitionid", ConditionOperator.Equal, id);
                var v = svc.RetrieveMultiple(q).Entities.FirstOrDefault();
                s.Content[ValueKey] = v == null ? null : v.GetAttributeValue<string>("value");
            }
            catch (Exception) { /* current value is extra information */ }
            return s;
        }

        private static bool IsSecret(Snapshot s) { return OptionValue(s.Record, "type") == SecretType; }

        internal static Dictionary<string, string> Describe(Snapshot s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (s == null || s.Record == null) return d;
            FormXmlDiff.Add(d, "Type", TypeLabel(OptionValue(s.Record, "type")));
            FormXmlDiff.Add(d, "Display name", s.Record.GetAttributeValue<string>("displayname") ?? "-");
            FormXmlDiff.Add(d, "Default value", IsSecret(s) ? "(secret, not compared)" : Show(s.Get("defaultvalue")));
            return d;
        }

        private static string Show(string v) { return string.IsNullOrEmpty(v) ? "(empty)" : TextDiff.Shorten(v, 200); }

        public override List<string> Compare(Snapshot left, Snapshot right, string leftName, string rightName)
        {
            return FormXmlDiff.CompareMaps(Describe(left), Describe(right), leftName, rightName);
        }

        public override string Pretty(Snapshot s)
        {
            if (s.Record == null) return "";
            if (IsSecret(s)) return "Type: Secret\r\n(The value is stored in a secret store and is never read by this tool.)";
            return "Type: " + TypeLabel(OptionValue(s.Record, "type")) + "\r\nDefault value: " + Show(s.Get("defaultvalue")) +
                   "\r\nCurrent value in this environment: " + Show(s.Get(ValueKey));
        }

        public override bool LayerContentUsable(Snapshot layer) { return false; }

        public override string State(IOrganizationService svc, Snapshot s)
        {
            if (IsSecret(s)) return "Secret";
            return string.IsNullOrEmpty(s.Get("defaultvalue")) && string.IsNullOrEmpty(s.Get(ValueKey)) ? "No value" : "Has value";
        }

        public override bool IsProblemState(string state) { return state == "No value"; }

        public override string WhenIdentical
        {
            get { return "Check the current value in Target: it is set per environment and is not deployed with the definition (use a deployment settings file when importing)."; }
        }

        public override void TypeChecks(CheckContext c)
        {
            var t = c.TargetSnap;
            if (IsSecret(t))
            {
                c.Findings.Add(new Finding(Severity.Info, "Current value", "This is a secret: its value is stored in a secret store and this tool never reads it. Check it in the maker portal (Solutions > Environment variables)."));
                return;
            }

            var tgtDefault = t.Get("defaultvalue");
            var tgtValue = t.Get(ValueKey);
            if (string.IsNullOrEmpty(tgtDefault) && string.IsNullOrEmpty(tgtValue))
            {
                c.Findings.Add(new Finding(Severity.Problem, "Current value",
                    "This environment variable has no default value and no current value in Target. Flows, apps and plugins that read it get nothing and usually fail.",
                    "Set the current value in Target (Solutions > Environment variables > this variable > New value), or provide it in a deployment settings file / the import wizard next time."));
            }
            else
            {
                string devText = "";
                if (c.DevSnap != null && !IsSecret(c.DevSnap))
                    devText = " Dev: " + Show(c.DevSnap.Get(ValueKey) ?? c.DevSnap.Get("defaultvalue")) + ".";
                c.Findings.Add(new Finding(Severity.Info, "Current value",
                    "Target uses " + (string.IsNullOrEmpty(tgtValue) ? "the default value: " + Show(tgtDefault) : "its own current value: " + Show(tgtValue)) + "." + devText +
                    " Different values in Dev and Target are normal."));
            }
        }
    }
}
