using InventoryManagementSystem.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Interfaces
{
    public interface ISupplierOrderService
    {
        Task<SupplierOrder?> GetOrderByIdAsync(string id);
        Task<SupplierOrder?> GetOrderByNumberAsync(string orderNumber);
        Task<IEnumerable<SupplierOrder>> GetPagedOrdersAsync(string? search, string? supplierId, string? status, int page, int pageSize, DateTime? fromDate = null, DateTime? toDate = null, decimal? minAmount = null, decimal? maxAmount = null, string? sortBy = null);
        Task<long> GetFilteredCountAsync(string? search, string? supplierId, string? status, DateTime? fromDate = null, DateTime? toDate = null, decimal? minAmount = null, decimal? maxAmount = null);
        Task<IEnumerable<SupplierOrder>> GetSupplierOrdersAsync(string supplierId, string? status, int limit = 50);
        Task<Dictionary<string, long>> GetOrderStatusCountsAsync(string? supplierId = null);
        Task<(bool Success, string Message, SupplierOrder? Order)> CreateOrderAsync(SupplierOrder order, string executedBy);
        Task<(bool Success, string Message)> UpdateOrderStatusAsync(string orderId, string newStatus, string updatedBy, string? supplierNotes = null, DateTime? expectedDeliveryDate = null);
        Task<(bool Success, string Message)> DeleteOrderAsync(string orderId, string executedBy, string? supplierIdFilter = null);
        Task<(bool Success, string Message, int DeletedCount)> BulkDeleteOrdersAsync(List<string> orderIds, string executedBy, string? supplierIdFilter = null);
    }
}
