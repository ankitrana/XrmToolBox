using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DeploymentDoctor
{
    /// <summary>Processes (workflow table): classic workflows, business rules, actions, BPFs, cloud and desktop flows.</summary>
    internal class ProcessHandler : ComponentHandler
    {
        private const int CloudFlow = 5, DesktopFlow = 6, BusinessRule = 2, ClassicWorkflow = 0;
        private static readonly object[] ShownCategories = { 0, 2, 3, 4, 5, 6 };
        public const string NoTable = "none";

        public override ComponentKind Kind { get { return ComponentKind.Process; } }
        public override string Label { get { return "Process"; } }
        public override string Table { get { return "workflow"; } }
        public override int ComponentType { get { return 29; } }
        public override string LayerName { get { return "Workflow"; } }
        public override string[] ContentAttributes { get { return new[] { "clientdata", "xaml" }; } }
        public override string[] OtherAttributes { get { return new[] { "name", "category", "statecode", "statuscode", "primaryentity" }; } }
        public override bool Publishable { get { return false; } }   // processes are activated, not published
        public override bool IncludedWithTable(ComponentItem item) { return item.SubTypeCode == BusinessRule; }

        public static string CategoryLabel(int category)
        {
            switch (category)
            {
                case 0: return "Classic workflow";
                case 1: return "Dialog";
                case 2: return "Business rule";
                case 3: return "Action";
                case 4: return "Business process flow";
                case 5: return "Cloud flow";
                case 6: return "Desktop flow";
                default: return "Process category " + category;
            }
        }

        private static bool IsFlow(int category) { return category == CloudFlow || category == DesktopFlow; }

        public override List<ComponentItem> LoadAll(IOrganizationService svc)
        {
            var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("name", "category", "statecode", "primaryentity") };
            q.Criteria.AddCondition("type", ConditionOperator.Equal, 1);   // definitions only, not activations/templates
            q.Criteria.AddCondition("category", ConditionOperator.In, ShownCategories);
            q.AddOrder("name", OrderType.Ascending);
            return All(svc, q).Select(e =>
            {
                var category = OptionValue(e, "category");
                var table = e.GetAttributeValue<string>("primaryentity");
                return new ComponentItem
                {
                    Kind = Kind,
                    Id = e.Id,
                    Name = e.GetAttributeValue<string>("name"),
                    Group = string.IsNullOrEmpty(table) ? NoTable : table,
                    SubType = CategoryLabel(category),
                    SubTypeCode = category,
                    IsActive = OptionValue(e, "statecode") == 1,
                    InactiveText = IsFlow(category) ? "off" : "draft"
                };
            }).ToList();
        }

        private static int CategoryOf(Snapshot s, Snapshot other)
        {
            var e = s.Record ?? (other == null ? null : other.Record);
            return OptionValue(e, "category", 0);
        }

        // ------------------------------------------------------------ compare

        public override List<string> Compare(Snapshot left, Snapshot right, string leftName, string rightName)
        {
            if (IsFlow(CategoryOf(left, right)))
                return FormXmlDiff.CompareMaps(DescribeFlow(left.Get("clientdata")), DescribeFlow(right.Get("clientdata")), leftName, rightName);

            return TextDiff.Compare(FormXmlDiff.Pretty(left.Get("xaml")), FormXmlDiff.Pretty(right.Get("xaml")), leftName, rightName, "process definition (XAML)");
        }

        /// <summary>Triggers, steps (with nested steps flattened) and connection references of a cloud flow.</summary>
        private static Dictionary<string, string> DescribeFlow(string clientData)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var root = Json.Parse(clientData);
            var props = Json.Obj(root, "properties");
            var def = Json.Obj(props, "definition");
            if (def == null) return d;

            var triggers = Json.Obj(def, "triggers");
            if (triggers != null)
                foreach (var t in triggers)
                    FormXmlDiff.Add(d, "Trigger '" + t.Key + "'", Signature(t.Value));

            Steps(d, Json.Obj(def, "actions"), "");

            var refs = Json.Obj(props, "connectionReferences");
            if (refs != null)
                foreach (var r in refs)
                {
                    var conn = Json.Obj(r.Value, "connection");
                    object logical = null;
                    if (conn != null) conn.TryGetValue("connectionReferenceLogicalName", out logical);
                    FormXmlDiff.Add(d, "Connection reference '" + r.Key + "'", logical as string ?? "(embedded connection)");
                }
            return d;
        }

        private static void Steps(Dictionary<string, string> d, IDictionary<string, object> actions, string parent)
        {
            if (actions == null) return;
            foreach (var a in actions)
            {
                var path = parent + a.Key;
                FormXmlDiff.Add(d, "Step '" + path + "'", Signature(a.Value));

                var node = a.Value as IDictionary<string, object>;
                if (node == null) continue;
                Steps(d, Json.Obj(node, "actions"), path + " > ");
                Steps(d, Json.Obj(Json.Obj(node, "else"), "actions"), path + " (no) > ");
                Steps(d, Json.Obj(Json.Obj(node, "default"), "actions"), path + " (default) > ");
                var cases = Json.Obj(node, "cases");
                if (cases != null)
                    foreach (var cs in cases)
                        Steps(d, Json.Obj(cs.Value, "actions"), path + " (" + cs.Key + ") > ");
            }
        }

        /// <summary>Step type, operation and a short hash of its settings (nested steps excluded).</summary>
        private static string Signature(object node)
        {
            var dict = node as IDictionary<string, object>;
            if (dict == null) return "";
            var own = dict.Where(kv => kv.Key != "actions" && kv.Key != "else" && kv.Key != "cases" && kv.Key != "default")
                          .OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value);
            object type;
            dict.TryGetValue("type", out type);
            var host = Json.Obj(Json.Obj(dict, "inputs"), "host");
            object op = null;
            if (host != null) host.TryGetValue("operationId", out op);
            var after = Json.Obj(dict, "runAfter");
            return string.Format("type={0}{1}{2}, settings #{3}",
                type, op == null ? "" : ", operation=" + op,
                after == null || after.Count == 0 ? "" : ", runs after " + string.Join("/", after.Keys),
                TextDiff.Hash(Encoding.UTF8.GetBytes(Json.Serialize(own))));
        }

        public override string Pretty(Snapshot s)
        {
            if (IsFlow(CategoryOf(s, null))) return Json.Pretty(s.Get("clientdata"));
            return FormXmlDiff.Pretty(s.Get("xaml"));
        }

        public override bool LayerContentUsable(Snapshot layer)
        {
            return !string.IsNullOrEmpty(layer.Get("clientdata")) || !string.IsNullOrEmpty(layer.Get("xaml"));
        }

        public override string WhenIdentical
        {
            get { return "Runs that started before the import keep the old version: check the run history for runs started after the import."; }
        }

        // -------------------------------------------------------------- checks

        public override void TypeChecks(CheckContext c)
        {
            int category = c.Item.SubTypeCode;
            string on = IsFlow(category) ? "on" : "activated";
            string off = IsFlow(category) ? "OFF" : "in DRAFT (not activated)";

            // On / off
            var tgtState = OptionValue(c.TargetSnap.Record, "statecode", 0);
            var devState = c.DevSnap == null ? (int?)null : OptionValue(c.DevSnap.Record, "statecode", 0);
            if (tgtState != 1)
            {
                c.Findings.Add(new Finding(devState == 0 ? Severity.Warning : Severity.Problem, "Turned on",
                    "The " + CategoryLabel(category).ToLowerInvariant() + " is " + off + " in Target" + (devState == 1 ? ", but " + on + " in Dev" : "") +
                    ". It doesn't run until it is turned on." +
                    (category == CloudFlow ? " Imports leave flows off when a connection reference has no connection." : ""),
                    category == CloudFlow
                        ? "Make sure every connection reference has a connection (see below), then turn the flow on."
                        : "Turn it on in Target. If it fails, the error tells you what is missing (e.g. a referenced component).",
                    new FixAction
                    {
                        Label = "Turn on in Target",
                        OnDev = false,
                        WhatItDoes = "Turn on (activate) '" + c.Item.Name + "' in Target (SetState: Activated). This does not create an unmanaged layer.",
                        Run = s => s.Execute(new SetStateRequest
                        {
                            EntityMoniker = new EntityReference(Table, c.Item.Id),
                            State = new OptionSetValue(1),
                            Status = new OptionSetValue(2)
                        }),
                        Undo = "Yes, turn it off again (same place in the maker portal).",
                        Advice = "It starts running in Target straight away for new triggers: it can send emails, create or update records. " +
                                 (category == CloudFlow
                                     ? "A cloud flow runs with the connections in its connection references, not as you; fix any connection reference without a connection first, or turning on fails. "
                                     : "") +
                                 "Make sure the off state wasn't on purpose (e.g. switched off during a data migration).",
                        ManualSteps = category == CloudFlow
                            ? "1. Open make.powerapps.com and switch to the Target environment.\r\n" +
                              "2. Solutions > your solution > Cloud flows > '" + c.Item.Name + "'.\r\n" +
                              "3. Check the connection references have connections, then click Turn on."
                            : "1. Open make.powerapps.com and switch to the Target environment.\r\n" +
                              "2. Solutions > your solution > Processes (or Business rules) > '" + c.Item.Name + "'.\r\n" +
                              "3. Click Activate (classic designer: Activate button at the top)."
                    }));
            }
            else
            {
                c.Findings.Add(new Finding(Severity.Ok, "Turned on", "It is " + on + " in Target." +
                    (devState == 0 ? " (It is " + (IsFlow(category) ? "off" : "draft") + " in Dev.)" : "")));
            }

            if (category == CloudFlow)
                DoctorService.Try(c.Findings, "Connection references", () => CheckConnections(c));

            DoctorService.Try(c.Findings, "Duplicate copies", () => CheckDuplicates(c));

            if (category == ClassicWorkflow || category == CloudFlow)
                c.Findings.Add(new Finding(Severity.Info, "Running instances",
                    "Runs that started before the import (waiting workflows, long-running flows) keep using the old definition. Only new runs use the deployed version."));
        }

        private void CheckConnections(CheckContext c)
        {
            var refs = Json.Obj(Json.Obj(Json.Parse(c.TargetSnap.Get("clientdata")), "properties"), "connectionReferences");
            var logicalNames = new List<string>();
            if (refs != null)
                foreach (var r in refs)
                {
                    var conn = Json.Obj(r.Value, "connection");
                    object logical;
                    if (conn != null && conn.TryGetValue("connectionReferenceLogicalName", out logical) && logical is string)
                        logicalNames.Add((string)logical);
                }
            if (logicalNames.Count == 0) return;

            var q = new QueryExpression("connectionreference") { ColumnSet = new ColumnSet("connectionreferencelogicalname", "connectionreferencedisplayname", "connectionid") };
            q.Criteria.AddCondition("connectionreferencelogicalname", ConditionOperator.In, logicalNames.Cast<object>().ToArray());
            var found = c.Target.RetrieveMultiple(q).Entities.ToDictionary(e => e.GetAttributeValue<string>("connectionreferencelogicalname"), StringComparer.OrdinalIgnoreCase);

            var missing = logicalNames.Where(n => !found.ContainsKey(n)).ToList();
            var noConnection = found.Values.Where(e => string.IsNullOrEmpty(e.GetAttributeValue<string>("connectionid")))
                                     .Select(e => e.GetAttributeValue<string>("connectionreferencedisplayname") ?? e.GetAttributeValue<string>("connectionreferencelogicalname")).ToList();

            if (missing.Count > 0)
                c.Findings.Add(new Finding(Severity.Problem, "Connection references",
                    "These connection references don't exist in Target: " + string.Join(", ", missing) + ".",
                    "Add the connection references to the solution in Dev and deploy again."));
            if (noConnection.Count > 0)
                c.Findings.Add(new Finding(Severity.Problem, "Connection references",
                    "These connection references have no connection in Target: " + string.Join(", ", noConnection) + ". The flow can't run or be turned on.",
                    "In Target open the connection reference (Solutions > Default solution > Connection references), pick a connection, then turn the flow on. " +
                    "Next time provide connections in the import wizard or a deployment settings file."));
            if (missing.Count == 0 && noConnection.Count == 0)
                c.Findings.Add(new Finding(Severity.Ok, "Connection references", "All " + logicalNames.Count + " connection reference(s) have a connection in Target."));
        }

        private void CheckDuplicates(CheckContext c)
        {
            var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("name", "statecode", "ismanaged") };
            q.Criteria.AddCondition("name", ConditionOperator.Equal, c.Item.Name);
            q.Criteria.AddCondition("type", ConditionOperator.Equal, 1);
            q.Criteria.AddCondition("category", ConditionOperator.Equal, c.Item.SubTypeCode);
            q.Criteria.AddCondition("workflowid", ConditionOperator.NotEqual, c.Item.Id);
            var copies = c.Target.RetrieveMultiple(q).Entities;
            if (copies.Count == 0) return;

            c.Findings.Add(new Finding(Severity.Warning, "Duplicate copies",
                copies.Count + " other " + CategoryLabel(c.Item.SubTypeCode).ToLowerInvariant() + "(s) named '" + c.Item.Name + "' exist in Target: " +
                string.Join("; ", copies.Select(e => (OptionValue(e, "statecode") == 1 ? "on" : "off") + ", " +
                    (e.GetAttributeValue<bool>("ismanaged") ? "managed" : "unmanaged") + ", id " + e.Id)) +
                ". An old copy that is still on can do the old behaviour.",
                "Turn off or delete the old copy in Target (after checking nothing else uses it)."));
        }
    }
}
