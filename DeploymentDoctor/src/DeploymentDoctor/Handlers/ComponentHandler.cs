using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DeploymentDoctor
{
    /// <summary>A component as read from one environment (or from a solution layer).</summary>
    internal class Snapshot
    {
        public Entity Record;                                           // published record (null for a layer)
        public Dictionary<string, string> Content = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Unpublished;                  // null when there is no unpublished copy

        public string Get(string attr)
        {
            string v;
            return Content.TryGetValue(attr, out v) ? v : null;
        }
    }

    /// <summary>Everything a type-specific check needs.</summary>
    internal class CheckContext
    {
        public IOrganizationService Target;
        public IOrganizationService Dev;
        public ComponentItem Item;
        public Snapshot TargetSnap;
        public Snapshot DevSnap;                // null without Dev
        public Diagnosis Diagnosis;
        public List<Finding> Findings { get { return Diagnosis.Findings; } }
    }

    /// <summary>
    /// What differs per component type: where it lives, how it is compared, published and checked.
    /// The generic checks (solution, layers, publish state, content) live in DoctorService.
    /// </summary>
    internal abstract class ComponentHandler
    {
        public abstract ComponentKind Kind { get; }
        public abstract string Label { get; }               // "Form", "View", ...
        public abstract string Table { get; }               // backing table
        public abstract int ComponentType { get; }          // solutioncomponent.componenttype
        public abstract string LayerName { get; }           // msdyn_solutioncomponentname / RemoveActiveCustomizations name
        public abstract string[] ContentAttributes { get; } // compared between environments
        public virtual string[] OtherAttributes { get { return new string[0]; } }
        public virtual bool Publishable { get { return true; } }
        public virtual string GroupLabel { get { return "Table:"; } }

        /// <summary>True when adding the table with all subcomponents also adds this component.</summary>
        public virtual bool IncludedWithTable(ComponentItem item) { return false; }

        public abstract List<ComponentItem> LoadAll(IOrganizationService svc);

        /// <summary>Maker-readable differences, left vs right.</summary>
        public abstract List<string> Compare(Snapshot left, Snapshot right, string leftName, string rightName);

        /// <summary>Readable content for the XML/content tabs.</summary>
        public abstract string Pretty(Snapshot s);

        /// <summary>Is the content stored in a solution layer complete enough to compare with Dev?</summary>
        public virtual bool LayerContentUsable(Snapshot layer)
        {
            return ContentAttributes.Any(a => !string.IsNullOrEmpty(layer.Get(a)));
        }

        public virtual string PublishScope(ComponentItem item) { return "table '" + item.Group + "'"; }

        public virtual void Publish(IOrganizationService svc, ComponentItem item)
        {
            PublishXml(svc, "<entities><entity>" + item.Group + "</entity></entities>");
        }

        /// <summary>Checks only this type has (form access, flow state, ...).</summary>
        public virtual void TypeChecks(CheckContext c) { }

        /// <summary>What to tell users when Target already matches Dev.</summary>
        public virtual string WhenIdentical { get { return "Ask users to hard-refresh (Ctrl+F5)."; } }

        // ------------------------------------------------------------ shared

        public Snapshot Load(IOrganizationService svc, Guid id)
        {
            Entity e;
            try { e = svc.Retrieve(Table, id, new ColumnSet(ContentAttributes.Concat(OtherAttributes).Distinct().ToArray())); }
            catch (Exception) { return null; }

            var s = new Snapshot { Record = e };
            foreach (var a in ContentAttributes) s.Content[a] = e.GetAttributeValue<string>(a);

            if (Publishable)
            {
                try
                {
                    var r = (RetrieveUnpublishedResponse)svc.Execute(new RetrieveUnpublishedRequest
                    {
                        Target = new EntityReference(Table, id),
                        ColumnSet = new ColumnSet(ContentAttributes)
                    });
                    if (r.Entity != null)
                    {
                        s.Unpublished = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var a in ContentAttributes) s.Unpublished[a] = r.Entity.GetAttributeValue<string>(a);
                    }
                }
                catch (Exception) { /* no unpublished copy */ }
            }
            return s;
        }

        /// <summary>Content attributes pulled out of a layer's msdyn_componentjson.</summary>
        public Snapshot FromLayer(string componentJson)
        {
            var s = new Snapshot();
            foreach (var a in ContentAttributes)
            {
                var v = Json.FindValue(componentJson, a);
                if (v != null) s.Content[a] = v;
            }
            return s;
        }

        public bool UnpublishedDiffers(Snapshot s)
        {
            return s.Unpublished != null && ContentAttributes.Any(a => Normalize(s.Get(a)) != Normalize(s.Unpublished[a]));
        }

        public bool RawDiffers(Snapshot a, Snapshot b)
        {
            return ContentAttributes.Any(x => Normalize(a.Get(x)) != Normalize(b.Get(x)));
        }

        public Snapshot UnpublishedAsSnapshot(Snapshot s)
        {
            var u = new Snapshot { Record = s.Record };
            foreach (var kv in s.Unpublished) u.Content[kv.Key] = kv.Value;
            return u;
        }

        protected static string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var t = s.Trim();
            return t.StartsWith("<") ? FormXmlDiff.Normalize(t) : t;
        }

        public static void PublishXml(IOrganizationService svc, string inner)
        {
            svc.Execute(new PublishXmlRequest { ParameterXml = "<importexportxml>" + inner + "</importexportxml>" });
        }

        public static int OptionValue(Entity e, string attr, int dflt = -1)
        {
            if (e == null) return dflt;
            var v = e.GetAttributeValue<OptionSetValue>(attr);
            return v == null ? dflt : v.Value;
        }

        /// <summary>Page through a query.</summary>
        public static List<Entity> All(IOrganizationService svc, QueryExpression q)
        {
            var list = new List<Entity>();
            q.PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 };
            while (true)
            {
                var res = svc.RetrieveMultiple(q);
                list.AddRange(res.Entities);
                if (!res.MoreRecords) break;
                q.PageInfo.PageNumber++;
                q.PageInfo.PagingCookie = res.PagingCookie;
            }
            return list;
        }

        /// <summary>
        /// Model-driven apps can pick specific forms/views per table. Report apps that pick some of this
        /// table's components of this kind but not this one.
        /// </summary>
        protected void CheckApps(CheckContext c, IEnumerable<Guid> sameKindIds, string what)
        {
            var ids = sameKindIds.Select(x => (object)x).ToArray();
            if (ids.Length == 0) return;

            var q = new QueryExpression("appmodulecomponent") { ColumnSet = new ColumnSet("objectid") };
            q.Criteria.AddCondition("componenttype", ConditionOperator.Equal, ComponentType);
            q.Criteria.AddCondition("objectid", ConditionOperator.In, ids);
            var app = q.AddLink("appmodule", "appmoduleidunique", "appmoduleidunique");
            app.EntityAlias = "app";
            app.Columns = new ColumnSet("name");

            var byApp = c.Target.RetrieveMultiple(q).Entities
                .GroupBy(e => { var av = e.GetAttributeValue<AliasedValue>("app.name"); return av == null ? "?" : (string)av.Value; })
                .ToList();
            var missing = byApp.Where(g => !g.Any(e => e.GetAttributeValue<Guid>("objectid") == c.Item.Id)).Select(g => g.Key).ToList();
            var included = byApp.Where(g => g.Any(e => e.GetAttributeValue<Guid>("objectid") == c.Item.Id)).Select(g => g.Key).ToList();

            var check = Label + " in apps";
            if (missing.Count > 0)
                c.Findings.Add(new Finding(Severity.Problem, check,
                    "These apps pick specific " + what + " for '" + c.Item.Group + "' and do NOT include this one: " + string.Join(", ", missing) +
                    ". Users of those apps never see it.",
                    "Add it to the app in Dev (app designer > table > " + what + "), then deploy the app with the solution."));
            else
                c.Findings.Add(new Finding(Severity.Ok, check,
                    included.Count > 0
                        ? "Included in: " + string.Join(", ", included) + ". Apps not listed show all " + what + " of the table."
                        : "No app restricts the " + what + " of this table, so all apps show it."));
        }
    }

    internal static class Handlers
    {
        public static readonly List<ComponentHandler> All = new List<ComponentHandler>
        {
            new FormHandler(), new ViewHandler(), new WebResourceHandler(), new ProcessHandler()
        };

        public static ComponentHandler For(ComponentKind kind)
        {
            return All.First(h => h.Kind == kind);
        }
    }
}
