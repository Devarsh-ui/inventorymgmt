using InventoryManagementSystem.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Interfaces
{
    public interface ISupplierPurchaseReturnRepository : IBaseRepository<SupplierPurchaseReturn>
    {
        Task<SupplierPurchaseReturn?> GetByReturnNumberAsync(string returnNumber);
        Task<IEnumerable<SupplierPurchaseReturn>> GetPagedReturnsAsync(string? search, string? supplierId, string? status, string? reason, int page, int pageSize);
        Task<long> GetFilteredCountAsync(string? search, string? supplierId, string? status, string? reason);
        Task<IEnumerable<SupplierPurchaseReturn>> GetSupplierReturnsAsync(string supplierId, string? status, int limit = 50);
        Task<Dictionary<string, long>> GetReturnStatusCountsAsync(string? supplierId = null);
        Task<IEnumerable<SupplierPurchaseReturn>> GetReturnsForOrderAsync(string purchaseOrderId);
    }
}
