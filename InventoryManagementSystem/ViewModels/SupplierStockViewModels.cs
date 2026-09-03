using InventoryManagementSystem.Models;
using System;
using System.Collections.Generic;

namespace InventoryManagementSystem.ViewModels
{
    public class SupplierStockInViewModel
    {
        public Supplier? Supplier { get; set; }
        public IEnumerable<Product> Products { get; set; } = new List<Product>();
        public IEnumerable<StockTransaction> Transactions { get; set; } = new List<StockTransaction>();

        // Metrics
        public int TotalStockInUnits { get; set; }
        public int TodayStockInUnits { get; set; }
        public decimal TotalStockInValuation { get; set; }
        public int TotalTransactionsCount { get; set; }

        // Filter & Pagination
        public string? SearchQuery { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 15;
        public int TotalPages { get; set; } = 1;
        public int TotalFilteredCount { get; set; }

        // Form Fields
        public string ProductId { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public string Reason { get; set; } = "Factory Shipment";
        public string? Notes { get; set; }
        public string? Imei1 { get; set; }
        public string? Imei2 { get; set; }
        public string? SerialNumber { get; set; }
        public string? BulkImeis { get; set; }
    }

    public class SupplierStockOutViewModel
    {
        public Supplier? Supplier { get; set; }
        public IEnumerable<Product> Products { get; set; } = new List<Product>();
        public IEnumerable<StockTransaction> Transactions { get; set; } = new List<StockTransaction>();

        // Metrics
        public int TotalStockOutUnits { get; set; }
        public int TodayStockOutUnits { get; set; }
        public decimal TotalStockOutValuation { get; set; }
        public int TotalTransactionsCount { get; set; }

        // Filter & Pagination
        public string? SearchQuery { get; set; }
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 15;
        public int TotalPages { get; set; } = 1;
        public int TotalFilteredCount { get; set; }

        // Form Fields
        public string ProductId { get; set; } = string.Empty;
        public int Quantity { get; set; } = 1;
        public string Reason { get; set; } = "Dispatched to Retailer";
        public string? DeviceId { get; set; }
        public string? Notes { get; set; }
    }
}
