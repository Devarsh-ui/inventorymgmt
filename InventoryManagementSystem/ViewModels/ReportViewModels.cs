using System;
using System.Collections.Generic;

namespace InventoryManagementSystem.ViewModels
{
    public class ReportFilterRequest
    {
        public string ReportType { get; set; } = "Sales"; // Sales, ImeiInventory, TradeIn, Returns, Repairs, Warranty, Customer, Supplier, Inventory, StockMovement, EmployeeActivity, Alerts, ExecutiveSummary
        public string DatePreset { get; set; } = "ThisMonth";
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string? ProductId { get; set; }
        public string? CategoryId { get; set; }
        public string? SupplierId { get; set; }
        public string? TransactionType { get; set; }
        public string? EmployeeId { get; set; }
        public string? PaymentStatus { get; set; }
        public string? StockStatus { get; set; }
        public string? ReturnStatus { get; set; } // None, Partially Returned, Fully Returned
        public string? ReturnDestination { get; set; } // Returned, Damaged, UnderRepair
        public string? InvoiceNumber { get; set; }
        public string? IMEI { get; set; }
        public string? SearchTerm { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string? SortBy { get; set; }
        public bool IsDescending { get; set; } = true;
    }

    public class ReportSummaryStats
    {
        public decimal GrossSales { get; set; }
        public decimal TotalReturnsAmount { get; set; }
        public decimal NetRevenue { get; set; }
        public decimal TotalSalesRevenue { get; set; } // Alias for NetRevenue
        public int TotalOrders { get; set; }
        public int CompletedInvoicesCount { get; set; }
        public int PartiallyReturnedCount { get; set; }
        public int FullyReturnedCount { get; set; }
        public int TotalItemsSold { get; set; }
        public int TotalStockInQty { get; set; }
        public int TotalStockOutQty { get; set; }
        public decimal CurrentInventoryValue { get; set; }
        public decimal SellableInventoryValue { get; set; }
        public decimal DamagedInventoryValue { get; set; }
        public decimal UnderRepairInventoryValue { get; set; }
        public int DamagedInventoryCount { get; set; }
        public int UnderRepairInventoryCount { get; set; }
        public decimal PotentialSalesValue { get; set; }
        public int TotalInventoryQty { get; set; }
        public int LowStockCount { get; set; }
        public int OutOfStockCount { get; set; }
        public decimal TotalDiscounts { get; set; }
        public decimal TotalGstTax { get; set; }
        public decimal AverageOrderValue { get; set; }
        public int TotalProductsCount { get; set; }
        public int TotalCategoriesCount { get; set; }
        public decimal TotalProfitPotential { get; set; }
        public decimal NetProfit { get; set; }
    }

    public class ReportRowItem
    {
        public string Id { get; set; } = string.Empty;
        public string DateString { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ProductInfo { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string CustomerInfo { get; set; } = string.Empty;
        public string PrimaryText { get; set; } = string.Empty;
        public string SecondaryText { get; set; } = string.Empty;
        public string BadgeText { get; set; } = string.Empty;
        public string BadgeClass { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public int StockQty { get; set; }
        public int MinStockQty { get; set; }
        public int PreviousStock { get; set; }
        public int NewStock { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal CostPrice { get; set; }
        public decimal SellingPrice { get; set; }
        public decimal SubTotal { get; set; }
        public decimal Discount { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal GrandTotal { get; set; }
        public decimal RefundedAmount { get; set; }
        public decimal NetAmount { get; set; }
        public string ReturnStatus { get; set; } = "None";
        public decimal CostValue { get; set; }
        public decimal SalesValue { get; set; }
        public decimal ProfitValue { get; set; }
        public decimal CostValuation { get; set; }
        public decimal SalesValuation { get; set; }
        public decimal ProfitPotential { get; set; }
        public string UserExecutor { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string ReferenceInfo { get; set; } = string.Empty;
    }

    public class ReportResultData
    {
        public string ReportTitle { get; set; } = "System Report";
        public string ReportType { get; set; } = "Sales";
        public ReportSummaryStats SummaryStats { get; set; } = new ReportSummaryStats();
        public List<string> Headers { get; set; } = new List<string>();
        public List<ReportRowItem> Rows { get; set; } = new List<ReportRowItem>();
        public int TotalCount { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public int TotalPages { get; set; } = 1;
        public string AppliedFiltersText { get; set; } = string.Empty;
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}
