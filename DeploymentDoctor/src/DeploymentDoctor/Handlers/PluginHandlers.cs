using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DeploymentDoctor
{
    /// <summary>
    /// Plug-in steps (sdkmessageprocessingstep): what runs, when, on which message and table, and whether it is enabled.
    /// Note: the "remove unmanaged layer" fix is not offered for this type; the tool explains the manual way.
    /// </summary>
    internal class PluginStepHandler : ComponentHandler
    {
        public override ComponentKind Kind { get { return ComponentKind.PluginStep; } }
        public override string Label { get { return "Plug-in step"; } }
        public override string Table { get { return "sdkmessageprocessingstep"; } }
        public override string Category { get { return "Code and resources"; } }
        public override int ComponentType { get { return 92; } }
        public override string LayerName { get { return "SdkMessageProcessingStep"; } }
        public override string[] ContentAttributes { get { return new[] { "filteringattributes", "configuration" }; } }
        public override string[] OtherAttributes
        {
            get { return new[] { "name", "stage", "mode", "rank", "statecode", "description", "sdkmessageid", "plugintypeid" }; }
        }
        public override bool Publishable { get { return false; } }

        public override bool SupportsRemoveActive { get { return false; } }
        public override string RemoveActiveAdvice(ComponentItem item)
        {
            return "Someone changed this plug-in step directly in Target. Make the change in Dev and deploy it. " +
                   "To remove the unmanaged change use 'See solution layers' in the maker portal and delete the Active layer. Deployment Doctor does not remove this layer itself yet.";
        }

        public static string StageLabel(int stage)
        {
            switch (stage)
            {
                case 10: return "Pre-validation";
                case 20: return "Pre-operation";
                case 30: return "Main operation";
                case 40: return "Post-operation";
                default: return "Stage " + stage;
            }
        }

        public static string ModeLabel(int mode) { return mode == 1 ? "asynchronous" : "synchronous"; }

        public override string GroupLabel { get { return "Table:"; } }

        public override List<ComponentItem> LoadAll(IOrganizationService svc)
        {
            var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("name", "stage", "mode", "statecode") };
            var filter = q.AddLink("sdkmessagefilter", "sdkmessagefilterid", "sdkmessagefilterid", JoinOperator.LeftOuter);
            filter.EntityAlias = "f";
            filter.Columns = new ColumnSet("primaryobjecttypecode");
            q.AddOrder("name", OrderType.Ascending);
            return All(svc, q).Select(e =>
            {
                var av = e.GetAttributeValue<AliasedValue>("f.primaryobjecttypecode");
                var table = av == null ? null : av.Value as string;
                return new ComponentItem
                {
                    Kind = Kind,
                    Id = e.Id,
                    Name = e.GetAttributeValue<string>("name"),
                    Group = string.IsNullOrEmpty(table) || table == "none" ? ProcessHandler.NoTable : table,
                    SubType = StageLabel(OptionValue(e, "stage")) + ", " + ModeLabel(OptionValue(e, "mode")),
                    SubTypeCode = OptionValue(e, "stage"),
                    IsActive = OptionValue(e, "statecode", 0) == 0,
                    InactiveText = "disabled"
                };
            }).ToList();
        }

        /// <summary>The step plus the readable names of its message, plug-in class and assembly (with version).</summary>
        public override Snapshot Load(IOrganizationService svc, Guid id)
        {
            var s = base.Load(svc, id);
            if (s == null) return null;
            try
            {
                var msg = s.Record.GetAttributeValue<EntityReference>("sdkmessageid");
                if (msg != null) s.Content["message"] = svc.Retrieve("sdkmessage", msg.Id, new ColumnSet("name")).GetAttributeValue<string>("name");
            }
            catch (Exception) { }
            try
            {
                var pt = s.Record.GetAttributeValue<EntityReference>("plugintypeid");
                if (pt != null)
                {
                    var t = svc.Retrieve("plugintype", pt.Id, new ColumnSet("typename", "pluginassemblyid"));
                    s.Content["plugintype"] = t.GetAttributeValue<string>("typename");
                    var asm = t.GetAttributeValue<EntityReference>("pluginassemblyid");
                    if (asm != null)
                    {
                        var a = svc.Retrieve("pluginassembly", asm.Id, new ColumnSet("name", "version"));
                        s.Content["assembly"] = a.GetAttributeValue<string>("name") + " v" + a.GetAttributeValue<string>("version");
                    }
                }
            }
            catch (Exception) { }
            return s;
        }

        internal static Dictionary<string, string> Describe(Snapshot s)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (s == null || s.Record == null) return d;
            var r = s.Record;
            FormXmlDiff.Add(d, "Message", s.Get("message") ?? "?");
            FormXmlDiff.Add(d, "Stage", StageLabel(OptionValue(r, "stage")));
            FormXmlDiff.Add(d, "Mode", ModeLabel(OptionValue(r, "mode")));
            FormXmlDiff.Add(d, "Execution order (rank)", r.GetAttributeValue<int>("rank").ToString());
            FormXmlDiff.Add(d, "Plug-in class", s.Get("plugintype") ?? "?");
            FormXmlDiff.Add(d, "Plug-in assembly", s.Get("assembly") ?? "?");
            FormXmlDiff.Add(d, "Filtering columns", string.IsNullOrEmpty(s.Get("filteringattributes")) ? "(all columns)" : s.Get("filteringattributes"));
            var cfg = s.Get("configuration");
            FormXmlDiff.Add(d, "Unsecure configuration", string.IsNullOrEmpty(cfg) ? "(none)" : "set, #" + TextDiff.Hash(Encoding.UTF8.GetBytes(cfg)));
            FormXmlDiff.Add(d, "Description", r.GetAttributeValue<string>("description") ?? "-");
            return d;
        }

        public override List<string> Compare(Snapshot left, Snapshot right, string leftName, string rightName)
        {
            return FormXmlDiff.CompareMaps(Describe(left), Describe(right), leftName, rightName);
        }

        public override string Pretty(Snapshot s)
        {
            return string.Join("\r\n", Describe(s).Select(kv => kv.Key + ": " + kv.Value));
        }

        public override bool LayerContentUsable(Snapshot layer) { return false; }

        public override string State(IOrganizationService svc, Snapshot s)
        {
            return OptionValue(s.Record, "statecode", 0) == 0 ? "Enabled" : "Disabled";
        }

        public override string WhenIdentical
        {
            get { return "Check the plug-in assembly version is the one you expect, and look at the plug-in trace log for errors when it runs."; }
        }

        public override void TypeChecks(CheckContext c)
        {
            var tgtState = State(c.Target, c.TargetSnap);
            var devState = c.DevSnap == null ? null : State(c.Dev, c.DevSnap);

            if (tgtState == "Disabled")
            {
                c.Findings.Add(new Finding(devState == "Disabled" ? Severity.Warning : (devState == "Enabled" ? Severity.Problem : Severity.Warning), "Step enabled",
                    "The step is DISABLED in Target" + (devState == "Enabled" ? ", but enabled in Dev" : "") + ", so its logic never runs.",
                    "Enable it in Target (or click the button). If it was disabled on purpose, make Dev match so the next deployment doesn't hide the difference.",
                    new FixAction
                    {
                        Label = "Enable step in Target",
                        OnDev = false,
                        WhatItDoes = "Enable the plug-in step '" + c.Item.Name + "' in Target (SetState: Enabled). Its logic then runs for every matching operation.",
                        Run = s => s.Execute(new SetStateRequest
                        {
                            EntityMoniker = new EntityReference(Table, c.Item.Id),
                            State = new OptionSetValue(0),
                            Status = new OptionSetValue(1)
                        }),
                        Undo = "Yes, disable it again (Plug-in Registration Tool, or the step in Solutions).",
                        Advice = "It starts running straight away for new operations: it can change records, call other systems or block saves. " +
                                 "Make sure it was not disabled on purpose (for example during a data migration), and that the plug-in assembly version is the right one.",
                        ManualSteps = "1. Open the Plug-in Registration Tool and connect to the Target environment (or make.powerapps.com > Solutions > the solution > the step).\r\n" +
                                      "2. Find the step '" + c.Item.Name + "'.\r\n" +
                                      "3. Right-click > Enable."
                    }));
            }
            else
            {
                c.Findings.Add(new Finding(Severity.Ok, "Step enabled", "The step is enabled in Target." + (devState == "Disabled" ? " (It is disabled in Dev.)" : "")));
            }

            if (c.DevSnap != null && c.DevSnap.Get("assembly") != null && c.TargetSnap.Get("assembly") != null && c.DevSnap.Get("assembly") != c.TargetSnap.Get("assembly"))
                c.Findings.Add(new Finding(Severity.Warning, "Plug-in assembly version",
                    "The step runs " + c.TargetSnap.Get("assembly") + " in Target but " + c.DevSnap.Get("assembly") + " in Dev.",
                    "Deploy the plug-in assembly with the solution (or register the same assembly version in Target)."));
        }
    }

    /// <summary>Plug-in assemblies (pluginassembly): version, isolation and a fingerprint of the DLL itself.</summary>
    internal class PluginAssemblyHandler : ComponentHandler
    {
        public override ComponentKind Kind { get { return ComponentKind.PluginAssembly; } }
        public override string Label { get { return "Plug-in assembly"; } }
        public override string Table { get { return "pluginassembly"; } }
        public override string Category { get { return "Code and resources"; } }
        public override int ComponentType { get { return 91; } }
        public override string LayerName { get { return "PluginAssembly"; } }
        public override string[] ContentAttributes { get { return new[] { "content" }; } }
        public override string[] OtherAttributes { get { return new[] { "name", "version", "isolationmode", "sourcetype" }; } }
        public override bool HasGroups { get { return false; } }
        public override bool Publishable { get { return false; } }

        public override bool SupportsRemoveActive { get { return false; } }
        public override string RemoveActiveAdvice(ComponentItem item)
        {
            return "Someone updated this assembly directly in Target. Deploy it from Dev with the solution instead. " +
                   "To remove the unmanaged change use 'See solution layers' in the maker portal. Deployment Doctor does not remove this layer itself yet.";
        }

        public override List<ComponentItem> LoadAll(IOrganizationService svc)
        {
            var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("name", "version") };
            q.AddOrder("name", OrderType.Ascending);
            return All(svc, q).Select(e => new ComponentItem
            {
                Kind = Kind,
                Id = e.Id,
                Name = e.GetAttributeValue<string>("name"),
                Group = SiteMapHandler.Everything,
                SubType = "v" + e.GetAttributeValue<string>("version"),
                IsActive = true
            }).ToList();
        }

        private static byte[] Bytes(Snapshot s)
        {
            var b64 = s.Get("content");
            if (string.IsNullOrEmpty(b64)) return new byte[0];
            try { return Convert.FromBase64String(b64); }
            catch (FormatException) { return Encoding.UTF8.GetBytes(b64); }
        }

        private static string Isolation(Snapshot s) { return OptionValue(s.Record, "isolationmode") == 1 ? "None" : "Sandbox"; }

        public override List<string> Compare(Snapshot left, Snapshot right, string leftName, string rightName)
        {
            var result = new List<string>();
            var lv = left.Record.GetAttributeValue<string>("version");
            var rv = right.Record.GetAttributeValue<string>("version");
            if (lv != rv) result.Add(string.Format("Changed: version\r\n      {0}: {1}\r\n      {2}: {3}", leftName, lv, rightName, rv));
            if (Isolation(left) != Isolation(right)) result.Add(string.Format("Changed: isolation mode\r\n      {0}: {1}\r\n      {2}: {3}", leftName, Isolation(left), rightName, Isolation(right)));

            var a = Bytes(left); var b = Bytes(right);
            if (!a.SequenceEqual(b))
                result.Add(string.Format("Changed: the DLL itself ({0}: {1:N0} bytes, #{2}; {3}: {4:N0} bytes, #{5})", leftName, a.Length, TextDiff.Hash(a), rightName, b.Length, TextDiff.Hash(b)));
            return result;
        }

        public override string Pretty(Snapshot s)
        {
            var b = Bytes(s);
            return "Version: " + s.Record.GetAttributeValue<string>("version") + "\r\nIsolation: " + Isolation(s) + "\r\nDLL: " + b.Length.ToString("N0") + " bytes, #" + TextDiff.Hash(b);
        }

        public override bool LayerContentUsable(Snapshot layer) { return false; }

        public override string WhenIdentical
        {
            get { return "The same DLL is in both environments. If behaviour still differs look at the plug-in steps (enabled? filtering columns?) and the plug-in trace log."; }
        }

        public override void TypeChecks(CheckContext c)
        {
            if (c.DevSnap == null) return;
            var dv = c.DevSnap.Record.GetAttributeValue<string>("version");
            var tv = c.TargetSnap.Record.GetAttributeValue<string>("version");
            c.Findings.Add(new Finding(dv == tv ? Severity.Ok : Severity.Warning, "Assembly version",
                dv == tv ? "Version " + tv + " in both environments." : "Dev has version " + dv + ", Target has " + tv + ".",
                dv == tv ? null : "Deploy the solution that contains the new assembly version, or update the assembly in Target."));
        }
    }
}
