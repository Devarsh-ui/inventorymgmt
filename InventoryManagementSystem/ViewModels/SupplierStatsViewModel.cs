using InventoryManagementSystem.Models;
using System;
using System.Collections.Generic;

namespace InventoryManagementSystem.ViewModels
{
    public class SupplierStatsViewModel
    {
        // Filter Parameters
        public string RangePreset { get; set; } = "30days"; // "today", "7days", "30days", "thismonth", "custom"
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? StatusFilter { get; set; }

        // Summary KPI Metrics
        public decimal TotalRevenue { get; set; }
        public int TotalUnitsSupplied { get; set; }
        public int TotalOrdersCount { get; set; }
        public int CompletedOrdersCount { get; set; }
        public int PendingOrdersCount { get; set; }
        public int RejectedOrdersCount { get; set; }
        public double AcceptanceRatePercentage { get; set; }
        public decimal AverageOrderValue { get; set; }
        public string TopCategoryName { get; set; } = "N/A";

        // Chart & Detailed Breakdown Sources
        public List<DailySalesStat> TimelineStats { get; set; } = new();
        public Dictionary<string, int> StatusDistribution { get; set; } = new();
        public List<TopSuppliedProductStat> TopProducts { get; set; } = new();
        public List<CategorySalesStat> CategoryBreakdown { get; set; } = new();
        public List<SupplierOrder> RecentOrders { get; set; } = new();
    }

    public class DailySalesStat
    {
        public string DateLabel { get; set; } = string.Empty;
        public decimal Revenue { get; set; }
        public int UnitsSold { get; set; }
        public int OrdersCount { get; set; }
    }

    public class TopSuppliedProductStat
    {
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Brand { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public int UnitsSold { get; set; }
        public decimal TotalRevenue { get; set; }
        public decimal CurrentStock { get; set; }
    }

    public class CategorySalesStat
    {
        public string CategoryName { get; set; } = string.Empty;
        public int UnitsSold { get; set; }
        public decimal TotalRevenue { get; set; }
        public double PercentageShare { get; set; }
    }
}
