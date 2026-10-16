using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using InventoryManagementSystem.Data;
using MongoDB.Driver;
using System;
using System.Threading.Tasks;
using System.Security.Claims;
using System.Linq;

namespace InventoryManagementSystem.Controllers
{
    [Authorize]
    public class SubscriptionController : Controller
    {
        private readonly ILicenseService _licenseService;
        private readonly IFeatureCatalogService _featureCatalogService;
        private readonly IPlatformSettingsService _platformSettingsService;
        private readonly MongoDbContext _context;
        private readonly IAuditLogService _auditLogService;

        public SubscriptionController(
            ILicenseService licenseService,
            IFeatureCatalogService featureCatalogService,
            IPlatformSettingsService platformSettingsService,
            MongoDbContext context,
            IAuditLogService auditLogService)
        {
            _licenseService = licenseService;
            _featureCatalogService = featureCatalogService;
            _platformSettingsService = platformSettingsService;
            _context = context;
            _auditLogService = auditLogService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            return RedirectToAction(nameof(MyPlan));
        }

        [HttpGet]
        public async Task<IActionResult> MyPlan()
        {
            var tenantId = User.FindFirst("TenantId")?.Value ?? User.FindFirst(ClaimTypes.GroupSid)?.Value;
            if (string.IsNullOrEmpty(tenantId))
            {
                TempData["ErrorMessage"] = "No tenant context found for this account.";
                return RedirectToAction("Index", "Home");
            }

            var tenant = await _context.Tenants.Find(t => t.Id == tenantId).FirstOrDefaultAsync();
            var currentPackage = await _licenseService.GetTenantPackageAsync(tenantId);
            var activeLicense = await _licenseService.GetActiveLicenseAsync(tenantId);
            var allFeatures = await _featureCatalogService.GetActiveFeaturesAsync();
            var platformSettings = await _platformSettingsService.GetSettingsAsync();

            // Resource capacity & live usage counts
            var adminCount = await _context.Users.CountDocumentsAsync(u => u.TenantId == tenantId && u.Role == Role.Admin);
            var employeeCount = await _context.Users.CountDocumentsAsync(u => u.TenantId == tenantId && u.Role != Role.Admin && u.Role != Role.SuperAdmin);
            var productCount = await _context.Products.CountDocumentsAsync(p => p.TenantId == tenantId);
            var supplierCount = await _context.Suppliers.CountDocumentsAsync(s => s.TenantId == tenantId);
            var customerCount = await _context.Customers.CountDocumentsAsync(c => c.TenantId == tenantId);
            var salesCount = await _context.Sales.CountDocumentsAsync(s => s.TenantId == tenantId);

            ViewBag.Tenant = tenant;
            ViewBag.ActiveLicense = activeLicense;
            ViewBag.CurrentPackage = currentPackage;
            ViewBag.AllFeatures = allFeatures;
            ViewBag.PlatformSettings = platformSettings;
            ViewBag.AdminCount = adminCount;
            ViewBag.EmployeeCount = employeeCount;
            ViewBag.ProductCount = productCount;
            ViewBag.SupplierCount = supplierCount;
            ViewBag.CustomerCount = customerCount;
            ViewBag.SalesCount = salesCount;

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Plans()
        {
            var tenantId = User.FindFirst("TenantId")?.Value ?? User.FindFirst(ClaimTypes.GroupSid)?.Value;
            var currentPackage = await _licenseService.GetTenantPackageAsync(tenantId ?? string.Empty);
            var activeLicense = await _licenseService.GetActiveLicenseAsync(tenantId ?? string.Empty);
            var allPackages = await _licenseService.GetPublicPackagesAsync();
            var groupedFeatures = await _featureCatalogService.GetGroupedFeaturesAsync();
            var allFeatures = await _featureCatalogService.GetActiveFeaturesAsync();
            var platformSettings = await _platformSettingsService.GetSettingsAsync();

            ViewBag.CurrentPackage = currentPackage;
            ViewBag.ActiveLicense = activeLicense;
            ViewBag.GroupedFeatures = groupedFeatures;
            ViewBag.AllFeatures = allFeatures;
            ViewBag.PlatformSettings = platformSettings;

            return View(allPackages);
        }

        [HttpGet]
        public async Task<IActionResult> Renew(string? packageId)
        {
            var tenantId = User.FindFirst("TenantId")?.Value ?? User.FindFirst(ClaimTypes.GroupSid)?.Value;
            if (string.IsNullOrEmpty(tenantId))
            {
                TempData["ErrorMessage"] = "No tenant context found for this account.";
                return RedirectToAction("Index", "Home");
            }

            var tenant = await _context.Tenants.Find(t => t.Id == tenantId).FirstOrDefaultAsync();
            if (tenant == null) return NotFound("Tenant shop not found.");

            var currentPackage = await _licenseService.GetTenantPackageAsync(tenantId);
            var activeLicense = await _licenseService.GetActiveLicenseAsync(tenantId);
            var publicPackages = await _licenseService.GetPublicPackagesAsync();
            var platformSettings = await _platformSettingsService.GetSettingsAsync();
            var allFeatures = await _featureCatalogService.GetActiveFeaturesAsync();

            SubscriptionPackage selectedPackage = currentPackage ?? publicPackages.FirstOrDefault() ?? new SubscriptionPackage { Name = "Basic Plan", MonthlyPrice = 499 };
            if (!string.IsNullOrEmpty(packageId))
            {
                var pkgMatch = await _context.SubscriptionPackages.Find(p => p.Id == packageId).FirstOrDefaultAsync();
                if (pkgMatch != null)
                {
                    selectedPackage = pkgMatch;
                }
            }

            // Check for any pending renewal requests
            var pendingRequest = await _context.LicenseRenewalRequests
                .Find(r => r.TenantId == tenantId && r.Status == "Pending")
                .SortByDescending(r => r.RequestedAt)
                .FirstOrDefaultAsync();

            ViewBag.Tenant = tenant;
            ViewBag.ActiveLicense = activeLicense;
            ViewBag.CurrentPackage = currentPackage;
            ViewBag.SelectedPackage = selectedPackage;
            ViewBag.PublicPackages = publicPackages;
            ViewBag.PlatformSettings = platformSettings;
            ViewBag.AllFeatures = allFeatures;
            ViewBag.PendingRequest = pendingRequest;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Renew(
            string packageId, 
            string requestedCycle, 
            int durationMonths, 
            string? contactPhone, 
            string? requestMessage, 
            string? paymentMode)
        {
            var tenantId = User.FindFirst("TenantId")?.Value ?? User.FindFirst(ClaimTypes.GroupSid)?.Value;
            if (string.IsNullOrEmpty(tenantId))
            {
                TempData["ErrorMessage"] = "No tenant context found for this account.";
                return RedirectToAction("Index", "Home");
            }

            var tenant = await _context.Tenants.Find(t => t.Id == tenantId).FirstOrDefaultAsync();
            if (tenant == null) return NotFound("Tenant shop not found.");

            var activeLicense = await _licenseService.GetActiveLicenseAsync(tenantId);
            var targetPackage = await _context.SubscriptionPackages.Find(p => p.Id == packageId).FirstOrDefaultAsync() 
                                ?? await _licenseService.GetTenantPackageAsync(tenantId);

            if (targetPackage == null)
            {
                TempData["ToastMessage"] = "Please select a valid subscription package.";
                TempData["ToastType"] = "warning";
                return RedirectToAction(nameof(Renew));
            }

            if (durationMonths <= 0) durationMonths = 1;
            int calculatedDays = requestedCycle switch
            {
                "Yearly" => 365,
                "Semi-Annual" => 180,
                "Quarterly" => 90,
                _ => durationMonths * 30
            };

            // Build package limits summary string
            string limitsSummary = $"Admins: {(targetPackage.MaxAdmins > 0 ? targetPackage.MaxAdmins.ToString() : "Unlimited")}, " +
                                  $"Staff: {(targetPackage.MaxEmployees > 0 ? targetPackage.MaxEmployees.ToString() : "Unlimited")}, " +
                                  $"Products: {(targetPackage.MaxProducts > 0 ? targetPackage.MaxProducts.ToString() : "Unlimited")}, " +
                                  $"Suppliers: {(targetPackage.MaxSuppliers > 0 ? targetPackage.MaxSuppliers.ToString() : "Unlimited")}";

            var renewalRequest = new LicenseRenewalRequest
            {
                TenantId = tenant.Id,
                TenantCode = tenant.TenantCode,
                ShopName = tenant.ShopName,
                OwnerName = tenant.OwnerName,
                ContactEmail = tenant.ContactEmail,
                ContactPhone = !string.IsNullOrWhiteSpace(contactPhone) ? contactPhone.Trim() : tenant.Phone,
                CurrentStatus = tenant.Status,
                CurrentExpiryDate = activeLicense?.ExpiryDate,
                PackageId = targetPackage.Id,
                PackageName = targetPackage.Name,
                PackageMonthlyPrice = targetPackage.MonthlyPrice,
                PackageYearlyPrice = targetPackage.YearlyPrice > 0 ? targetPackage.YearlyPrice : (targetPackage.MonthlyPrice * 12),
                IsCustomPackage = targetPackage.IsCustom || !string.IsNullOrEmpty(targetPackage.AssignedTenantId) || targetPackage.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase),
                PackageFeatures = targetPackage.EnabledFeatures ?? targetPackage.EnabledModules ?? new List<string>(),
                PackageLimitsSummary = limitsSummary,
                RequestedCycle = requestedCycle ?? "Monthly",
                RequestedDurationMonths = durationMonths,
                RequestedDays = calculatedDays,
                RequestMessage = requestMessage?.Trim() ?? string.Empty,
                PaymentModePreference = paymentMode ?? "UPI / Online",
                Status = "Pending",
                RequestedAt = DateTime.UtcNow
            };

            await _context.LicenseRenewalRequests.InsertOneAsync(renewalRequest);

            // Audit Log
            await _auditLogService.LogActivityAsync(
                "SUBSCRIPTION_RENEWAL_REQUESTED",
                User.Identity?.Name ?? tenant.OwnerName,
                $"Shop: {tenant.ShopName} ({tenant.TenantCode})",
                $"Submitted renewal request for package '{targetPackage.Name}' ({requestedCycle}, {calculatedDays} days). Notes: {requestMessage}");

            TempData["ToastMessage"] = $"Your renewal request for '{targetPackage.Name}' has been submitted to SaaS Support! We will review and activate your license shortly.";
            TempData["ToastType"] = "success";

            return RedirectToAction(nameof(Renew));
        }
    }
}
