using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace DeploymentDoctor
{
    internal class WebResourceHandler : ComponentHandler
    {
        // Types stored as text: HTML, CSS, JS, XML, XSL, SVG, RESX
        private static readonly HashSet<int> TextTypes = new HashSet<int> { 1, 2, 3, 4, 9, 11, 12 };

        public override ComponentKind Kind { get { return ComponentKind.WebResource; } }
        public override string Label { get { return "Web resource"; } }
        public override string Table { get { return "webresource"; } }
        public override int ComponentType { get { return 61; } }
        public override string LayerName { get { return "WebResource"; } }
        public override string[] ContentAttributes { get { return new[] { "content" }; } }
        public override string[] OtherAttributes { get { return new[] { "name", "displayname", "webresourcetype" }; } }
        public override string GroupLabel { get { return "Prefix:"; } }

        public static string TypeLabel(int type)
        {
            switch (type)
            {
                case 1: return "HTML";
                case 2: return "CSS";
                case 3: return "Script (JS)";
                case 4: return "XML";
                case 5: return "PNG";
                case 6: return "JPG";
                case 7: return "GIF";
                case 8: return "Silverlight (XAP)";
                case 9: return "XSL";
                case 10: return "ICO";
                case 11: return "SVG";
                case 12: return "RESX";
                default: return "Type " + type;
            }
        }

        /// <summary>"new_/scripts/account.js" -> "new_", "cts_account.js" -> "cts_".</summary>
        public static string Prefix(string name)
        {
            if (string.IsNullOrEmpty(name)) return "(none)";
            var slash = name.IndexOf('/');
            if (slash > 0) return name.Substring(0, slash);
            var underscore = name.IndexOf('_');
            return underscore > 0 ? name.Substring(0, underscore + 1) : "(no prefix)";
        }

        public override List<ComponentItem> LoadAll(IOrganizationService svc)
        {
            var q = new QueryExpression(Table) { ColumnSet = new ColumnSet("name", "webresourcetype") };
            q.AddOrder("name", OrderType.Ascending);
            return All(svc, q).Select(e =>
            {
                var type = OptionValue(e, "webresourcetype");
                var name = e.GetAttributeValue<string>("name");
                return new ComponentItem
                {
                    Kind = Kind,
                    Id = e.Id,
                    Name = name,
                    Group = Prefix(name),
                    SubType = TypeLabel(type),
                    SubTypeCode = type,
                    IsActive = true
                };
            }).ToList();
        }

        private static byte[] Bytes(Snapshot s)
        {
            var b64 = s.Get("content");
            if (string.IsNullOrEmpty(b64)) return new byte[0];
            try { return Convert.FromBase64String(b64); }
            catch (FormatException) { return Encoding.UTF8.GetBytes(b64); }
        }

        private static string Text(byte[] data)
        {
            var text = Encoding.UTF8.GetString(data);
            return text.Length > 0 && text[0] == '﻿' ? text.Substring(1) : text;
        }

        private static int TypeOf(Snapshot s, Snapshot other)
        {
            var e = s.Record ?? (other == null ? null : other.Record);
            return OptionValue(e, "webresourcetype", 3);
        }

        public override List<string> Compare(Snapshot left, Snapshot right, string leftName, string rightName)
        {
            var a = Bytes(left);
            var b = Bytes(right);
            if (a.SequenceEqual(b)) return new List<string>();

            if (TextTypes.Contains(TypeOf(left, right)))
                return TextDiff.Compare(Text(a), Text(b), leftName, rightName, "file content");

            return new List<string>
            {
                string.Format("Changed: file content ({0}: {1:N0} bytes, #{2}; {3}: {4:N0} bytes, #{5})",
                    leftName, a.Length, TextDiff.Hash(a), rightName, b.Length, TextDiff.Hash(b))
            };
        }

        public override string Pretty(Snapshot s)
        {
            var data = Bytes(s);
            if (TextTypes.Contains(TypeOf(s, null))) return Text(data);
            return string.Format("(binary file, {0:N0} bytes, #{1})", data.Length, TextDiff.Hash(data));
        }

        public override string PublishScope(ComponentItem item) { return "web resource '" + item.Name + "'"; }

        public override void Publish(IOrganizationService svc, ComponentItem item)
        {
            PublishXml(svc, "<webresources><webresource>" + item.Id.ToString("B") + "</webresource></webresources>");
        }

        public override string WhenIdentical
        {
            get
            {
                return "Browsers cache web resources: ask users to hard-refresh (Ctrl+F5) or clear the browser cache, " +
                       "and check the form's library list loads this file.";
            }
        }

        public override void TypeChecks(CheckContext c)
        {
            DoctorService.Try(c.Findings, "Used by", () =>
            {
                var res = (RetrieveDependentComponentsResponse)c.Target.Execute(new RetrieveDependentComponentsRequest
                {
                    ObjectId = c.Item.Id,
                    ComponentType = ComponentType
                });
                var deps = res.EntityCollection.Entities;
                var formIds = deps.Where(x => OptionValue(x, "dependentcomponenttype") == 60)
                                  .Select(x => x.GetAttributeValue<Guid>("dependentcomponentobjectid")).Distinct().ToList();
                var others = deps.Count(x => OptionValue(x, "dependentcomponenttype") != 60);

                var formNames = new List<string>();
                if (formIds.Count > 0)
                {
                    var q = new QueryExpression("systemform") { ColumnSet = new ColumnSet("name", "objecttypecode") };
                    q.Criteria.AddCondition("formid", ConditionOperator.In, formIds.Cast<object>().ToArray());
                    formNames = c.Target.RetrieveMultiple(q).Entities
                        .Select(e => e.GetAttributeValue<string>("name") + " (" + e.GetAttributeValue<string>("objecttypecode") + ")").ToList();
                }

                if (formIds.Count == 0 && others == 0)
                {
                    c.Findings.Add(new Finding(c.Item.SubTypeCode == 3 ? Severity.Warning : Severity.Info, "Used by",
                        "Nothing in Target uses this web resource (no form, ribbon, sitemap or app references it).",
                        c.Item.SubTypeCode == 3
                            ? "Check the form in Target lists this script under Form libraries and calls it in an event handler. Fix that in Dev and deploy."
                            : null));
                }
                else
                {
                    c.Findings.Add(new Finding(Severity.Info, "Used by",
                        (formNames.Count > 0 ? "Forms: " + string.Join(", ", formNames) + ". " : "") +
                        (others > 0 ? others + " other component(s) (ribbons, sitemaps, other web resources, apps)." : "")));
                }
            });
        }
    }
}
