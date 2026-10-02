using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace UnmanagedLayerFinder
{
    internal static class LayerService
    {
        private const string UnmanagedLayerName = "Active";

        // Layer name that returned rows for a component type, so later components skip failed candidates
        private static readonly Dictionary<int, string> WorkingLayerName = new Dictionary<int, string>();

        public static List<SolutionItem> LoadSolutions(IOrganizationService svc)
        {
            var q = new QueryExpression("solution") { ColumnSet = new ColumnSet("friendlyname", "uniquename", "ismanaged", "version") };
            q.Criteria.AddCondition("isvisible", ConditionOperator.Equal, true);
            q.AddOrder("friendlyname", OrderType.Ascending);
            var pub = q.AddLink("publisher", "publisherid", "publisherid");
            pub.EntityAlias = "pub";
            pub.Columns = new ColumnSet("friendlyname", "uniquename");

            return svc.RetrieveMultiple(q).Entities.Select(e =>
            {
                bool managed = e.GetAttributeValue<bool>("ismanaged");
                string pubName = Aliased(e, "pub.friendlyname");
                return new SolutionItem
                {
                    Id = e.Id,
                    IsManaged = managed,
                    PublisherName = pubName,
                    PublisherUniqueName = Aliased(e, "pub.uniquename"),
                    Display = string.Format("{0} ({1}) v{2} [{3}] - {4}",
                        e.GetAttributeValue<string>("friendlyname"),
                        e.GetAttributeValue<string>("uniquename"),
                        e.GetAttributeValue<string>("version"),
                        managed ? "managed" : "unmanaged",
                        pubName)
                };
            }).ToList();
        }

        private static string Aliased(Entity e, string name)
        {
            var av = e.GetAttributeValue<AliasedValue>(name);
            return av == null ? null : av.Value as string;
        }

        public static List<ComponentRow> LoadComponents(IOrganizationService svc, Guid solutionId)
        {
            var rows = new List<ComponentRow>();

            var q = new QueryExpression("solutioncomponent")
            {
                ColumnSet = new ColumnSet("objectid", "componenttype"),
                PageInfo = new PagingInfo { Count = 5000, PageNumber = 1 }
            };
            q.Criteria.AddCondition("solutionid", ConditionOperator.Equal, solutionId);

            while (true)
            {
                var res = svc.RetrieveMultiple(q);
                foreach (var e in res.Entities)
                {
                    rows.Add(new ComponentRow
                    {
                        TypeCode = e.GetAttributeValue<OptionSetValue>("componenttype").Value,
                        ComponentId = e.GetAttributeValue<Guid>("objectid")
                    });
                }
                if (!res.MoreRecords) break;
                q.PageInfo.PageNumber++;
                q.PageInfo.PagingCookie = res.PagingCookie;
            }

            RegisterDynamicTypes(svc, rows.Select(r => r.TypeCode));

            foreach (var r in rows)
            {
                var info = ComponentTypes.Get(r.TypeCode);
                r.Type = info.Label;
                r.Status = info.SupportsLayers ? "" : "Not supported";
            }

            ResolveNames(svc, rows);
            return rows.OrderBy(r => r.Type).ThenBy(r => r.Name).ToList();
        }

        /// <summary>
        /// Component types without a fixed code (connection references, AI models, etc.) get org-specific codes.
        /// Look them up in solutioncomponentdefinition and register them with their table and layer name candidates.
        /// </summary>
        private static void RegisterDynamicTypes(IOrganizationService svc, IEnumerable<int> codes)
        {
            ComponentTypes.ClearDynamic();
            WorkingLayerName.Clear();

            var unknown = ComponentTypes.UnknownCodes(codes).Cast<object>().ToArray();
            if (unknown.Length == 0) return;

            EntityCollection defs;
            try
            {
                var q = new QueryExpression("solutioncomponentdefinition")
                {
                    ColumnSet = new ColumnSet("name", "primaryentityname", "solutioncomponenttype")
                };
                q.Criteria.AddCondition("solutioncomponenttype", ConditionOperator.In, unknown);
                defs = svc.RetrieveMultiple(q);
            }
            catch (Exception)
            {
                return; // leave them as "Not supported"
            }

            foreach (var d in defs.Entities)
            {
                var code = d.GetAttributeValue<int>("solutioncomponenttype");
                var name = d.GetAttributeValue<string>("name");
                var table = d.GetAttributeValue<string>("primaryentityname");

                var info = new ComponentTypeInfo { Label = name ?? table ?? ("Type " + code) };
                foreach (var candidate in new[] { name, table })
                    if (!string.IsNullOrEmpty(candidate) && !info.LayerNames.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                        info.LayerNames.Add(candidate);

                if (!string.IsNullOrEmpty(table))
                {
                    try
                    {
                        var meta = ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest
                        {
                            LogicalName = table,
                            EntityFilters = EntityFilters.Entity
                        })).EntityMetadata;

                        info.Table = table;
                        info.IdAttribute = meta.PrimaryIdAttribute;
                        info.NameAttribute = meta.PrimaryNameAttribute;
                        var label = meta.DisplayName == null || meta.DisplayName.UserLocalizedLabel == null
                            ? null : meta.DisplayName.UserLocalizedLabel.Label;
                        if (!string.IsNullOrEmpty(label)) info.Label = label;
                    }
                    catch (Exception) { /* no table metadata: layers only */ }
                }

                ComponentTypes.RegisterDynamic(code, info);
            }
        }

        /// <summary>Fill Name (and Type/Details for processes and environment variables).</summary>
        private static void ResolveNames(IOrganizationService svc, List<ComponentRow> rows)
        {
            // Tables
            if (rows.Any(r => r.TypeCode == 1))
            {
                var meta = (RetrieveAllEntitiesResponse)svc.Execute(new RetrieveAllEntitiesRequest
                {
                    EntityFilters = EntityFilters.Entity,
                    RetrieveAsIfPublished = true
                });
                var byId = meta.EntityMetadata
                    .Where(m => m.MetadataId.HasValue)
                    .ToDictionary(m => m.MetadataId.Value, m => m.LogicalName);
                foreach (var r in rows.Where(x => x.TypeCode == 1))
                {
                    string ln;
                    if (byId.TryGetValue(r.ComponentId, out ln)) r.Name = ln;
                }
            }

            // Record-backed types
            foreach (var group in rows.Where(r => ComponentTypes.Get(r.TypeCode).Table != null).GroupBy(r => r.TypeCode))
            {
                var info = ComponentTypes.Get(group.Key);
                if (string.IsNullOrEmpty(info.IdAttribute)) continue;

                var columns = new List<string>();
                if (!string.IsNullOrEmpty(info.NameAttribute)) columns.Add(info.NameAttribute);
                if (group.Key == 29) columns.Add("category");
                if (group.Key == 380) columns.Add("defaultvalue");
                if (group.Key == 381) columns.AddRange(new[] { "value", "environmentvariabledefinitionid" });
                if (columns.Count == 0) continue;

                var ids = group.Select(r => (object)r.ComponentId).Distinct().ToList();
                var records = new Dictionary<Guid, Entity>();
                try
                {
                    for (int i = 0; i < ids.Count; i += 200)
                    {
                        var q = new QueryExpression(info.Table) { ColumnSet = new ColumnSet(columns.ToArray()) };
                        q.Criteria.AddCondition(info.IdAttribute, ConditionOperator.In, ids.Skip(i).Take(200).ToArray());
                        foreach (var e in svc.RetrieveMultiple(q).Entities)
                            records[e.Id] = e;
                    }
                }
                catch (Exception) { /* leave names blank for this type */ }

                foreach (var r in group)
                {
                    Entity e;
                    if (!records.TryGetValue(r.ComponentId, out e)) continue;
                    if (!string.IsNullOrEmpty(info.NameAttribute)) r.Name = e.GetAttributeValue<string>(info.NameAttribute);

                    switch (group.Key)
                    {
                        case 29:
                            var cat = e.GetAttributeValue<OptionSetValue>("category");
                            r.Type = ComponentTypes.ProcessLabel(cat == null ? (int?)null : cat.Value);
                            break;
                        case 380:
                            var def = e.GetAttributeValue<string>("defaultvalue");
                            r.Details = "Definition. Default value: " + Shorten(def);
                            break;
                        case 381:
                            var parent = e.GetAttributeValue<EntityReference>("environmentvariabledefinitionid");
                            r.Details = string.Format("Current value: {0}  (for definition '{1}')",
                                Shorten(e.GetAttributeValue<string>("value")),
                                parent == null ? "?" : parent.Name);
                            break;
                    }
                }
            }
        }

        public static void CheckComponent(IOrganizationService svc, ComponentRow row, bool includeAudit)
        {
            row.Layers = 0; row.LayerStack = null; row.UnmanagedLayerTime = null; row.ChangedAttributes = null;
            row.ModifiedBy = null; row.ModifiedOn = null; row.AuditUser = null; row.AuditTime = null; row.Notes = null;

            var info = ComponentTypes.Get(row.TypeCode);
            if (!info.SupportsLayers)
            {
                row.Status = "Not supported";
                row.Notes = "Component type " + row.TypeCode + " is not mapped (see ComponentTypes.cs)";
                return;
            }

            try
            {
                var layers = GetLayers(svc, row);
                row.Layers = layers.Count;
                row.LayerStack = string.Join(" > ", layers.Select(l => l.GetAttributeValue<string>("msdyn_solutionname")));

                if (string.IsNullOrEmpty(row.Name) && layers.Count > 0)
                    row.Name = layers[0].GetAttributeValue<string>("msdyn_name");

                var active = layers.FirstOrDefault(l => l.GetAttributeValue<string>("msdyn_solutionname") == UnmanagedLayerName);
                if (active == null)
                {
                    row.Status = layers.Count == 0 ? "No layers" : "Managed only";
                    if (layers.Count == 0) row.Notes = "No layers returned for: " + string.Join(", ", info.LayerNames);
                    return;
                }

                row.Status = "Unmanaged";
                var time = active.GetAttributeValue<DateTime?>("msdyn_overwritetime");
                // Dataverse returns 1900-01-01 when it did not record a time; don't show it as real
                if (time.HasValue && time.Value.Year > 1900) row.UnmanagedLayerTime = time;
                else row.Notes = Append(row.Notes, "Layer time not recorded by Dataverse (see ModifiedOn)");
                row.ChangedAttributes = SummarizeChanges(active.GetAttributeValue<string>("msdyn_changes"));
            }
            catch (Exception ex)
            {
                row.Status = "Error";
                row.Notes = ex.Message;
                return;
            }

            LookupModifiedBy(svc, row);
            if (includeAudit) LookupAudit(svc, row);
        }

        /// <summary>
        /// Query msdyn_componentlayer, trying each candidate layer name until one returns rows
        /// (every component in a solution has at least one layer). Remembers the name that worked per type.
        /// </summary>
        private static DataCollection<Entity> GetLayers(IOrganizationService svc, ComponentRow row)
        {
            var info = ComponentTypes.Get(row.TypeCode);
            string known;
            var candidates = WorkingLayerName.TryGetValue(row.TypeCode, out known)
                ? new[] { known }
                : info.LayerNames.ToArray();

            DataCollection<Entity> layers = null;
            Exception lastError = null;
            foreach (var name in candidates)
            {
                try
                {
                    var q = new QueryExpression("msdyn_componentlayer")
                    {
                        ColumnSet = new ColumnSet("msdyn_name", "msdyn_solutionname", "msdyn_publishername",
                                                  "msdyn_overwritetime", "msdyn_order", "msdyn_changes")
                    };
                    q.Criteria.AddCondition("msdyn_componentid", ConditionOperator.Equal, row.ComponentId.ToString());
                    q.Criteria.AddCondition("msdyn_solutioncomponentname", ConditionOperator.Equal, name);
                    q.AddOrder("msdyn_order", OrderType.Descending);

                    layers = svc.RetrieveMultiple(q).Entities;
                    if (layers.Count > 0)
                    {
                        WorkingLayerName[row.TypeCode] = name;
                        return layers;
                    }
                }
                catch (Exception ex) { lastError = ex; }
            }

            if (layers == null && lastError != null) throw lastError;
            return layers ?? new EntityCollection().Entities;
        }

        /// <summary>Last modifier of the backing record (only for record-backed component types).</summary>
        private static void LookupModifiedBy(IOrganizationService svc, ComponentRow row)
        {
            var info = ComponentTypes.Get(row.TypeCode);
            if (info.Table == null)
            {
                row.Notes = Append(row.Notes, "No modifiedby on this component type (use audit)");
                return;
            }
            try
            {
                var e = svc.Retrieve(info.Table, row.ComponentId, new ColumnSet("modifiedby", "modifiedon"));
                var by = e.GetAttributeValue<EntityReference>("modifiedby");
                row.ModifiedBy = by == null ? null : by.Name;
                row.ModifiedOn = e.GetAttributeValue<DateTime?>("modifiedon");
            }
            catch (Exception ex)
            {
                row.Notes = Append(row.Notes, "modifiedby lookup failed: " + ex.Message);
            }
        }

        /// <summary>Latest audit entry for the component. Only works if auditing was on when the change happened.</summary>
        private static void LookupAudit(IOrganizationService svc, ComponentRow row)
        {
            try
            {
                var q = new QueryExpression("audit")
                {
                    ColumnSet = new ColumnSet("createdon", "userid"),
                    TopCount = 1
                };
                q.Criteria.AddCondition("objectid", ConditionOperator.Equal, row.ComponentId);
                q.AddOrder("createdon", OrderType.Descending);
                var e = svc.RetrieveMultiple(q).Entities.FirstOrDefault();
                if (e == null)
                {
                    row.Notes = Append(row.Notes, "No audit record");
                    return;
                }
                var u = e.GetAttributeValue<EntityReference>("userid");
                row.AuditUser = u == null ? null : u.Name;
                row.AuditTime = e.GetAttributeValue<DateTime?>("createdon");
            }
            catch (Exception ex)
            {
                row.Notes = Append(row.Notes, "audit lookup failed: " + ex.Message);
            }
        }

        /// <summary>msdyn_changes is JSON; pull out the attribute names it mentions, else show a trimmed raw value.</summary>
        private static string SummarizeChanges(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            var keys = Regex.Matches(json, "\"(?:Key|Name|name|key)\"\\s*:\\s*\"([^\"]+)\"")
                            .Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
            if (keys.Count > 0) return string.Join(", ", keys);
            return json.Length > 200 ? json.Substring(0, 200) + "..." : json;
        }

        private static string Shorten(string s)
        {
            if (s == null) return "(empty)";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > 120 ? s.Substring(0, 120) + "..." : s;
        }

        private static string Append(string existing, string add)
        {
            return string.IsNullOrEmpty(existing) ? add : existing + "; " + add;
        }
    }
}
