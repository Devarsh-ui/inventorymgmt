using InventoryManagementSystem.Models;
using System;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Interfaces
{
    public interface IPlatformSettingsService
    {
        Task<PlatformSettings> GetSettingsAsync();
        Task SaveSettingsAsync(PlatformSettings settings, string updatedBy);
        Task<(bool IsInMaintenance, string Message, DateTime? EndTime)> CheckWebsiteMaintenanceAsync(string pageName);
        Task<(bool IsInMaintenance, string Message, DateTime? EndTime)> CheckShopDashboardMaintenanceAsync();
    }
}
