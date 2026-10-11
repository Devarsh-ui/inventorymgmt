using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using InventoryManagementSystem.Data;
using MongoDB.Driver;
using System;
using System.Security.Claims;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers
{
    [Authorize(Roles = Role.Admin)]
    public class SettingsController : Controller
    {
        private readonly MongoDbContext _context;
        private readonly IAuditLogService _auditLogService;
        private readonly IMongoCollection<Settings> _settingsCollection;

        public SettingsController(MongoDbContext context, IAuditLogService auditLogService)
        {
            _context = context;
            _auditLogService = auditLogService;
            _settingsCollection = _context.GetCollection<Settings>("Settings");
        }

        private string? GetCurrentTenantId()
        {
            return User.FindFirst("TenantId")?.Value ?? User.FindFirst(ClaimTypes.GroupSid)?.Value;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var tenantId = GetCurrentTenantId();
            Settings? settings = null;

            if (!string.IsNullOrEmpty(tenantId))
            {
                // Fetch tenant-specific settings
                settings = await _settingsCollection.Find(s => s.TenantId == tenantId).FirstOrDefaultAsync();
                if (settings == null)
                {
                    // Seed from tenant record
                    var tenant = await _context.Tenants.Find(t => t.Id == tenantId).FirstOrDefaultAsync();
                    settings = new Settings
                    {
                        TenantId = tenantId,
                        CompanyName = !string.IsNullOrWhiteSpace(tenant?.ShopName) ? tenant.ShopName : "My Mobile Shop",
                        CompanyEmail = !string.IsNullOrWhiteSpace(tenant?.ContactEmail) ? tenant.ContactEmail : "support@shop.com",
                        CompanyPhone = !string.IsNullOrWhiteSpace(tenant?.Phone) ? tenant.Phone : "+91 98765 43210",
                        Address = !string.IsNullOrWhiteSpace(tenant?.Address) ? tenant.Address : "123 Business Hub, Mumbai, India",
                        CurrencySymbol = "₹",
                        GstRate = 18.0m,
                        LowStockThreshold = 5,
                        GstinNumber = !string.IsNullOrWhiteSpace(tenant?.GSTIN) ? tenant.GSTIN : "27AAAAA0000A1Z5"
                    };
                    await _settingsCollection.InsertOneAsync(settings);
                }
            }
            else
            {
                // Platform global fallback
                settings = await _settingsCollection.Find(s => s.TenantId == null || s.TenantId == "").FirstOrDefaultAsync();
                if (settings == null)
                {
                    settings = new Settings
                    {
                        CompanyName = "SIMS Enterprise Ltd.",
                        CompanyEmail = "support@sims.com",
                        CompanyPhone = "+91 98765 43210",
                        Address = "123 Business Hub, Mumbai, India",
                        CurrencySymbol = "₹",
                        GstRate = 18.0m,
                        LowStockThreshold = 5,
                        GstinNumber = "27AAAAA0000A1Z5"
                    };
                    await _settingsCollection.InsertOneAsync(settings);
                }
            }

            return View(settings);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(Settings model)
        {
            if (!ModelState.IsValid)
            {
                return View("Index", model);
            }

            var tenantId = GetCurrentTenantId();

            if (!string.IsNullOrEmpty(tenantId))
            {
                model.TenantId = tenantId;
                var existing = await _settingsCollection.Find(s => s.TenantId == tenantId).FirstOrDefaultAsync();

                if (existing != null)
                {
                    var filter = Builders<Settings>.Filter.Eq(s => s.Id, existing.Id);
                    var update = Builders<Settings>.Update
                        .Set(s => s.TenantId, tenantId)
                        .Set(s => s.CompanyName, model.CompanyName)
                        .Set(s => s.CompanyEmail, model.CompanyEmail)
                        .Set(s => s.CompanyPhone, model.CompanyPhone)
                        .Set(s => s.Address, model.Address)
                        .Set(s => s.CurrencySymbol, model.CurrencySymbol)
                        .Set(s => s.GstRate, model.GstRate)
                        .Set(s => s.LowStockThreshold, model.LowStockThreshold)
                        .Set(s => s.GstinNumber, model.GstinNumber)
                        .Set(s => s.LastUpdated, DateTime.UtcNow)
                        .Set(s => s.UpdatedBy, User.Identity?.Name ?? "Admin");

                    await _settingsCollection.UpdateOneAsync(filter, update);
                }
                else
                {
                    model.Id = string.Empty;
                    model.TenantId = tenantId;
                    model.LastUpdated = DateTime.UtcNow;
                    model.UpdatedBy = User.Identity?.Name ?? "Admin";
                    await _settingsCollection.InsertOneAsync(model);
                }

                // Synchronize shop name, email, phone, address, and GSTIN directly to the Tenant document
                var tenantUpdate = Builders<Tenant>.Update
                    .Set(t => t.ShopName, model.CompanyName)
                    .Set(t => t.ContactEmail, model.CompanyEmail)
                    .Set(t => t.Phone, model.CompanyPhone)
                    .Set(t => t.Address, model.Address)
                    .Set(t => t.GSTIN, model.GstinNumber)
                    .Set(t => t.UpdatedAt, DateTime.UtcNow);

                await _context.Tenants.UpdateOneAsync(t => t.Id == tenantId, tenantUpdate);

                await _auditLogService.LogActivityAsync("Settings Updated", User.Identity?.Name ?? "Admin", "Shop Config", $"Updated shop configurations and identity for '{model.CompanyName}'.");

                TempData["ToastMessage"] = "Shop configurations saved and shop profile updated successfully!";
                TempData["ToastType"] = "success";
            }
            else
            {
                // Platform global fallback
                var filter = Builders<Settings>.Filter.Eq(s => s.Id, model.Id);
                var update = Builders<Settings>.Update
                    .Set(s => s.CompanyName, model.CompanyName)
                    .Set(s => s.CompanyEmail, model.CompanyEmail)
                    .Set(s => s.CompanyPhone, model.CompanyPhone)
                    .Set(s => s.Address, model.Address)
                    .Set(s => s.CurrencySymbol, model.CurrencySymbol)
                    .Set(s => s.GstRate, model.GstRate)
                    .Set(s => s.LowStockThreshold, model.LowStockThreshold)
                    .Set(s => s.GstinNumber, model.GstinNumber)
                    .Set(s => s.LastUpdated, DateTime.UtcNow)
                    .Set(s => s.UpdatedBy, User.Identity?.Name ?? "Admin");

                await _settingsCollection.UpdateOneAsync(filter, update);
                await _auditLogService.LogActivityAsync("Settings Updated", User.Identity?.Name ?? "Admin", "System Config", "Updated global system configurations.");

                TempData["ToastMessage"] = "Global configurations saved successfully!";
                TempData["ToastType"] = "success";
            }

            return RedirectToAction(nameof(Index));
        }
    }
}
