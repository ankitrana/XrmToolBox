using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace DeploymentDoctor
{
    /// <summary>
    /// Loading lists and the generic diagnosis. "Target" = where the change doesn't show, "Dev" = source.
    /// Type-specific parts (content compare, extra checks) are in the Handlers folder.
    /// </summary>
    internal static class DoctorService
    {
        private const int TableComponentType = 1;
        private const string ActiveLayer = "Active";

        // ------------------------------------------------------------ Loading

        public static List<SolutionItem> LoadSolutions(IOrganizationService svc)
        {
            var q = new QueryExpression("solution") { ColumnSet = new ColumnSet("friendlyname", "uniquename", "ismanaged", "version") };
            q.Criteria.AddCondition("isvisible", ConditionOperator.Equal, true);
            q.AddOrder("friendlyname", OrderType.Ascending);
            var pub = q.AddLink("publisher", "publisherid", "publisherid");
            pub.EntityAlias = "pub";
            pub.Columns = new ColumnSet("friendlyname");

            return svc.RetrieveMultiple(q).Entities.Select(ToSolution).ToList();
        }

        private static SolutionItem FindSolution(IOrganizationService svc, string uniqueName)
        {
            var q = new QueryExpression("solution") { ColumnSet = new ColumnSet("friendlyname", "uniquename", "ismanaged", "version") };
            q.Criteria.AddCondition("uniquename", ConditionOperator.Equal, uniqueName);
            var e = svc.RetrieveMultiple(q).Entities.FirstOrDefault();
            return e == null ? null : ToSolution(e);
        }

        private static SolutionItem ToSolution(Entity e)
        {
            var av = e.GetAttributeValue<AliasedValue>("pub.friendlyname");
            return new SolutionItem
            {
                Id = e.Id,
                UniqueName = e.GetAttributeValue<string>("uniquename"),
                FriendlyName = e.GetAttributeValue<string>("friendlyname"),
                Version = e.GetAttributeValue<string>("version"),
                IsManaged = e.GetAttributeValue<bool>("ismanaged"),
                PublisherName = av == null ? null : av.Value as string
            };
        }

        public static List<TableItem> LoadTables(IOrganizationService svc)
        {
            var meta = (RetrieveAllEntitiesResponse)svc.Execute(new RetrieveAllEntitiesRequest { EntityFilters = EntityFilters.Entity });
            return meta.EntityMetadata.Select(m => new TableItem
            {
                LogicalName = m.LogicalName,
                MetadataId = m.MetadataId ?? Guid.Empty,
                DisplayName = m.DisplayName == null || m.DisplayName.UserLocalizedLabel == null ? null : m.DisplayName.UserLocalizedLabel.Label
            }).ToList();
        }

        /// <summary>Components of this kind in the solution: directly, or through a table added with all subcomponents.</summary>
        public static HashSet<Guid> IdsInSolution(IOrganizationService svc, Guid solutionId, ComponentHandler h, List<ComponentItem> items, List<TableItem> tables)
        {
            var ids = new HashSet<Guid>();
            var fullTables = new HashSet<Guid>();
            var q = new QueryExpression("solutioncomponent") { ColumnSet = new ColumnSet("objectid", "componenttype", "rootcomponentbehavior") };
            q.Criteria.AddCondition("solutionid", ConditionOperator.Equal, solutionId);
            q.Criteria.AddCondition("componenttype", ConditionOperator.In, h.ComponentType, TableComponentType);

            foreach (var e in ComponentHandler.All(svc, q))
            {
                var id = e.GetAttributeValue<Guid>("objectid");
                if (ComponentHandler.OptionValue(e, "componenttype") == h.ComponentType) ids.Add(id);
                else if (ComponentHandler.OptionValue(e, "rootcomponentbehavior", 0) == 0) fullTables.Add(id);
            }

            var fullTableNames = new HashSet<string>(tables.Where(t => fullTables.Contains(t.MetadataId)).Select(t => t.LogicalName));
            foreach (var item in items.Where(i => h.IncludedWithTable(i) && fullTableNames.Contains(i.Group))) ids.Add(item.Id);
            return ids;
        }

        // ---------------------------------------------------------- Diagnosis

        private enum Membership { Direct, ViaTable, TableWithoutIt, None }

        public static Diagnosis Diagnose(IOrganizationService target, IOrganizationService dev, SolutionItem sol, ComponentItem item)
        {
            var h = Handlers.For(item.Kind);
            var label = h.Label;
            var lower = label.ToLowerInvariant();
            var d = new Diagnosis();
            var f = d.Findings;

            // 1. Exists in target
            var tgt = h.Load(target, item.Id);
            if (tgt == null)
                f.Add(new Finding(Severity.Problem, label + " exists in Target", label + " id " + item.Id + " was not found in Target.",
                    "The solution never delivered it. Add it to the solution in Dev and deploy again."));
            else
                d.TargetContent = h.Pretty(tgt);

            // 2-3. Dev side
            Snapshot devSnap = null;
            SolutionItem devSol = null;
            if (dev == null)
            {
                f.Add(new Finding(Severity.Info, "Dev comparison", "No Dev connection, so Dev checks were skipped.",
                    "Click 'Connect Dev (source)' to also check publish state, solution membership and content in Dev."));
            }
            else
            {
                Try(f, label + " in Dev", () =>
                {
                    devSnap = h.Load(dev, item.Id);
                    if (devSnap == null)
                    {
                        f.Add(new Finding(Severity.Warning, label + " in Dev", "This " + lower + " id does not exist in the Dev connection.",
                            "Check you connected the right Dev environment. If it was recreated in Dev it has a new id and is a different " + lower + "."));
                        return;
                    }
                    d.DevContent = h.Pretty(devSnap);
                    if (!h.Publishable) return;

                    if (h.UnpublishedDiffers(devSnap))
                    {
                        var changes = h.Compare(h.UnpublishedAsSnapshot(devSnap), devSnap, "Dev unpublished", "Dev published");
                        f.Add(new Finding(Severity.Problem, "Published in Dev",
                            "Dev has unpublished changes on this " + lower + (changes.Count > 0 ? " (" + changes.Count + " difference(s))" : "") +
                            ". Solution export takes the PUBLISHED version, so these changes were not in the package.",
                            "Publish in Dev, bump the solution version, export managed and import into Target.",
                            new FixAction
                            {
                                Label = "Publish in Dev",
                                OnDev = true,
                                WhatItDoes = "Publish " + h.PublishScope(item) + " in Dev (PublishXml)." +
                                             (item.Kind == ComponentKind.WebResource ? "" : " Other unpublished changes on that table in Dev are published too."),
                                Run = s => h.Publish(s, item),
                                Undo = "No, publishing can't be undone, but it only affects Dev. Target changes only when you export and import again.",
                                Advice = (item.Kind == ComponentKind.WebResource
                                            ? "Make sure the saved file in Dev is the version you want to ship."
                                            : "Other developers may have unfinished, unpublished changes on table '" + item.Group + "' in Dev; they get published too. Check with your team first.") +
                                         " After publishing: increase the solution version, export as managed and import into Target.",
                                ManualSteps = "1. Open make.powerapps.com and switch to the Dev environment.\r\n" +
                                              "2. Solutions > " + (sol == null ? "your solution" : "'" + sol.FriendlyName + "'") + ".\r\n" +
                                              "3. Open the " + lower + " '" + item.Name + "' and click Publish (or click 'Publish all customizations' on the solution)."
                            }));
                    }
                    else
                    {
                        f.Add(new Finding(Severity.Ok, "Published in Dev", "No unpublished changes on this " + lower + " in Dev."));
                    }
                });

                if (sol != null)
                {
                    Try(f, "Solution in Dev", () =>
                    {
                        devSol = FindSolution(dev, sol.UniqueName);
                        if (devSol == null)
                        {
                            f.Add(new Finding(Severity.Warning, "Solution in Dev", "Solution '" + sol.UniqueName + "' was not found in Dev.",
                                "Check the Dev connection, or that the same solution unique name is used in both environments."));
                            return;
                        }
                        if (devSnap == null) return;

                        var m = GetMembership(dev, devSol.Id, item, h);
                        if (m == Membership.Direct || m == Membership.ViaTable)
                        {
                            f.Add(new Finding(Severity.Ok, label + " in Dev solution",
                                m == Membership.Direct ? "It is in '" + sol.UniqueName + "' in Dev." : "It is included through table '" + item.Group + "' (all subcomponents)."));
                        }
                        else
                        {
                            f.Add(new Finding(Severity.Problem, label + " in Dev solution",
                                "The " + lower + " is NOT in '" + sol.UniqueName + "' in Dev" +
                                (m == Membership.TableWithoutIt ? " (the table is in the solution, but without this " + lower + ")." : ".") +
                                " Exports of this solution don't carry it.",
                                "Add it to the solution in Dev, bump the version, export and import again.",
                                devSol.IsManaged ? null : new FixAction
                                {
                                    Label = "Add to Dev solution",
                                    OnDev = true,
                                    WhatItDoes = "Add " + lower + " '" + item.Name + "' to unmanaged solution '" + sol.UniqueName + "' in Dev (AddSolutionComponent, no required components).",
                                    Run = s => s.Execute(new AddSolutionComponentRequest
                                    {
                                        ComponentType = h.ComponentType,
                                        ComponentId = item.Id,
                                        SolutionUniqueName = sol.UniqueName,
                                        AddRequiredComponents = false
                                    }),
                                    Undo = "Yes. In Dev open the solution, select the " + lower + " and choose Remove > Remove from this solution.",
                                    Advice = "Only this " + lower + " is added, not the components it needs (fields, web resources, connection references...). " +
                                             "Before exporting, run the solution checker or 'Show dependencies' so the import into Target doesn't fail on a missing dependency.",
                                    ManualSteps = "1. Open make.powerapps.com and switch to the Dev environment.\r\n" +
                                                  "2. Solutions > '" + sol.FriendlyName + "' > Add existing > " + AddExistingPath(item) + ".\r\n" +
                                                  "3. Pick '" + item.Name + "' and click Add. Add required components if asked.\r\n" +
                                                  "4. Increase the solution version, export as managed and import into Target."
                                }));
                        }
                    });
                }
            }

            if (tgt == null)
            {
                Finish(d, h);
                return d;
            }

            // 4. Solution membership and version in target
            if (sol != null)
            {
                Try(f, label + " in Target solution", () =>
                {
                    var m = GetMembership(target, sol.Id, item, h);
                    if (m == Membership.Direct || m == Membership.ViaTable)
                        f.Add(new Finding(Severity.Ok, label + " in Target solution", "It is part of '" + sol.UniqueName + "' in Target."));
                    else
                        f.Add(new Finding(Severity.Problem, label + " in Target solution",
                            "The " + lower + " is NOT part of '" + sol.UniqueName + "' in Target, so this solution never deployed it.",
                            "Add it to the solution in Dev, then export and import a new version."));
                });

                if (devSol != null)
                {
                    Version dv, tv;
                    if (Version.TryParse(devSol.Version, out dv) && Version.TryParse(sol.Version, out tv))
                    {
                        if (dv > tv)
                            f.Add(new Finding(Severity.Warning, "Solution version",
                                "Dev is at v" + devSol.Version + ", Target at v" + sol.Version + ". The latest version is not deployed yet (or an older zip was imported).",
                                "Export the current version from Dev and import it into Target."));
                        else
                            f.Add(new Finding(Severity.Info, "Solution version", "Dev v" + devSol.Version + ", Target v" + sol.Version + "."));
                    }
                }
            }

            // 5. Layers
            LayerRow mine = null, active = null;
            Try(f, "Solution layers", () =>
            {
                d.Layers = GetLayers(target, item.Id, h.LayerName);
                if (d.Layers.Count == 0)
                {
                    f.Add(new Finding(Severity.Warning, "Solution layers", "msdyn_componentlayer returned no layers for this " + lower + "."));
                    return;
                }

                active = d.Layers.FirstOrDefault(l => l.Solution == ActiveLayer);
                if (active != null) active.Note = "Unmanaged (Active)";

                if (sol != null)
                {
                    mine = d.Layers.FirstOrDefault(l => SameName(l.Solution, sol.UniqueName));
                    var holding = d.Layers.FirstOrDefault(l => SameName(l.Solution, sol.UniqueName + "_Upgrade"));
                    if (holding != null)
                    {
                        holding.Note = "Staged upgrade (not applied)";
                        f.Add(new Finding(Severity.Warning, "Pending upgrade",
                            "'" + holding.Solution + "' exists: the import was staged for upgrade but the upgrade was never applied. " +
                            "Items removed in Dev are not removed in Target until it is.",
                            "In Target open Solutions > '" + sol.FriendlyName + "' > Apply Upgrade."));
                        if (mine == null) mine = holding;
                    }
                    if (mine != null && mine.Note == null) mine.Note = "Your solution";

                    if (mine == null)
                    {
                        f.Add(new Finding(Severity.Problem, "Your solution's layer",
                            "'" + sol.UniqueName + "' has no layer on this " + lower + " in Target, so its import did not change it.",
                            "Make sure it is in the solution in Dev, then export and import a new version."));
                    }
                    else
                    {
                        var above = d.Layers.TakeWhile(l => l != mine).Where(l => l != active && l.Note == null).ToList();
                        foreach (var a in above) a.Note = "Above yours";
                        if (above.Count > 0)
                            f.Add(new Finding(Severity.Warning, "Other managed solutions above yours",
                                "Installed above '" + sol.UniqueName + "': " + string.Join(", ", above.Select(a => a.Solution)) +
                                (item.Kind == ComponentKind.Form
                                    ? ". Forms merge layers, so these solutions win for every part of the form they also change."
                                    : ". The top layer wins, so their version of this " + lower + " is what users get."),
                                "Make the change in the top solution, or remove this " + lower + " from those solutions. Compare their layers in the maker portal (Solution layers)."));
                        else
                            f.Add(new Finding(Severity.Ok, "Other managed solutions above yours", "'" + sol.UniqueName + "' is the top managed layer."));
                    }
                }

                if (active != null)
                {
                    f.Add(new Finding(Severity.Problem, "Unmanaged layer in Target",
                        "Someone customized this " + lower + " directly in Target" + (active.Written.HasValue ? " (" + active.Written.Value.ToString("yyyy-MM-dd HH:mm") + ")" : "") +
                        ". The unmanaged (Active) layer sits on top of every managed layer and overrides " +
                        (item.Kind == ComponentKind.Form ? "the parts it changed" : "what your solution deployed") +
                        (string.IsNullOrEmpty(active.Changes) ? "." : ": " + TextDiff.Shorten(active.Changes, 300)),
                        "If the Target change is wanted, make it in Dev first. Then remove the unmanaged layer in Target.",
                        new FixAction
                        {
                            Label = "Remove unmanaged layer",
                            OnDev = false,
                            WhatItDoes = "Remove the unmanaged (Active) customizations of " + lower + " '" + item.Name + "' in Target (RemoveActiveCustomizations)" +
                                         (h.Publishable ? ", then publish " + h.PublishScope(item) : "") +
                                         ". It falls back to the managed layers. This cannot be undone.",
                            Run = s =>
                            {
                                s.Execute(new OrganizationRequest("RemoveActiveCustomizations")
                                {
                                    Parameters = { { "SolutionComponentName", h.LayerName }, { "ComponentId", item.Id } }
                                });
                                if (h.Publishable) h.Publish(s, item);
                            },
                            Undo = "No. The unmanaged changes are deleted. To get them back someone has to make them again by hand.",
                            Advice = "Look at what the unmanaged layer changed first (maker portal > See solution layers > Active layer, or the Differences tab here). " +
                                     "If those Target changes are wanted, make them in Dev and deploy BEFORE removing the layer, otherwise they are lost. " +
                                     "Users see the change immediately, so prefer a quiet time and tell the team.",
                            ManualSteps = "1. Open make.powerapps.com and switch to the Target environment.\r\n" +
                                          "2. Solutions > " + (sol == null ? "your solution" : "'" + sol.FriendlyName + "'") + " > find the " + lower + " '" + item.Name + "'.\r\n" +
                                          "3. Click ... > Advanced > See solution layers.\r\n" +
                                          "4. Select the 'Active' layer, review its changes, then click 'Remove active customizations'.\r\n" +
                                          (h.Publishable ? "5. Publish all customizations." : "")
                        }));
                }
                else
                {
                    f.Add(new Finding(Severity.Ok, "Unmanaged layer in Target", "No unmanaged (Active) layer on this " + lower + "."));
                }
            });

            // 6. Content
            if (devSnap != null)
            {
                d.Differences = h.Compare(devSnap, tgt, "Dev", "Target");
                d.TargetMatchesDev = d.Differences.Count == 0;

                if (mine != null && !string.IsNullOrEmpty(mine.ComponentJson))
                {
                    var layer = h.FromLayer(mine.ComponentJson);
                    layer.Record = tgt.Record;   // type info (web resource type, process category) for compare/display
                    if (h.LayerContentUsable(layer))
                    {
                        d.LayerContent = h.Pretty(layer);
                        d.LayerDifferences = h.Compare(devSnap, layer, "Dev", "your layer");
                        if (d.LayerDifferences.Count == 0)
                            f.Add(new Finding(Severity.Ok, "Package content",
                                "Your solution's layer in Target has Dev's current version, so the import itself was fine."));
                        else if (d.TargetMatchesDev)
                            // The content stored in a managed layer is not always the full published component
                            // (seen for forms in a real env), so it only matters when the published version differs too.
                            f.Add(new Finding(Severity.Info, "Package content",
                                "The content stored in your solution's layer differs from Dev in " + d.LayerDifferences.Count + " place(s), but Target's published " + lower +
                                " matches Dev, so this can be ignored (Dataverse doesn't always store the full component in a managed layer)."));
                        else
                            f.Add(new Finding(Severity.Problem, "Package content",
                                "Your solution's layer in Target differs from Dev in " + d.LayerDifferences.Count + " place(s): the imported package probably does not contain Dev's current version " +
                                "(older zip, changes made after export, or export before publish).",
                                "Publish in Dev, bump the solution version, export managed again and import it."));
                    }
                }

                // A newer version in Dev doesn't matter for this component when it is already the same
                var version = f.FirstOrDefault(x => x.Check == "Solution version" && x.Severity == Severity.Warning);
                if (version != null && d.TargetMatchesDev)
                {
                    version.Severity = Severity.Info;
                    version.Result += " This " + lower + " is already the same in both, so it is not affected.";
                    version.HowToFix = null;
                }

                if (d.TargetMatchesDev)
                {
                    f.Add(new Finding(Severity.Ok, "Target vs Dev",
                        "Target's published " + lower + " is identical to Dev. The deployment worked. " + h.WhenIdentical));
                }
                else
                {
                    f.Add(new Finding(Severity.Problem, "Target vs Dev",
                        "Target's published " + lower + " differs from Dev in " + d.Differences.Count + " place(s). See 'Differences: Dev vs Target'; the other findings explain why.",
                        "Fix the cause above, then deploy again."));

                    var onlyInTarget = d.Differences.Count(x => x.StartsWith("Only in Target"));
                    if (onlyInTarget > 0 && active == null && item.Kind != ComponentKind.WebResource)
                        f.Add(new Finding(Severity.Warning, "Removed items still in Target",
                            onlyInTarget + " item(s) exist in Target but not in Dev. Imports done as 'Update' (or patches) never delete anything.",
                            "Import the solution as 'Upgrade' (the default), or apply a staged upgrade."));
                }
            }

            // 7. Unpublished changes in target
            if (h.Publishable && h.UnpublishedDiffers(tgt))
                f.Add(new Finding(Severity.Warning, "Unpublished changes in Target",
                    "Someone edited this " + lower + " in Target without publishing. When published it becomes (or changes) an unmanaged layer.",
                    "Don't publish it - make the change in Dev instead. Remove the unmanaged layer if one exists."));

            // 8. Type-specific checks
            var ctx = new CheckContext { Target = target, Dev = dev, Item = item, TargetSnap = tgt, DevSnap = devSnap, Diagnosis = d };
            Try(f, label + " checks", () => h.TypeChecks(ctx));

            Finish(d, h);
            return d;
        }

        private static void Finish(Diagnosis d, ComponentHandler h)
        {
            // Stable sort: problems first, keeping the order the checks ran in (most fundamental cause first)
            d.Findings = d.Findings.OrderBy(x => x.Severity).ToList();
            var first = d.Findings.FirstOrDefault(x => x.Severity == Severity.Problem);
            var warnings = d.Findings.Count(x => x.Severity == Severity.Warning);
            var lower = h.Label.ToLowerInvariant();
            d.Verdict = first != null
                ? "Most likely cause: " + first.Check + ". " + first.Result
                : d.TargetMatchesDev
                    ? "Deployment worked: Target's published " + lower + " is identical to Dev." +
                      (warnings > 0 ? " Check the " + warnings + " warning(s) below for why users might still see something else." : "")
                : warnings > 0
                    ? "No definite cause found. Review the " + warnings + " warning(s) below."
                    : "No problem found: nothing overrides your solution in Target.";

            var step = first ?? d.Findings.FirstOrDefault(x => x.Severity == Severity.Warning && !string.IsNullOrEmpty(x.HowToFix));
            d.NextStep = step != null
                ? step.HowToFix + (step.Action != null ? " (or click '" + step.Action.Label + "')" : "")
                : h.WhenIdentical;
        }

        // ------------------------------------------------------------ Helpers

        private static Membership GetMembership(IOrganizationService svc, Guid solutionId, ComponentItem item, ComponentHandler h)
        {
            var q = new QueryExpression("solutioncomponent") { ColumnSet = new ColumnSet("componenttype") };
            q.Criteria.AddCondition("solutionid", ConditionOperator.Equal, solutionId);
            q.Criteria.AddCondition("objectid", ConditionOperator.Equal, item.Id);
            q.Criteria.AddCondition("componenttype", ConditionOperator.Equal, h.ComponentType);
            if (svc.RetrieveMultiple(q).Entities.Count > 0) return Membership.Direct;
            if (!h.IncludedWithTable(item)) return Membership.None;

            var meta = ((RetrieveEntityResponse)svc.Execute(new RetrieveEntityRequest { LogicalName = item.Group, EntityFilters = EntityFilters.Entity })).EntityMetadata;
            var t = new QueryExpression("solutioncomponent") { ColumnSet = new ColumnSet("rootcomponentbehavior") };
            t.Criteria.AddCondition("solutionid", ConditionOperator.Equal, solutionId);
            t.Criteria.AddCondition("objectid", ConditionOperator.Equal, meta.MetadataId.Value);
            t.Criteria.AddCondition("componenttype", ConditionOperator.Equal, TableComponentType);
            var row = svc.RetrieveMultiple(t).Entities.FirstOrDefault();
            if (row == null) return Membership.None;
            return ComponentHandler.OptionValue(row, "rootcomponentbehavior", 0) == 0 ? Membership.ViaTable : Membership.TableWithoutIt;
        }

        private static List<LayerRow> GetLayers(IOrganizationService svc, Guid id, string layerName)
        {
            var q = new QueryExpression("msdyn_componentlayer")
            {
                ColumnSet = new ColumnSet("msdyn_solutionname", "msdyn_publishername", "msdyn_overwritetime", "msdyn_order", "msdyn_changes", "msdyn_componentjson")
            };
            q.Criteria.AddCondition("msdyn_componentid", ConditionOperator.Equal, id.ToString());
            q.Criteria.AddCondition("msdyn_solutioncomponentname", ConditionOperator.Equal, layerName);
            q.AddOrder("msdyn_order", OrderType.Descending);

            return svc.RetrieveMultiple(q).Entities.Select(e =>
            {
                var time = e.GetAttributeValue<DateTime?>("msdyn_overwritetime");
                return new LayerRow
                {
                    Order = e.GetAttributeValue<int>("msdyn_order"),
                    Solution = e.GetAttributeValue<string>("msdyn_solutionname"),
                    Publisher = e.GetAttributeValue<string>("msdyn_publishername"),
                    // Dataverse returns 1900-01-01 when it did not record a time
                    Written = time.HasValue && time.Value.Year > 1900 ? time : null,
                    Changes = e.GetAttributeValue<string>("msdyn_changes"),
                    ComponentJson = e.GetAttributeValue<string>("msdyn_componentjson")
                };
            }).ToList();
        }

        /// <summary>Who this connection acts as (shown before any fix runs).</summary>
        public static CallerInfo GetCaller(IOrganizationService svc)
        {
            var who = (WhoAmIResponse)svc.Execute(new WhoAmIRequest());
            var u = svc.Retrieve("systemuser", who.UserId, new ColumnSet("fullname", "domainname", "internalemailaddress", "applicationid"));
            var login = u.GetAttributeValue<string>("domainname");
            if (string.IsNullOrEmpty(login)) login = u.GetAttributeValue<string>("internalemailaddress");
            return new CallerInfo
            {
                FullName = u.GetAttributeValue<string>("fullname"),
                Login = login,
                IsApplicationUser = u.GetAttributeValue<Guid?>("applicationid").HasValue
            };
        }

        /// <summary>Where to find the component in the maker portal's "Add existing" menu.</summary>
        private static string AddExistingPath(ComponentItem item)
        {
            switch (item.Kind)
            {
                case ComponentKind.Form: return "Table > '" + item.Group + "' > Select components > Forms";
                case ComponentKind.View: return "Table > '" + item.Group + "' > Select components > Views";
                case ComponentKind.WebResource: return "More > Web resource";
                default: return item.SubTypeCode == 5 ? "Automation > Cloud flow" : "Automation > Process";
            }
        }

        /// <summary>Run one check; a failure becomes a warning instead of stopping the whole diagnosis.</summary>
        public static void Try(List<Finding> f, string check, Action action)
        {
            try { action(); }
            catch (Exception ex) { f.Add(new Finding(Severity.Warning, check, "Check could not run: " + ex.Message)); }
        }

        private static bool SameName(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
    }
}
