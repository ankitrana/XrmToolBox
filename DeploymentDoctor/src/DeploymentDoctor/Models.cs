using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace DeploymentDoctor
{
    public class SolutionItem
    {
        public Guid Id { get; set; }
        public string UniqueName { get; set; }
        public string FriendlyName { get; set; }
        public string Version { get; set; }
        public bool IsManaged { get; set; }
        public string PublisherName { get; set; }

        public bool IsMicrosoft
        {
            get
            {
                var text = (PublisherName ?? "").ToLowerInvariant();
                return text.Contains("microsoft") || text.Contains("dynamics 365") || text.Contains("dynamics365");
            }
        }

        public override string ToString()
        {
            return string.Format("{0} ({1}) v{2} [{3}]", FriendlyName, UniqueName, Version, IsManaged ? "managed" : "unmanaged");
        }
    }

    public enum ComponentKind { Form, View, WebResource, Process }

    /// <summary>One form, view, web resource or process in the pickers.</summary>
    public class ComponentItem
    {
        public ComponentKind Kind { get; set; }
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string Group { get; set; }       // table logical name, or web resource prefix
        public string SubType { get; set; }     // "Main", "Public view", "Script (JS)", "Cloud flow" ...
        public int SubTypeCode { get; set; }    // form type, view querytype, web resource type, process category
        public bool IsActive { get; set; }
        public string InactiveText { get; set; } = "inactive";

        public override string ToString()
        {
            return string.Format("{0}  [{1}{2}]", Name, SubType, IsActive ? "" : ", " + InactiveText);
        }
    }

    public class TableItem
    {
        public string LogicalName { get; set; }
        public string DisplayName { get; set; }
        public Guid MetadataId { get; set; }
        public override string ToString() { return string.IsNullOrEmpty(DisplayName) ? LogicalName : DisplayName + " (" + LogicalName + ")"; }
    }

    /// <summary>Entry of the second picker: a table, a web resource prefix, or "(no table)".</summary>
    public class GroupOption
    {
        public string Key { get; set; }
        public string Text { get; set; }
        public override string ToString() { return Text; }
    }

    public enum Severity { Problem, Warning, Ok, Info }

    /// <summary>One check result row.</summary>
    public class Finding
    {
        public Severity Severity { get; set; }
        public string Check { get; set; }
        public string Result { get; set; }
        public string HowToFix { get; set; }
        public string FixButton { get { return Action == null ? "" : Action.Label; } }

        internal FixAction Action;

        internal Finding(Severity severity, string check, string result, string howToFix = null, FixAction action = null)
        {
            Severity = severity; Check = check; Result = result; HowToFix = howToFix; Action = action;
        }
    }

    /// <summary>A change the tool can make for you. Always confirmed by the user first.</summary>
    internal class FixAction
    {
        public string Label;            // short, e.g. "Remove unmanaged layer"
        public bool OnDev;              // false = target connection
        public string WhatItDoes;       // shown in the confirmation dialog
        public Action<IOrganizationService> Run;

        // Shown in FixConfirmDialog so nobody changes an environment without knowing the impact
        public string Undo;             // can it be undone, and how
        public string Advice;           // what to check before doing it
        public string ManualSteps;      // how to do the same by hand (maker portal)
    }

    /// <summary>Who a connection acts as (WhoAmI + systemuser).</summary>
    internal class CallerInfo
    {
        public string FullName;
        public string Login;            // domainname / email
        public bool IsApplicationUser;  // app registration (client id + secret)

        public override string ToString()
        {
            return (FullName ?? "?") + (string.IsNullOrEmpty(Login) ? "" : " (" + Login + ")") +
                   (IsApplicationUser ? " - application user (app registration), not a person" : "");
        }
    }

    public class LayerRow
    {
        public int Order { get; set; }
        public string Solution { get; set; }
        public string Publisher { get; set; }
        public DateTime? Written { get; set; }
        public string Note { get; set; }

        internal string ComponentJson;  // msdyn_componentjson: the layer's attribute values
        internal string Changes;        // msdyn_changes (Active layer)
    }

    public class Diagnosis
    {
        public List<Finding> Findings = new List<Finding>();
        public List<LayerRow> Layers = new List<LayerRow>();
        public List<string> Differences = new List<string>();
        public List<string> LayerDifferences = new List<string>();   // Dev vs your solution's layer
        public bool TargetMatchesDev;
        public string Verdict;
        public string NextStep;

        // Readable content for the tabs and reports (XML, script text, flow definition)
        public string TargetContent;
        public string DevContent;
        public string LayerContent;
    }
}
