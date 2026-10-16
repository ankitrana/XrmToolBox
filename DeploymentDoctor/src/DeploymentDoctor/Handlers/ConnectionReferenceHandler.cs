using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DeploymentDoctor
{
    /// <summary>
    /// Connection references: the link between a flow or app and a connection. The reference travels with the solution,
    /// the connection behind it is chosen per environment. Note: it has no fixed solution component number, so solution
    /// membership is matched by id, and layers are not verified for this type.
    /// </summary>
    internal class ConnectionReferenceHandler : ComponentHandler
    {
        public override ComponentKind Kind { get { return ComponentKind.ConnectionReference; } }
        public override string Label { get { return "Connection reference"; } }
        public override string Table { get { return "connectionreference"; } }
        public override string Category { get { return "Settings and connections"; } }
        public override int ComponentType { get { return -1; } }
        public override string LayerName { get { return "ConnectionReference"; } }
        public override string[] ContentAttributes { get { return new[] { "connectorid" }; } }
        public override string[] OtherAttributes { get { return new[] { "connectionreferencelogicalname", "connectionreferencedisplayname", "connectionid" }; } }
        public override bool HasGroups { get { return false; } }
        public override bool Publishable { get { return false; } }

        public override bool SupportsRemoveActive { get { return false; } }
        public override string RemoveActiveAdvice(ComponentItem item)
        {
            return "Someone changed this connection reference directly in Target. Make the change in Dev and deploy it. " +
                   "To remove the unmanaged change use 'See solution layers' in the maker portal and delete the Active layer. Deployment Doctor does not remove this layer itself yet.";
        }

        /// <summary>"/providers/Microsoft.PowerApps/apis/shared_office365" -> "office365".</summary>
        public static string Connector(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            var last = id.TrimEnd('/');
            last = last.Substring(last.LastIndexOf('/') + 1);
            return last.StartsWith("shared_", StringComparison.OrdinalIgnoreCase) ? last.Substring(7) : last;
        }

        public override List<ComponentItem> LoadAll(IOrganizationService svc)
        {
            var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("connectionreferencelogicalname", "connectionreferencedisplayname", "connectorid", "connectionid") };
            q.AddOrder("connectionreferencedisplayname", OrderType.Ascending);
            return All(svc, q).Select(e =>
            {
                var logical = e.GetAttributeValue<string>("connectionreferencelogicalname");
                var display = e.GetAttributeValue<string>("connectionreferencedisplayname");
                var connected = !string.IsNullOrEmpty(e.GetAttributeValue<string>("connectionid"));
                return new ComponentItem
                {
                    Kind = Kind,
                    Id = e.Id,
                    Name = string.IsNullOrEmpty(display) ? logical : display,
                    Group = SiteMapHandler.Everything,
                    SubType = Connector(e.GetAttributeValue<string>("connectorid")),
                    IsActive = connected,
                    InactiveText = "no connection"
                };
            }).ToList();
        }

        internal static Dictionary<string, string> Describe(Snapshot s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (s == null || s.Record == null) return d;
            FormXmlDiff.Add(d, "Connector", Connector(s.Get("connectorid")));
            FormXmlDiff.Add(d, "Display name", s.Record.GetAttributeValue<string>("connectionreferencedisplayname") ?? "-");
            FormXmlDiff.Add(d, "Logical name", s.Record.GetAttributeValue<string>("connectionreferencelogicalname") ?? "-");
            return d;
        }

        public override List<string> Compare(Snapshot left, Snapshot right, string leftName, string rightName)
        {
            return FormXmlDiff.CompareMaps(Describe(left), Describe(right), leftName, rightName);
        }

        public override string Pretty(Snapshot s)
        {
            if (s.Record == null) return "";
            return "Connector: " + Connector(s.Get("connectorid")) + "\r\nLogical name: " + s.Record.GetAttributeValue<string>("connectionreferencelogicalname") +
                   "\r\nConnection: " + (string.IsNullOrEmpty(s.Record.GetAttributeValue<string>("connectionid")) ? "(none)" : "set (the connection id is not shown)");
        }

        public override bool LayerContentUsable(Snapshot layer) { return false; }

        public override string State(IOrganizationService svc, Snapshot s)
        {
            return string.IsNullOrEmpty(s.Record.GetAttributeValue<string>("connectionid")) ? "No connection" : "Connected";
        }

        public override bool IsProblemState(string state) { return state == "No connection"; }

        public override string WhenIdentical
        {
            get { return "Check that the connection behind it still works (an expired sign-in turns flows off) and that every flow using it is on."; }
        }

        public override void TypeChecks(CheckContext c)
        {
            var targetConnected = State(c.Target, c.TargetSnap) == "Connected";
            var devConnected = c.DevSnap == null ? (bool?)null : State(c.Dev, c.DevSnap) == "Connected";

            if (!targetConnected)
                c.Findings.Add(new Finding(Severity.Problem, "Connection",
                    "This connection reference has no connection in Target" + (devConnected == true ? ", but it has one in Dev" : "") +
                    ". Flows that use it can't run and stay off or fail.",
                    "In Target open Solutions > Connection references (or the Default solution), pick a connection for it, then turn the flows that use it on. " +
                    "Next time provide the connection in the import wizard or in a deployment settings file."));
            else
                c.Findings.Add(new Finding(Severity.Ok, "Connection", "It has a connection in Target. (The connection itself is chosen per environment, so it is not compared with Dev.)"));
        }
    }
}
