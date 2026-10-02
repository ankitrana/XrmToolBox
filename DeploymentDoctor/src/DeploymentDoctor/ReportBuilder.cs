using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace DeploymentDoctor
{
    /// <summary>What the report was run against (shown in the report header).</summary>
    internal class ReportContext
    {
        public string KindLabel;          // "Form", "View", "Web resource", "Process"
        public string ComponentName;
        public string Table;
        public string SubType;
        public Guid ComponentId;
        public string GroupLabel;         // "Table" or "Prefix"
        public string Solution;
        public string TargetName;
        public string TargetUrl;
        public string DevName;
        public string DevUrl;
        public DateTime Generated;
        public string ToolVersion;

        // What the person exporting chose to share (both off by default: see ExportOptionsDialog)
        public bool IncludeXml;
        public bool IncludeUrls;
    }

    /// <summary>Builds shareable reports (text, CSV, HTML) from a diagnosis.</summary>
    internal static class ReportBuilder
    {
        public const string ShareNotice =
            "Contains configuration metadata from your environments (no record data, no credentials). Share only with people allowed to see it.";

        /// <summary>Problems and warnings with a fix, in the order they should be handled.</summary>
        public static List<Finding> ActionPlan(Diagnosis d)
        {
            return d.Findings
                .Where(f => (f.Severity == Severity.Problem || f.Severity == Severity.Warning) && !string.IsNullOrEmpty(f.HowToFix))
                .ToList();
        }

        /// <summary>Dev vs Target, plus Dev vs your solution's layer when that was compared.</summary>
        public static string DifferencesText(Diagnosis d)
        {
            if (d.DevContent == null) return "Dev was not connected, so no comparison.";
            var sb = new StringBuilder();
            sb.AppendLine("Dev vs Target (published version):");
            if (d.Differences.Count == 0) sb.AppendLine("  No differences.");
            foreach (var line in d.Differences) sb.AppendLine("  " + line);
            if (d.LayerDifferences.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Dev vs the content stored in your solution's layer in Target:");
                foreach (var line in d.LayerDifferences) sb.AppendLine("  " + line);
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- Text

        public static string Text(Diagnosis d, ReportContext c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Deployment Doctor report");
            sb.AppendLine("Note: " + ShareNotice);
            sb.AppendLine();
            sb.AppendLine(c.KindLabel + ": " + c.ComponentName + " (" + c.SubType + ", " + c.GroupLabel.ToLowerInvariant() + " " + c.Table + ")");
            sb.AppendLine("Solution: " + c.Solution);
            sb.AppendLine("Target:   " + c.TargetName + "   Dev: " + (c.DevName ?? "(not connected)"));
            sb.AppendLine("Run on:   " + c.Generated.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine();
            sb.AppendLine(d.Verdict);
            if (!string.IsNullOrEmpty(d.NextStep)) sb.AppendLine("Next step: " + d.NextStep);

            var plan = ActionPlan(d);
            if (plan.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("What to do next:");
                for (int i = 0; i < plan.Count; i++)
                    sb.AppendLine(string.Format("  {0}. [{1}] {2}", i + 1, plan[i].Check, plan[i].HowToFix));
            }

            sb.AppendLine();
            sb.AppendLine("All checks:");
            foreach (var f in d.Findings)
                sb.AppendLine(string.Format("  [{0}] {1}: {2}", f.Severity, f.Check, f.Result));

            sb.AppendLine();
            sb.AppendLine("Solution layers in Target (top first): " + string.Join(" > ", d.Layers.Select(l => l.Solution)));
            sb.AppendLine();
            sb.Append(DifferencesText(d));
            return sb.ToString();
        }

        // ----------------------------------------------------------------- CSV

        public static string Csv(Diagnosis d, ReportContext c)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Type,Name,Table,Solution,Target,Dev,RunOn,Result,Check,WhatWasFound,HowToFix");
            foreach (var f in d.Findings)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Q(c.KindLabel + " - " + c.SubType), Q(c.ComponentName), Q(c.Table), Q(c.Solution), Q(c.TargetName), Q(c.DevName), Q(c.Generated.ToString("yyyy-MM-dd HH:mm")),
                    Q(f.Severity.ToString()), Q(f.Check), Q(f.Result), Q(f.HowToFix)
                }));
            }
            return sb.ToString();
        }

        private static string Q(string s)
        {
            return s == null ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        // ---------------------------------------------------------------- HTML

        public static string Html(Diagnosis d, ReportContext c)
        {
            var worst = d.Findings.Select(x => x.Severity).DefaultIfEmpty(Severity.Ok).Min();
            var sb = new StringBuilder();
            sb.Append(@"<!doctype html>
<html lang=""en""><head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Deployment Doctor - ").Append(H(c.ComponentName)).Append(@"</title>
<style>
  :root { --problem:#fde2e1; --problem-b:#c62828; --warning:#fff6d6; --warning-b:#b7791f; --ok:#e6f4ea; --ok-b:#2e7d32; --info:#eef2f7; --info-b:#5f6b7a; }
  body { font-family: 'Segoe UI', Arial, sans-serif; color:#1f2328; background:#f6f8fa; margin:0; padding:24px; }
  @media (max-width:600px) { body { padding:16px; } table.grid th, table.grid td { padding:6px; } }
  .scroll { overflow-x:auto; }
  .wrap { max-width:1100px; margin:0 auto; }
  h1 { font-size:22px; margin:0 0 4px; }
  h2 { font-size:16px; margin:28px 0 8px; }
  .sub { color:#57606a; margin-bottom:16px; }
  .card { background:#fff; border:1px solid #d0d7de; border-radius:8px; padding:16px; }
  .meta td { padding:3px 16px 3px 0; vertical-align:top; overflow-wrap:anywhere; }
  .meta td:first-child { color:#57606a; white-space:nowrap; }
  .verdict { border-left:6px solid; border-radius:8px; padding:14px 16px; margin:16px 0; font-size:15px; }
  .verdict.Problem { background:var(--problem); border-color:var(--problem-b); }
  .verdict.Warning { background:var(--warning); border-color:var(--warning-b); }
  .verdict.Ok, .verdict.Info { background:var(--ok); border-color:var(--ok-b); }
  .verdict .next { margin-top:8px; font-weight:600; }
  table.grid { width:100%; border-collapse:collapse; background:#fff; border:1px solid #d0d7de; border-radius:8px; overflow:hidden; }
  table.grid th { text-align:left; background:#f0f3f6; font-weight:600; padding:8px; border-bottom:1px solid #d0d7de; font-size:13px; }
  table.grid td { padding:8px; border-bottom:1px solid #eaeef2; vertical-align:top; font-size:13px; overflow-wrap:anywhere; }
  tr.Problem td { background:var(--problem); } tr.Warning td { background:var(--warning); } tr.Ok td { background:var(--ok); }
  .badge { display:inline-block; padding:1px 8px; border-radius:10px; font-size:12px; font-weight:600; color:#fff; }
  .badge.Problem { background:var(--problem-b); } .badge.Warning { background:var(--warning-b); } .badge.Ok { background:var(--ok-b); } .badge.Info { background:var(--info-b); }
  ol.plan li { margin:6px 0; }
  pre { background:#fff; border:1px solid #d0d7de; border-radius:8px; padding:12px; overflow:auto; font-size:12px; white-space:pre-wrap; word-break:break-word; }
  details { margin:8px 0; } summary { cursor:pointer; font-weight:600; }
  .foot { color:#57606a; font-size:12px; margin-top:28px; }
  .notice { background:var(--info); border:1px solid #c9d1d9; border-radius:8px; padding:8px 12px; font-size:13px; color:#3d4651; margin-bottom:16px; }
</style></head><body><div class=""wrap"">
<h1>Deployment Doctor report</h1>
<div class=""sub"">Why doesn't the deployed change show in the target environment?</div>
<div class=""notice"">&#128274; ").Append(H(ShareNotice)).Append(@"</div>
<div class=""card""><table class=""meta"">");
            Meta(sb, c.KindLabel, c.ComponentName + " (" + c.SubType + ")");
            Meta(sb, c.GroupLabel, c.Table);
            Meta(sb, "Id", c.ComponentId.ToString());
            Meta(sb, "Solution", c.Solution);
            Meta(sb, "Target", c.TargetName + (!c.IncludeUrls || string.IsNullOrEmpty(c.TargetUrl) ? "" : "  -  " + c.TargetUrl));
            Meta(sb, "Dev (source)", c.DevName == null ? "(not connected - Dev checks skipped)" : c.DevName + (!c.IncludeUrls || string.IsNullOrEmpty(c.DevUrl) ? "" : "  -  " + c.DevUrl));
            Meta(sb, "Run on", c.Generated.ToString("yyyy-MM-dd HH:mm"));
            sb.Append("</table></div>");

            sb.Append("<div class=\"verdict ").Append(worst).Append("\">").Append(H(d.Verdict));
            if (!string.IsNullOrEmpty(d.NextStep)) sb.Append("<div class=\"next\">Next step: ").Append(H(d.NextStep)).Append("</div>");
            sb.Append("</div>");

            var plan = ActionPlan(d);
            if (plan.Count > 0)
            {
                sb.Append("<h2>What to do next</h2><div class=\"card\"><ol class=\"plan\">");
                foreach (var f in plan)
                    sb.Append("<li><span class=\"badge ").Append(f.Severity).Append("\">").Append(f.Severity).Append("</span> <b>")
                      .Append(H(f.Check)).Append(":</b> ").Append(H(f.HowToFix)).Append("</li>");
                sb.Append("</ol></div>");
            }

            sb.Append("<h2>All checks</h2><div class=\"scroll\"><table class=\"grid\"><tr><th>Result</th><th>Check</th><th>What was found</th><th>How to fix</th></tr>");
            foreach (var f in d.Findings)
                sb.Append("<tr class=\"").Append(f.Severity).Append("\"><td><span class=\"badge ").Append(f.Severity).Append("\">").Append(f.Severity)
                  .Append("</span></td><td>").Append(H(f.Check)).Append("</td><td>").Append(H(f.Result)).Append("</td><td>")
                  .Append(f.Severity == Severity.Ok ? "" : H(f.HowToFix)).Append("</td></tr>");
            sb.Append("</table></div>");

            if (d.Layers.Count > 0)
            {
                sb.Append("<h2>Solution layers in Target (top layer first)</h2><div class=\"scroll\"><table class=\"grid\"><tr><th>Order</th><th>Solution</th><th>Publisher</th><th>Written</th><th>Note</th></tr>");
                foreach (var l in d.Layers)
                {
                    var cls = l.Note == "Unmanaged (Active)" ? "Problem" : l.Note == "Your solution" ? "Ok" : string.IsNullOrEmpty(l.Note) ? "" : "Warning";
                    sb.Append("<tr class=\"").Append(cls).Append("\"><td>").Append(l.Order).Append("</td><td>").Append(H(l.Solution)).Append("</td><td>")
                      .Append(H(l.Publisher)).Append("</td><td>").Append(l.Written.HasValue ? l.Written.Value.ToString("yyyy-MM-dd HH:mm") : "")
                      .Append("</td><td>").Append(H(l.Note)).Append("</td></tr>");
                }
                sb.Append("</table></div>");
            }

            sb.Append("<h2>Differences: Dev vs Target</h2>");
            sb.Append("<pre>").Append(H(DifferencesText(d))).Append("</pre>");

            sb.Append("<h2>Full content</h2>");
            if (c.IncludeXml)
            {
                Xml(sb, "Target (published)", d.TargetContent);
                Xml(sb, "Dev (published)", d.DevContent);
                Xml(sb, "Stored in your solution's layer in Target", d.LayerContent);
            }
            else
            {
                sb.Append("<p class=\"sub\">Not included in this report (form and view XML, script files and flow definitions can contain URLs, parameters or keys). " +
                          "The differences above already list everything that differs.</p>");
            }

            sb.Append("<div class=\"foot\">Generated by Deployment Doctor v").Append(H(c.ToolVersion))
              .Append(" for XrmToolBox, created by Ankit Rana. github.com/ankitrana/XrmToolBox</div>");
            sb.Append("</div></body></html>");
            return sb.ToString();
        }

        private static void Meta(StringBuilder sb, string label, string value)
        {
            sb.Append("<tr><td>").Append(H(label)).Append("</td><td>").Append(H(value)).Append("</td></tr>");
        }

        private static void Xml(StringBuilder sb, string title, string content)
        {
            if (string.IsNullOrEmpty(content)) return;
            sb.Append("<details><summary>").Append(H(title)).Append("</summary><pre>").Append(H(content)).Append("</pre></details>");
        }

        private static string H(string s)
        {
            return WebUtility.HtmlEncode(s ?? "");
        }
    }
}
