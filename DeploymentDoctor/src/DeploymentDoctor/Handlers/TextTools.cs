using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace DeploymentDoctor
{
    /// <summary>JSON helpers on top of JavaScriptSerializer (framework assembly: no Newtonsoft version clash with the host).</summary>
    internal static class Json
    {
        public static object Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 512 }.DeserializeObject(json); }
            catch (Exception) { return null; }
        }

        public static string Serialize(object value)
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 512 }.Serialize(value);
        }

        /// <summary>
        /// msdyn_componentjson holds a layer's attributes as Key/Value pairs (or plain properties).
        /// Returns the value for <paramref name="key"/> as a string, wherever it is.
        /// </summary>
        public static string FindValue(string json, string key)
        {
            return Walk(Parse(json), key);
        }

        private static string Walk(object node, string key)
        {
            var dict = node as IDictionary<string, object>;
            if (dict != null)
            {
                object k, v;
                if ((dict.TryGetValue("Key", out k) || dict.TryGetValue("key", out k)) &&
                    string.Equals(k as string, key, StringComparison.OrdinalIgnoreCase) &&
                    (dict.TryGetValue("Value", out v) || dict.TryGetValue("value", out v)) && v is string)
                    return (string)v;
                foreach (var kv in dict)
                {
                    if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase) && kv.Value is string) return (string)kv.Value;
                    var found = Walk(kv.Value, key);
                    if (found != null) return found;
                }
                return null;
            }
            var list = node as IEnumerable;
            if (list != null && !(node is string))
                foreach (var item in list)
                {
                    var found = Walk(item, key);
                    if (found != null) return found;
                }
            return null;
        }

        public static IDictionary<string, object> Obj(object node, string name)
        {
            var dict = node as IDictionary<string, object>;
            object v;
            return dict != null && dict.TryGetValue(name, out v) ? v as IDictionary<string, object> : null;
        }

        /// <summary>Indented JSON for display.</summary>
        public static string Pretty(string json)
        {
            var node = Parse(json);
            if (node == null) return json ?? "";
            var sb = new StringBuilder();
            Write(sb, node, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object node, int indent)
        {
            var pad = new string(' ', indent * 2);
            var dict = node as IDictionary<string, object>;
            if (dict != null)
            {
                if (dict.Count == 0) { sb.Append("{}"); return; }
                sb.Append("{\r\n");
                int i = 0;
                foreach (var kv in dict)
                {
                    sb.Append(pad).Append("  ").Append(Serialize(kv.Key)).Append(": ");
                    Write(sb, kv.Value, indent + 1);
                    sb.Append(++i < dict.Count ? ",\r\n" : "\r\n");
                }
                sb.Append(pad).Append("}");
                return;
            }
            var list = node as IEnumerable;
            if (list != null && !(node is string))
            {
                var items = list.Cast<object>().ToList();
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append("[\r\n");
                for (int i = 0; i < items.Count; i++)
                {
                    sb.Append(pad).Append("  ");
                    Write(sb, items[i], indent + 1);
                    sb.Append(i < items.Count - 1 ? ",\r\n" : "\r\n");
                }
                sb.Append(pad).Append("]");
                return;
            }
            sb.Append(Serialize(node));
        }
    }

    /// <summary>Line-level compare for scripts, HTML, XAML and other text.</summary>
    internal static class TextDiff
    {
        public const int MaxLinesShown = 15;

        public static string[] Lines(string text)
        {
            if (string.IsNullOrEmpty(text)) return new string[0];
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToArray();
        }

        /// <summary>
        /// Lines present on only one side (ignoring order and indentation), with line numbers.
        /// Good enough to see what changed in a script without a full diff algorithm.
        /// </summary>
        public static List<string> Compare(string left, string right, string leftName, string rightName, string what)
        {
            var result = new List<string>();
            var l = Lines(left);
            var r = Lines(right);
            if (l.Select(x => x.Trim()).SequenceEqual(r.Select(x => x.Trim()))) return result;

            var onlyLeft = OnlyIn(l, r);
            var onlyRight = OnlyIn(r, l);

            if (onlyLeft.Count == 0 && onlyRight.Count == 0)
            {
                result.Add("Changed: " + what + " has the same lines in a different order (" + leftName + " " + l.Length + " lines, " + rightName + " " + r.Length + " lines)");
                return result;
            }

            result.Add(string.Format("Changed: {0} ({1} {2} lines, {3} {4} lines; {5} line(s) only in {1}, {6} only in {3})",
                what, leftName, l.Length, rightName, r.Length, onlyLeft.Count, onlyRight.Count));
            foreach (var x in onlyLeft.Take(MaxLinesShown)) result.Add(string.Format("Only in {0}: line {1}: {2}", leftName, x.Item1, Shorten(x.Item2)));
            if (onlyLeft.Count > MaxLinesShown) result.Add("Only in " + leftName + ": ... " + (onlyLeft.Count - MaxLinesShown) + " more line(s)");
            foreach (var x in onlyRight.Take(MaxLinesShown)) result.Add(string.Format("Only in {0}: line {1}: {2}", rightName, x.Item1, Shorten(x.Item2)));
            if (onlyRight.Count > MaxLinesShown) result.Add("Only in " + rightName + ": ... " + (onlyRight.Count - MaxLinesShown) + " more line(s)");
            return result;
        }

        private static List<Tuple<int, string>> OnlyIn(string[] a, string[] b)
        {
            // Multiset difference on trimmed, non-empty lines
            var counts = new Dictionary<string, int>();
            foreach (var line in b.Select(x => x.Trim()).Where(x => x.Length > 0))
                counts[line] = counts.TryGetValue(line, out var n) ? n + 1 : 1;

            var result = new List<Tuple<int, string>>();
            for (int i = 0; i < a.Length; i++)
            {
                var t = a[i].Trim();
                if (t.Length == 0) continue;
                if (counts.TryGetValue(t, out var n) && n > 0) counts[t] = n - 1;
                else result.Add(Tuple.Create(i + 1, t));
            }
            return result;
        }

        public static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "").Substring(0, 12).ToLowerInvariant();
        }

        public static string Shorten(string s, int max = 160)
        {
            if (s == null) return "";
            s = s.Replace("\r", " ").Replace("\n", " ");
            return s.Length > max ? s.Substring(0, max) + "..." : s;
        }
    }
}
