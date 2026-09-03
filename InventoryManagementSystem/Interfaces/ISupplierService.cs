using InventoryManagementSystem.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Interfaces
{
    public interface ISupplierService
    {
        Task<IEnumerable<Supplier>> GetAllSuppliersAsync();
        Task<Supplier?> GetSupplierByIdAsync(string id);
        Task<IEnumerable<Supplier>> GetPagedSuppliersAsync(string? search, string? terms, string? payableStatus, int page, int pageSize);
        Task<long> GetFilteredCountAsync(string? search, string? terms, string? payableStatus);
        Task<(bool Success, string Message, Supplier? Supplier)> SaveSupplierAsync(Supplier supplier, string executedBy);
        Task<(bool Success, string Message)> DeleteSupplierAsync(string id, string executedBy);
        Task<Supplier?> AuthenticateSupplierAsync(string emailOrUsername, string password);
        Task CleanupOrphanedSupplierDataAsync();
    }
}
