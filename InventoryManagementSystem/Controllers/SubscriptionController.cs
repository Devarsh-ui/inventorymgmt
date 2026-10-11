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
        private readonly MongoDbContext _context;

        public SubscriptionController(
            ILicenseService licenseService,
            IFeatureCatalogService featureCatalogService,
            MongoDbContext context)
        {
            _licenseService = licenseService;
            _featureCatalogService = featureCatalogService;
            _context = context;
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

            ViewBag.CurrentPackage = currentPackage;
            ViewBag.ActiveLicense = activeLicense;
            ViewBag.GroupedFeatures = groupedFeatures;
            ViewBag.AllFeatures = allFeatures;

            return View(allPackages);
        }
    }
}
