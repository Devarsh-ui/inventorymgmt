using InventoryManagementSystem.Models;
using System.Collections.Generic;

namespace InventoryManagementSystem.ViewModels
{
    public class SupplierInventoryViewModel
    {
        public IEnumerable<Product> Products { get; set; } = new List<Product>();
        public IEnumerable<Category> Categories { get; set; } = new List<Category>();

        // Filter Properties
        public string? SearchQuery { get; set; }
        public string? CategoryId { get; set; }
        public string? StockStatus { get; set; } // "All", "InStock", "LowStock", "OutOfStock"
        public int CurrentPage { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public int TotalPages { get; set; } = 1;
        public int TotalFilteredCount { get; set; }

        // Metrics Summary
        public int TotalSuppliedProducts { get; set; }
        public int TotalStockUnits { get; set; }
        public decimal TotalStockValuation { get; set; }
        public int LowStockCount { get; set; }
        public int OutOfStockCount { get; set; }
    }
}
