using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DeploymentDoctor
{
    internal enum CompareStatus { Same, Different, MissingInTarget, StateDiffers, NotPublishedInDev, OnlyInTarget, CouldNotCheck }

    /// <summary>One component of the solution and what was found when Dev and Target were compared.</summary>
    internal class CompareRow
    {
        public ComponentItem Item;
        public string TypeName;
        public CompareStatus Status;
        public string Summary;                  // one line for the table
        public List<string> Lines = new List<string>();     // everything, for the details pane and reports
        public string DevState, TargetState;

        public Severity Severity
        {
            get
            {
                switch (Status)
                {
                    case CompareStatus.Same: return Severity.Ok;
                    case CompareStatus.Different:
                    case CompareStatus.MissingInTarget: return Severity.Problem;
                    default: return Severity.Warning;
                }
            }
        }

        public string StatusText
        {
            get
            {
                switch (Status)
                {
                    case CompareStatus.Same: return "Same";
                    case CompareStatus.Different: return "Different";
                    case CompareStatus.MissingInTarget: return "Missing in Target";
                    case CompareStatus.StateDiffers: return "State differs";
                    case CompareStatus.NotPublishedInDev: return "Not published in Dev";
                    case CompareStatus.OnlyInTarget: return "Only in Target";
                    default: return "Could not check";
                }
            }
        }

        public string TableText
        {
            get
            {
                var i = Item;
                if (i.Group == ProcessHandler.NoTable) return i.Kind == ComponentKind.Ribbon ? "(application ribbon)" : "";
                if (i.Group == SiteMapHandler.Everything) return "";
                return i.Group;
            }
        }
    }

    internal class CompareResult
    {
        public string SolutionName;
        public string DevVersion, TargetVersion;
        public bool DevManaged;
        public List<CompareRow> Rows = new List<CompareRow>();
        public List<string> NotCompared = new List<string>();      // component types the tool does not compare yet
        public DateTime RanOn = DateTime.Now;

        public int Count(CompareStatus s) { return Rows.Count(r => r.Status == s); }
        public int NotMatching { get { return Rows.Count(r => r.Status != CompareStatus.Same); } }
    }

    /// <summary>
    /// "Did everything in this solution arrive in Target the same as in Dev?" Reads every component of the solution in Dev,
    /// finds it in Target and compares content and state with the same comparers the single-item doctor uses. Read-only.
    /// </summary>
    internal static class SolutionCompare
    {
        public static CompareResult Run(IOrganizationService dev, IOrganizationService target, SolutionItem targetSolution, Action<string> progress)
        {
            var devSol = DoctorService.FindSolution(dev, targetSolution.UniqueName);
            if (devSol == null)
                throw new InvalidOperationException("The solution '" + targetSolution.UniqueName + "' was not found in Dev. Connect the Dev environment the solution was exported from.");

            var coveredIds = new HashSet<Guid>();
            var result = new CompareResult
            {
                SolutionName = targetSolution.FriendlyName + " (" + targetSolution.UniqueName + ")",
                DevVersion = devSol.Version,
                TargetVersion = targetSolution.Version,
                DevManaged = devSol.IsManaged
            };

            progress("Reading tables...");
            var devTables = DoctorService.LoadTables(dev);
            var targetTables = DoctorService.LoadTables(target);

            foreach (var h in Handlers.All)
            {
                var name = h.DisplayName.ToLowerInvariant();
                progress("Reading " + name + "s in the solution...");
                List<ComponentItem> devItems, inSolution;
                HashSet<Guid> devIds;
                try
                {
                    devItems = h.LoadAll(dev);
                    devIds = DoctorService.IdsInSolution(dev, devSol.Id, h, devItems, devTables);
                    inSolution = devItems.Where(i => devIds.Contains(i.Id)).OrderBy(i => i.Name).ToList();
                    foreach (var id in devIds) coveredIds.Add(id);
                }
                catch (Exception ex)
                {
                    // One type failing (permissions, an older environment) must not stop the rest of the compare
                    result.Rows.Add(Failed(h, new ComponentItem { Kind = h.Kind, Name = "(all " + name + "s)", Group = "" }, "Could not read the " + name + "s in Dev: " + ex.Message));
                    continue;
                }

                int n = 0;
                foreach (var item in inSolution)
                {
                    n++;
                    progress("Comparing " + name + " " + n + " of " + inSolution.Count + ": " + item.Name);
                    result.Rows.Add(CompareOne(h, dev, target, item));
                }

                // Things still in Target's copy of the solution that Dev no longer has
                try
                {
                    var targetItems = h.LoadAll(target);
                    var targetIds = DoctorService.IdsInSolution(target, targetSolution.Id, h, targetItems, targetTables);
                    foreach (var extra in targetItems.Where(i => targetIds.Contains(i.Id) && !devIds.Contains(i.Id)).OrderBy(i => i.Name))
                    {
                        var row = new CompareRow
                        {
                            Item = extra,
                            TypeName = h.DisplayName,
                            Status = CompareStatus.OnlyInTarget,
                            Summary = "In the solution in Target, not in Dev"
                        };
                        row.Lines.Add("This " + h.Label.ToLowerInvariant() + " is part of the solution in Target but not in Dev. It was probably removed in Dev (or belongs to an older version).");
                        row.Lines.Add("Imports done as 'Update' (or patches) never delete anything. Import the solution as 'Upgrade' or apply the staged upgrade to remove it.");
                        result.Rows.Add(row);
                    }
                }
                catch (Exception ex)
                {
                    result.Rows.Add(Failed(h, new ComponentItem { Kind = h.Kind, Name = "(components only in Target)", Group = "" }, "Could not look for " + name + "s that exist only in Target: " + ex.Message));
                }
            }

            progress("Counting the other components of the solution...");
            try { result.NotCompared = OtherComponents(dev, devSol.Id, coveredIds); }
            catch (Exception) { /* a convenience line only */ }

            result.Rows = result.Rows
                .OrderBy(r => r.Severity == Severity.Problem ? 0 : r.Severity == Severity.Warning ? 1 : 2)
                .ThenBy(r => r.TypeName).ThenBy(r => r.Item.Name).ToList();
            return result;
        }

        private static CompareRow Failed(ComponentHandler h, ComponentItem item, string message)
        {
            var row = new CompareRow { Item = item, TypeName = h.DisplayName, Status = CompareStatus.CouldNotCheck, Summary = message };
            row.Lines.Add(message);
            return row;
        }

        private static CompareRow CompareOne(ComponentHandler h, IOrganizationService dev, IOrganizationService target, ComponentItem item)
        {
            var row = new CompareRow { Item = item, TypeName = h.DisplayName };
            try
            {
                var tgt = h.Load(target, item.Id);
                if (tgt == null)
                {
                    row.Status = CompareStatus.MissingInTarget;
                    row.Summary = "Not found in Target";
                    row.Lines.Add("This " + h.Label.ToLowerInvariant() + " is in the solution in Dev but does not exist in Target: the import did not deliver it.");
                    row.Lines.Add("Check it was in the exported version, that the import succeeded, and that the right solution was imported.");
                    return row;
                }

                var dv = h.Load(dev, item.Id);
                var diffs = dv == null ? new List<string>() : h.Compare(dv, tgt, "Dev", "Target");
                row.DevState = dv == null ? null : h.State(dev, dv);
                row.TargetState = h.State(target, tgt);
                bool stateDiffers = row.DevState != null && row.TargetState != null && row.DevState != row.TargetState;
                bool badState = !stateDiffers && row.TargetState != null && h.IsProblemState(row.TargetState);
                bool unpublished = h.Publishable && dv != null && h.UnpublishedDiffers(dv);

                if (diffs.Count > 0)
                {
                    row.Status = CompareStatus.Different;
                    row.Summary = diffs.Count + " difference" + (diffs.Count == 1 ? "" : "s") + ": " + FirstLine(diffs[0]);
                    row.Lines.Add("Target's published " + h.Label.ToLowerInvariant() + " differs from Dev in " + diffs.Count + " place(s):");
                    foreach (var d in diffs) row.Lines.Add("  •  " + d.Replace("\r\n", "\r\n     "));
                }
                else if (stateDiffers || badState)
                {
                    row.Status = CompareStatus.StateDiffers;
                    row.Summary = stateDiffers ? "Dev: " + row.DevState + ", Target: " + row.TargetState : "Target: " + row.TargetState;
                }
                else if (unpublished)
                {
                    row.Status = CompareStatus.NotPublishedInDev;
                    row.Summary = "Dev has unpublished changes";
                }
                else
                {
                    row.Status = CompareStatus.Same;
                    row.Summary = "";
                    row.Lines.Add("Content and state are the same in Dev and Target." + (row.DevState == null ? "" : " State: " + row.DevState + "."));
                    return row;
                }

                if (badState)
                    row.Lines.Add("Content is the same, but in Target the state is '" + row.TargetState + "', which stops it from working until it is fixed.");
                if (stateDiffers)
                    row.Lines.Add((diffs.Count > 0 ? "" : "Content is the same, but ") + "state differs: Dev is '" + row.DevState + "', Target is '" + row.TargetState + "'." +
                                  (h.Kind == ComponentKind.Process ? " A flow or process that is off in Target does not run." : ""));
                if (unpublished)
                    row.Lines.Add("Dev has unpublished changes on this " + h.Label.ToLowerInvariant() + ": an export contains the published version, so those changes were not deployed.");

                // Why might Target differ? Changes made directly in Target are the usual reason.
                try
                {
                    var active = DoctorService.GetLayers(target, item.Id, h.LayerName).FirstOrDefault(l => l.Solution == "Active");
                    if (active != null)
                        row.Lines.Add("Target has an unmanaged layer" + (active.Written.HasValue ? " (" + active.Written.Value.ToString("yyyy-MM-dd HH:mm") + ")" : "") +
                                      ": a change made directly in Target overrides the solution." + (string.IsNullOrEmpty(active.Changes) ? "" : " Parts changed: " + TextDiff.Shorten(active.Changes, 200) + "."));
                }
                catch (Exception) { /* hint only */ }

                row.Lines.Add("Use 'Diagnose this component' for the full explanation and the fix.");
            }
            catch (Exception ex)
            {
                return Failed(h, item, "Could not check: " + ex.Message);
            }
            return row;
        }

        private static string FirstLine(string text)
        {
            var i = text.IndexOf('\n');
            var line = (i < 0 ? text : text.Substring(0, i)).Trim();
            return TextDiff.Shorten(line, 90);
        }

        // ---------------------------------------------------- what is not compared yet

        private static readonly Dictionary<int, string> TypeNames = new Dictionary<int, string>
        {
            { 1, "table" }, { 2, "column" }, { 9, "choice" }, { 10, "relationship" }, { 14, "table key" }, { 20, "security role" },
            { 21, "field security profile" }, { 31, "report" }, { 36, "email template" }, { 44, "duplicate detection rule" }, { 59, "chart" },
            { 63, "connection role" }, { 66, "custom control" }, { 68, "custom control default config" }, { 80, "model-driven app" },
            { 90, "plugin type" }, { 91, "plugin assembly" }, { 92, "plugin step" }, { 93, "plugin step image" }, { 95, "service endpoint" },
            { 150, "routing rule" }, { 300, "canvas app" }, { 371, "connector" }, { 372, "connector" }, { 380, "environment variable" },
            { 381, "environment variable value" }, { 10026, "app setting" }
        };

        /// <summary>Component types in the solution that this tool does not compare yet, as "12 tables" style text.</summary>
        private static List<string> OtherComponents(IOrganizationService dev, Guid solutionId, HashSet<Guid> coveredIds)
        {
            var covered = new HashSet<int>(Handlers.All.Select(h => h.ComponentType).Where(t => t >= 0));
            var q = new QueryExpression("solutioncomponent") { ColumnSet = new ColumnSet("componenttype", "objectid") };
            q.Criteria.AddCondition("solutionid", ConditionOperator.Equal, solutionId);
            var counts = ComponentHandler.All(dev, q)
                .Where(e => !coveredIds.Contains(e.GetAttributeValue<Guid>("objectid")))
                .Select(e => ComponentHandler.OptionValue(e, "componenttype"))
                .Where(t => t >= 0 && !covered.Contains(t))
                .GroupBy(t => t).OrderByDescending(g => g.Count()).ToList();

            return counts.Select(g =>
            {
                string name;
                if (!TypeNames.TryGetValue(g.Key, out name)) name = "component of type " + g.Key;
                return g.Count() + " " + name + (g.Count() == 1 ? "" : "s");
            }).ToList();
        }
    }
}
