using InventoryManagementSystem.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Interfaces
{
    public interface ISupplierPurchaseReturnService
    {
        Task<SupplierPurchaseReturn?> GetReturnByIdAsync(string id);
        Task<SupplierPurchaseReturn?> GetReturnByNumberAsync(string returnNumber);
        Task<IEnumerable<SupplierPurchaseReturn>> GetPagedReturnsAsync(string? search, string? supplierId, string? status, string? reason, int page, int pageSize);
        Task<long> GetFilteredCountAsync(string? search, string? supplierId, string? status, string? reason);
        Task<IEnumerable<SupplierPurchaseReturn>> GetSupplierReturnsAsync(string supplierId, string? status, int limit = 50);
        Task<Dictionary<string, long>> GetReturnStatusCountsAsync(string? supplierId = null);
        
        Task<IEnumerable<SupplierOrder>> GetEligibleOrdersForReturnAsync(string? supplierId = null);
        Task<IEnumerable<Device>> GetEligibleDevicesForOrderAsync(string orderId, string? productId = null);
        Task<Dictionary<string, int>> GetReturnedQuantitiesForOrderAsync(string purchaseOrderId);

        Task<(bool Success, string Message, SupplierPurchaseReturn? ReturnRecord)> CreateReturnAsync(SupplierPurchaseReturn returnRecord, List<string> selectedDeviceIds, string executedBy);
        Task<(bool Success, string Message)> UpdateReturnStatusAsync(string returnId, string newStatus, string updatedBy, string? remarks = null, string? rejectionReason = null);
        Task<(bool Success, string Message)> ShipReturnAsync(string returnId, string executedBy);
        Task<(bool Success, string Message)> CancelReturnAsync(string returnId, string executedBy);
    }
}
