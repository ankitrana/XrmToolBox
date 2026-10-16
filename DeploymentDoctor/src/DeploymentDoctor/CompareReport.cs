using System;
using System.Linq;
using System.Net;
using System.Text;

namespace DeploymentDoctor
{
    /// <summary>Text, CSV and HTML versions of a solution compare. Environment names only (no URLs) and no component content.</summary>
    internal static class CompareReport
    {
        private static string Plain(CompareResult r, string dev, string target)
        {
            return "Dev: " + dev + "   Target: " + target;
        }

        private static string Headline(CompareResult r)
        {
            return r.NotMatching == 0
                ? "All " + r.Rows.Count + " components are the same in Dev and Target."
                : r.NotMatching + " of " + r.Rows.Count + " components need attention: " +
                  string.Join(", ", new[]
                  {
                      Part(r, CompareStatus.Different, "different"), Part(r, CompareStatus.MissingInTarget, "missing in Target"),
                      Part(r, CompareStatus.StateDiffers, "state differs"), Part(r, CompareStatus.NotPublishedInDev, "not published in Dev"),
                      Part(r, CompareStatus.OnlyInTarget, "only in Target"), Part(r, CompareStatus.CouldNotCheck, "could not be checked")
                  }.Where(x => x != null)) + ".";
        }

        private static string Part(CompareResult r, CompareStatus s, string label)
        {
            var n = r.Count(s);
            return n == 0 ? null : n + " " + label;
        }

        // ---------------------------------------------------------------- text

        public static string Text(CompareResult r, string dev, string target)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Solution compare: " + r.SolutionName);
            sb.AppendLine(Plain(r, dev, target));
            sb.AppendLine("Solution version: Dev " + r.DevVersion + ", Target " + r.TargetVersion);
            sb.AppendLine("Checked on " + r.RanOn.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine();
            sb.AppendLine(Headline(r));
            if (r.NotCompared.Count > 0) sb.AppendLine("Not compared yet: " + string.Join(", ", r.NotCompared));
            sb.AppendLine();
            foreach (var row in r.Rows.Where(x => x.Status != CompareStatus.Same))
            {
                sb.AppendLine("[" + row.StatusText + "] " + row.TypeName + ": " + row.Item.Name + (string.IsNullOrEmpty(row.TableText) ? "" : " (" + row.TableText + ")"));
                foreach (var l in row.Lines) sb.AppendLine("    " + l.Replace("\r\n", "\r\n    "));
            }
            var same = r.Rows.Where(x => x.Status == CompareStatus.Same).ToList();
            if (same.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Same in Dev and Target (" + same.Count + "):");
                foreach (var row in same) sb.AppendLine("  " + row.TypeName + ": " + row.Item.Name);
            }
            return sb.ToString();
        }

        // ----------------------------------------------------------------- CSV

        private static string Q(string s) { return "\"" + (s ?? "").Replace("\"", "\"\"") + "\""; }

        public static string Csv(CompareResult r, string dev, string target)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Status,Type,Component,Table,Dev state,Target state,What differs");
            foreach (var row in r.Rows)
                sb.AppendLine(string.Join(",", new[]
                {
                    Q(row.StatusText), Q(row.TypeName), Q(row.Item.Name), Q(row.TableText), Q(row.DevState), Q(row.TargetState),
                    Q(string.Join(" | ", row.Lines.Where(l => row.Status != CompareStatus.Same).Select(l => l.Replace("\r\n", " ").Trim())))
                }));
            return sb.ToString();
        }

        // ---------------------------------------------------------------- HTML

        private static string H(string s) { return WebUtility.HtmlEncode(s ?? ""); }

        public static string Html(CompareResult r, string dev, string target, string toolVersion)
        {
            var sb = new StringBuilder();
            sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Solution compare - ").Append(H(r.SolutionName)).Append("</title><style>")
              .Append("body{font-family:Segoe UI,Arial,sans-serif;margin:24px;color:#1f2933;background:#f3f5f7}")
              .Append("h1{font-size:22px;margin:0 0 4px}h2{font-size:16px;margin:24px 0 8px}")
              .Append(".meta{color:#5a6472;margin-bottom:12px}")
              .Append(".head{background:#fff;border:1px solid #dae0e6;border-left:6px solid #2e7d32;padding:14px 18px;margin:12px 0}")
              .Append(".head.bad{border-left-color:#c62828}.head.warn{border-left-color:#bf7800}")
              .Append("table{border-collapse:collapse;width:100%;background:#fff}th,td{border:1px solid #dae0e6;padding:6px 10px;text-align:left;vertical-align:top;font-size:13px}")
              .Append("th{background:#ecf0f4}.s{font-weight:600;white-space:nowrap}.Problem{color:#c62828}.Warning{color:#bf7800}.Ok{color:#2e7d32}")
              .Append("pre{white-space:pre-wrap;margin:0;font-family:Segoe UI,Arial,sans-serif;font-size:13px;color:#33404d}")
              .Append("</style></head><body>");
            sb.Append("<h1>Solution compare: ").Append(H(r.SolutionName)).Append("</h1>");
            sb.Append("<div class=\"meta\">Dev: ").Append(H(dev)).Append(" &nbsp;&rarr;&nbsp; Target: ").Append(H(target))
              .Append(" &nbsp;|&nbsp; Solution version: Dev ").Append(H(r.DevVersion)).Append(", Target ").Append(H(r.TargetVersion))
              .Append(" &nbsp;|&nbsp; Checked ").Append(r.RanOn.ToString("yyyy-MM-dd HH:mm")).Append(" with Deployment Doctor ").Append(H(toolVersion)).Append("</div>");

            var cls = r.NotMatching == 0 ? "" : (r.Count(CompareStatus.Different) + r.Count(CompareStatus.MissingInTarget) > 0 ? " bad" : " warn");
            sb.Append("<div class=\"head").Append(cls).Append("\"><b>").Append(H(Headline(r))).Append("</b>");
            if (r.NotCompared.Count > 0) sb.Append("<div class=\"meta\">Not compared yet: ").Append(H(string.Join(", ", r.NotCompared))).Append("</div>");
            sb.Append("</div>");

            sb.Append("<h2>All components</h2><table><tr><th>Status</th><th>Type</th><th>Component</th><th>Table</th><th>Dev</th><th>Target</th><th>What differs</th></tr>");
            foreach (var row in r.Rows)
            {
                sb.Append("<tr><td class=\"s ").Append(row.Severity).Append("\">").Append(Look.GlyphFor(row.Severity)).Append(" ").Append(H(row.StatusText)).Append("</td><td>")
                  .Append(H(row.TypeName)).Append("</td><td>").Append(H(row.Item.Name)).Append("</td><td>").Append(H(row.TableText)).Append("</td><td>")
                  .Append(H(row.DevState)).Append("</td><td>").Append(H(row.TargetState)).Append("</td><td>");
                if (row.Status != CompareStatus.Same)
                    sb.Append("<pre>").Append(H(string.Join("\r\n", row.Lines))).Append("</pre>");
                sb.Append("</td></tr>");
            }
            sb.Append("</table></body></html>");
            return sb.ToString();
        }
    }
}
