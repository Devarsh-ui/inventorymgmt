using System;
using System.Collections.Generic;
using InventoryManagementSystem.Models;

namespace InventoryManagementSystem.ViewModels
{
    public class SupplierReturnStatsViewModel
    {
        // KPI Summary Cards
        public int TotalReturnClaims { get; set; }
        public int TotalDamagedItems { get; set; }
        public decimal TotalReturnFinancialValue { get; set; }
        public int PendingClaimsCount { get; set; }
        public int AcceptedClaimsCount { get; set; }
        public int RejectedClaimsCount { get; set; }
        public decimal TotalRefundValue { get; set; }
        public int TotalReplacementItems { get; set; }

        // Breakdown Analytics
        public List<ReturnReasonStatItem> ReasonBreakdown { get; set; } = new List<ReturnReasonStatItem>();
        public List<ReturnResolutionStatItem> ResolutionBreakdown { get; set; } = new List<ReturnResolutionStatItem>();
        public List<TopReturnedProductStatItem> TopReturnedProducts { get; set; } = new List<TopReturnedProductStatItem>();

        // Filter Inputs
        public string? Search { get; set; }
        public string? Reason { get; set; }
        public string? Resolution { get; set; }
        public string? Status { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? Brand { get; set; }
        public string? CategoryId { get; set; }
        public string SortBy { get; set; } = "newest";

        // Pagination
        public List<SupplierPurchaseReturn> Returns { get; set; } = new List<SupplierPurchaseReturn>();
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalItems { get; set; }
        public int TotalPages => PageSize > 0 ? (int)System.Math.Ceiling((double)TotalItems / PageSize) : 1;

        // Select Options
        public List<Category> Categories { get; set; } = new List<Category>();
        public List<string> Brands { get; set; } = new List<string>();
        public List<string> Reasons { get; set; } = new List<string>();
        public List<string> Resolutions { get; set; } = new List<string>();
        public List<string> Statuses { get; set; } = new List<string>();
    }

    public class ReturnReasonStatItem
    {
        public string Reason { get; set; } = string.Empty;
        public int Count { get; set; }
        public int ItemQuantity { get; set; }
        public decimal TotalValue { get; set; }
        public double Percentage { get; set; }
    }

    public class ReturnResolutionStatItem
    {
        public string Resolution { get; set; } = string.Empty;
        public int Count { get; set; }
        public int ItemQuantity { get; set; }
        public decimal TotalValue { get; set; }
    }

    public class TopReturnedProductStatItem
    {
        public string ProductName { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public int ReturnedQuantity { get; set; }
        public decimal TotalReturnPrice { get; set; }
        public string TopReason { get; set; } = string.Empty;
    }
}
