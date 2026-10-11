using InventoryManagementSystem.Data;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Services
{
    public class PlatformSettingsService : IPlatformSettingsService
    {
        private readonly MongoDbContext _context;

        public PlatformSettingsService(MongoDbContext context)
        {
            _context = context;
        }

        public async Task<PlatformSettings> GetSettingsAsync()
        {
            var settings = await _context.PlatformSettings.Find(FilterDefinition<PlatformSettings>.Empty).FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new PlatformSettings
                {
                    PlatformName = "SIMS SaaS Platform",
                    LogoUrl = "/images/logo-dark.png",
                    ContactEmail = "support@sims.com",
                    ContactPhone = "+91 98765 43210",
                    OfficeAddress = "123 Business Hub, Mumbai, India",
                    SupportHours = "Mon - Sat: 9:00 AM - 8:00 PM IST",
                    WebsiteMaintenanceEnabled = false,
                    WebsiteMaintenanceMessage = "We are currently performing scheduled platform maintenance and upgrades. We will be back online shortly!",
                    MaintenancePages = new List<string> { "HomePage", "Features", "Pricing", "Register", "Contact" },
                    ShopDashboardMaintenanceEnabled = false,
                    ShopDashboardMaintenanceMessage = "The Shop Admin dashboard is currently undergoing scheduled system optimization. Normal access will resume shortly.",
                    LastUpdated = DateTime.UtcNow,
                    UpdatedBy = "System"
                };
                await _context.PlatformSettings.InsertOneAsync(settings);
            }
            return settings;
        }

        public async Task SaveSettingsAsync(PlatformSettings settings, string updatedBy)
        {
            settings.LastUpdated = DateTime.UtcNow;
            settings.UpdatedBy = updatedBy;

            var existing = await _context.PlatformSettings.Find(FilterDefinition<PlatformSettings>.Empty).FirstOrDefaultAsync();
            if (existing == null)
            {
                await _context.PlatformSettings.InsertOneAsync(settings);
            }
            else
            {
                settings.Id = existing.Id;
                await _context.PlatformSettings.ReplaceOneAsync(s => s.Id == existing.Id, settings);
            }
        }

        public async Task<(bool IsInMaintenance, string Message, DateTime? EndTime)> CheckWebsiteMaintenanceAsync(string pageName)
        {
            var settings = await GetSettingsAsync();
            if (!settings.WebsiteMaintenanceEnabled)
            {
                return (false, string.Empty, null);
            }

            var now = DateTime.UtcNow;

            // Check scheduled start time
            if (settings.WebsiteMaintenanceStartTime.HasValue && now < settings.WebsiteMaintenanceStartTime.Value)
            {
                return (false, string.Empty, null); // Hasn't started yet
            }

            // Check scheduled end time
            if (settings.WebsiteMaintenanceEndTime.HasValue && now > settings.WebsiteMaintenanceEndTime.Value)
            {
                return (false, string.Empty, null); // Already concluded
            }

            // Check if current page is within target maintenance pages
            var pages = settings.MaintenancePages ?? new List<string>();
            if (pages.Any(p => p.Equals("All", StringComparison.OrdinalIgnoreCase)))
            {
                return (true, settings.WebsiteMaintenanceMessage, settings.WebsiteMaintenanceEndTime);
            }

            bool matches = false;
            if (pageName.Equals("Index", StringComparison.OrdinalIgnoreCase) &&
                pages.Any(p => p.Equals("HomePage", StringComparison.OrdinalIgnoreCase) || p.Equals("Index", StringComparison.OrdinalIgnoreCase) || p.Equals("Home", StringComparison.OrdinalIgnoreCase)))
            {
                matches = true;
            }
            else if (pages.Any(p => p.Equals(pageName, StringComparison.OrdinalIgnoreCase)))
            {
                matches = true;
            }

            if (matches)
            {
                return (true, settings.WebsiteMaintenanceMessage, settings.WebsiteMaintenanceEndTime);
            }

            return (false, string.Empty, null);
        }

        public async Task<(bool IsInMaintenance, string Message, DateTime? EndTime)> CheckShopDashboardMaintenanceAsync()
        {
            var settings = await GetSettingsAsync();
            if (!settings.ShopDashboardMaintenanceEnabled)
            {
                return (false, string.Empty, null);
            }

            var now = DateTime.UtcNow;

            // Check scheduled start time
            if (settings.ShopDashboardMaintenanceStartTime.HasValue && now < settings.ShopDashboardMaintenanceStartTime.Value)
            {
                return (false, string.Empty, null); // Hasn't started yet
            }

            // Check scheduled end time
            if (settings.ShopDashboardMaintenanceEndTime.HasValue && now > settings.ShopDashboardMaintenanceEndTime.Value)
            {
                return (false, string.Empty, null); // Already concluded
            }

            return (true, settings.ShopDashboardMaintenanceMessage, settings.ShopDashboardMaintenanceEndTime);
        }
    }
}
