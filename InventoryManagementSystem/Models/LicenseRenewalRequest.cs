using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace InventoryManagementSystem.Models
{
    public class LicenseRenewalRequest
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        public string TenantId { get; set; } = string.Empty;
        public string TenantCode { get; set; } = string.Empty;
        public string ShopName { get; set; } = string.Empty;
        public string OwnerName { get; set; } = string.Empty;
        public string ContactEmail { get; set; } = string.Empty;
        public string ContactPhone { get; set; } = string.Empty;

        // Current status at time of request
        public string CurrentStatus { get; set; } = "Active";
        public DateTime? CurrentExpiryDate { get; set; }

        // Requested Package details
        public string PackageId { get; set; } = string.Empty;
        public string PackageName { get; set; } = string.Empty;
        public decimal PackageMonthlyPrice { get; set; }
        public decimal PackageYearlyPrice { get; set; }
        public bool IsCustomPackage { get; set; }
        public List<string> PackageFeatures { get; set; } = new();
        public string PackageLimitsSummary { get; set; } = string.Empty;

        // Request Specifications
        public string RequestedCycle { get; set; } = "Monthly"; // "Monthly", "Quarterly", "Semi-Annual", "Yearly", "Custom"
        public int RequestedDurationMonths { get; set; } = 1;
        public int RequestedDays { get; set; } = 30;
        public string RequestMessage { get; set; } = string.Empty; // Notes or message from shop owner
        public string PaymentModePreference { get; set; } = "UPI / Online"; // Bank Transfer, UPI, Cash, etc.

        // Status workflow
        public string Status { get; set; } = "Pending"; // "Pending", "Approved", "Rejected"
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ProcessedAt { get; set; }
        public string? ProcessedBy { get; set; }
        public string? AdminNotes { get; set; }
    }
}
