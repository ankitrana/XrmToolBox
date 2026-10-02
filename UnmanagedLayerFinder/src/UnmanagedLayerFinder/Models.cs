using System;
using System.Collections.Generic;

namespace UnmanagedLayerFinder
{
    public class SolutionItem
    {
        public Guid Id { get; set; }
        public string Display { get; set; }
        public bool IsManaged { get; set; }
        public string PublisherName { get; set; }
        public string PublisherUniqueName { get; set; }

        /// <summary>Publisher looks like a Microsoft first-party one (Microsoft Corporation, Dynamics 365, etc.).</summary>
        public bool IsMicrosoft
        {
            get
            {
                var text = ((PublisherName ?? "") + " " + (PublisherUniqueName ?? "")).ToLowerInvariant();
                return text.Contains("microsoft") || text.Contains("dynamics 365") || text.Contains("dynamics365");
            }
        }

        public override string ToString() { return Display; }
    }

    /// <summary>One solution component row shown in the grid.</summary>
    public class ComponentRow
    {
        public bool Selected { get; set; }
        public string Type { get; set; }
        public string Name { get; set; }
        public string Details { get; set; }             // e.g. current env var value
        public Guid ComponentId { get; set; }

        // Layer check results
        public string Status { get; set; }              // "", Unmanaged, Managed only, Error
        public int Layers { get; set; }
        public string LayerStack { get; set; }          // top -> bottom
        public DateTime? UnmanagedLayerTime { get; set; }
        public string ChangedAttributes { get; set; }

        // Who / when (best effort)
        public string ModifiedBy { get; set; }
        public DateTime? ModifiedOn { get; set; }
        public string AuditUser { get; set; }
        public DateTime? AuditTime { get; set; }
        public string Notes { get; set; }

        // Not displayed
        internal int TypeCode;
    }
}
