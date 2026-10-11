using InventoryManagementSystem.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Interfaces
{
    public interface ILicenseService
    {
        Task<TenantLicense?> GetActiveLicenseAsync(string tenantId);
        Task<bool> CanAccessModuleAsync(string tenantId, string moduleName);
        Task<bool> IsLicenseValidAsync(string tenantId);
        Task<SubscriptionPackage?> GetTenantPackageAsync(string tenantId);
        Task<(bool Allowed, string Message)> CheckPackageLimitAsync(string tenantId, string resourceType);
        Task<TenantLicense> CreateTrialLicenseAsync(string tenantId, string packageId, int days = 14);
        Task<bool> RenewOrExtendLicenseAsync(string tenantId, string packageId, int durationDays);
        Task<IEnumerable<SubscriptionPackage>> GetAllPackagesAsync();
        Task<IEnumerable<SubscriptionPackage>> GetStandardPackagesAsync();
        Task<IEnumerable<SubscriptionPackage>> GetPublicPackagesAsync();
        Task<IEnumerable<SubscriptionPackage>> GetCustomPackagesAsync();
        Task<SubscriptionPackage?> GetPackageByIdAsync(string packageId);
        Task CreateOrUpdatePackageAsync(SubscriptionPackage package);
    }
}
