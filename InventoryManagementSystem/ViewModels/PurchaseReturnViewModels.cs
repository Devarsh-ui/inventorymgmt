using InventoryManagementSystem.Models;
using System.Collections.Generic;

namespace InventoryManagementSystem.ViewModels
{
    public class PurchaseReturnListViewModel
    {
        public IEnumerable<SupplierPurchaseReturn> Returns { get; set; } = new List<SupplierPurchaseReturn>();
        public string? Search { get; set; }
        public string? SupplierId { get; set; }
        public string? Status { get; set; }
        public string? Reason { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public long TotalCount { get; set; }
        public Dictionary<string, long> StatusCounts { get; set; } = new Dictionary<string, long>();
        public IEnumerable<Supplier> Suppliers { get; set; } = new List<Supplier>();

        public int TotalPages => (int)System.Math.Ceiling((double)TotalCount / PageSize);
    }

    public class PurchaseReturnCreateViewModel
    {
        public string SupplierId { get; set; } = string.Empty;
        public string PurchaseOrderId { get; set; } = string.Empty;
        public string Reason { get; set; } = PurchaseReturnReason.Defective;
        public string AdditionalRemarks { get; set; } = string.Empty;
        public string ResolutionType { get; set; } = PurchaseReturnResolution.Refund;

        public IEnumerable<Supplier> Suppliers { get; set; } = new List<Supplier>();
        public IEnumerable<SupplierOrder> EligibleOrders { get; set; } = new List<SupplierOrder>();
        public SupplierOrder? SelectedOrder { get; set; }
        public IEnumerable<Device> AvailableDevices { get; set; } = new List<Device>();
        public List<string> SelectedDeviceIds { get; set; } = new List<string>();
        
        // Dictionary of ProductId to return quantity for non-device items
        public Dictionary<string, int> ProductReturnQuantities { get; set; } = new Dictionary<string, int>();
        // Previously returned quantity mapping per product
        public Dictionary<string, int> PreviouslyReturnedQuantities { get; set; } = new Dictionary<string, int>();
    }

    public class PurchaseReturnDetailsViewModel
    {
        public SupplierPurchaseReturn ReturnRecord { get; set; } = new SupplierPurchaseReturn();
    }
}
