using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using InventoryManagementSystem.Data;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers
{
    [Authorize(Roles = "SuperAdmin")]
    [Route("platform/[action]")]
    public class PlatformAdminController : Controller
    {
        private readonly MongoDbContext _context;
        private readonly ILicenseService _licenseService;
        private readonly IAuthService _authService;
        private readonly IFeatureCatalogService _featureCatalogService;
        private readonly IPlatformSettingsService _platformSettingsService;
        private readonly IImageService _imageService;
        private readonly IAuditLogService _auditLogService;

        public PlatformAdminController(
            MongoDbContext context,
            ILicenseService licenseService,
            IAuthService authService,
            IFeatureCatalogService featureCatalogService,
            IPlatformSettingsService platformSettingsService,
            IImageService imageService,
            IAuditLogService auditLogService)
        {
            _context = context;
            _licenseService = licenseService;
            _authService = authService;
            _featureCatalogService = featureCatalogService;
            _platformSettingsService = platformSettingsService;
            _imageService = imageService;
            _auditLogService = auditLogService;
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            var tenants = await _context.Tenants.Find(_ => true).ToListAsync();
            var licenses = await _context.TenantLicenses.Find(_ => true).ToListAsync();
            var packages = await _context.SubscriptionPackages.Find(_ => true).ToListAsync();
            var payments = await _context.SaaSPaymentTransactions.Find(_ => true).ToListAsync();

            ViewBag.TotalTenants = tenants.Count;
            ViewBag.ActiveTenants = tenants.Count(t => t.Status == "Active");
            ViewBag.TrialTenants = tenants.Count(t => t.Status == "Trial" || licenses.Any(l => l.TenantId == t.Id && l.Status == "Trial"));
            ViewBag.ExpiredTenants = licenses.Count(l => l.ExpiryDate < DateTime.UtcNow);

            var totalRevenue = payments.Where(p => p.Status == "Success").Sum(p => p.Amount);
            ViewBag.TotalRevenue = totalRevenue;

            // Estimated MRR based on active tenant packages
            decimal mrr = 0;
            foreach (var t in tenants.Where(t => t.Status == "Active"))
            {
                var pkg = packages.FirstOrDefault(p => p.Id == t.PackageId);
                if (pkg != null) mrr += pkg.MonthlyPrice;
            }
            ViewBag.MRR = mrr;

            ViewBag.RecentTenants = tenants.OrderByDescending(t => t.CreatedAt).Take(5).ToList();
            ViewBag.RecentPayments = payments.OrderByDescending(p => p.PaymentDate).Take(5).ToList();

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Tenants(string? search, string? statusFilter)
        {
            var filterBuilder = Builders<Tenant>.Filter;
            var filter = filterBuilder.Empty;

            if (!string.IsNullOrWhiteSpace(search))
            {
                filter &= filterBuilder.Or(
                    filterBuilder.Regex(t => t.ShopName, new MongoDB.Bson.BsonRegularExpression(search, "i")),
                    filterBuilder.Regex(t => t.OwnerName, new MongoDB.Bson.BsonRegularExpression(search, "i")),
                    filterBuilder.Regex(t => t.ContactEmail, new MongoDB.Bson.BsonRegularExpression(search, "i")),
                    filterBuilder.Regex(t => t.TenantCode, new MongoDB.Bson.BsonRegularExpression(search, "i"))
                );
            }

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                filter &= filterBuilder.Eq(t => t.Status, statusFilter);
            }

            var tenants = await _context.Tenants.Find(filter).SortByDescending(t => t.CreatedAt).ToListAsync();
            var licenses = await _context.TenantLicenses.Find(_ => true).ToListAsync();
            var packages = await _context.SubscriptionPackages.Find(_ => true).ToListAsync();

            var allSuppliers = await _context.Suppliers.Find(_ => true).ToListAsync();
            var allUsers = await _context.Users.Find(_ => true).ToListAsync();
            var allProducts = await _context.Products.Find(_ => true).ToListAsync();
            var allCustomers = await _context.Customers.Find(_ => true).ToListAsync();
            var allSales = await _context.Sales.Find(_ => true).ToListAsync();

            ViewBag.Licenses = licenses.ToDictionary(l => l.TenantId, l => l);
            ViewBag.Packages = packages.ToDictionary(p => p.Id, p => p);
            ViewBag.SupplierCounts = allSuppliers.Where(s => !string.IsNullOrEmpty(s.TenantId)).GroupBy(s => s.TenantId!).ToDictionary(g => g.Key, g => (long)g.Count());
            ViewBag.EmployeeCounts = allUsers.Where(u => !string.IsNullOrEmpty(u.TenantId) && u.Role != Role.Admin && u.Role != Role.SuperAdmin).GroupBy(u => u.TenantId!).ToDictionary(g => g.Key, g => (long)g.Count());
            ViewBag.ProductCounts = allProducts.Where(p => !string.IsNullOrEmpty(p.TenantId)).GroupBy(p => p.TenantId!).ToDictionary(g => g.Key, g => (long)g.Count());
            ViewBag.CustomerCounts = allCustomers.Where(c => !string.IsNullOrEmpty(c.TenantId)).GroupBy(c => c.TenantId!).ToDictionary(g => g.Key, g => (long)g.Count());
            ViewBag.SalesCounts = allSales.Where(s => !string.IsNullOrEmpty(s.TenantId)).GroupBy(s => s.TenantId!).ToDictionary(g => g.Key, g => (long)g.Count());

            return View(tenants);
        }

        [HttpGet]
        [Route("platform/TenantDetails/{id}")]
        public async Task<IActionResult> TenantDetails(string id)
        {
            var tenant = await _context.Tenants.Find(t => t.Id == id).FirstOrDefaultAsync();
            if (tenant == null)
            {
                TempData["ToastMessage"] = "Shop not found.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(Tenants));
            }

            var license = await _context.TenantLicenses.Find(l => l.TenantId == id).FirstOrDefaultAsync();
            var package = !string.IsNullOrEmpty(tenant.PackageId)
                ? await _context.SubscriptionPackages.Find(p => p.Id == tenant.PackageId).FirstOrDefaultAsync()
                : null;
            var allPackages = await _context.SubscriptionPackages.Find(p => !p.IsCustom).ToListAsync();
            var customPackages = await _context.SubscriptionPackages.Find(p => p.IsCustom).ToListAsync();

            var suppliers = await _context.Suppliers.Find(s => s.TenantId == id).ToListAsync();
            var users = await _context.Users.Find(u => u.TenantId == id && u.Role != Role.SuperAdmin).ToListAsync();
            var products = await _context.Products.Find(p => p.TenantId == id).ToListAsync();
            var customers = await _context.Customers.Find(c => c.TenantId == id).ToListAsync();
            var sales = await _context.Sales.Find(s => s.TenantId == id).ToListAsync();

            ViewBag.License = license;
            ViewBag.Package = package;
            ViewBag.Packages = allPackages;
            ViewBag.CustomPackages = customPackages;
            ViewBag.Suppliers = suppliers;
            ViewBag.Users = users;
            ViewBag.StaffCount = users.Count(u => u.Role != Role.Admin);
            ViewBag.ProductsCount = products.Count;
            ViewBag.CustomersCount = customers.Count;
            ViewBag.SalesCount = sales.Count;

            return View(tenant);
        }

        [HttpGet]
        public async Task<IActionResult> CreateTenant()
        {
            ViewBag.Packages = await _licenseService.GetStandardPackagesAsync();
            ViewBag.CustomPackages = await _licenseService.GetCustomPackagesAsync();
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTenant(
            string shopName, string ownerName, string contactEmail, string phone,
            string address, string gstin, string packageId, string adminUsername, string temporaryPassword, int durationDays = 30)
        {
            if (string.IsNullOrWhiteSpace(shopName) || string.IsNullOrWhiteSpace(contactEmail) || string.IsNullOrWhiteSpace(temporaryPassword))
            {
                TempData["ToastMessage"] = "Please fill in all required shop and admin credentials.";
                TempData["ToastType"] = "danger";
                ViewBag.Packages = await _licenseService.GetStandardPackagesAsync();
                ViewBag.CustomPackages = await _licenseService.GetCustomPackagesAsync();
                return View();
            }

            // Generate unique TenantCode
            var tenantCount = await _context.Tenants.CountDocumentsAsync(_ => true);
            var tenantCode = $"SHOP-{100 + tenantCount + 1}";

            var tenant = new Tenant
            {
                TenantCode = tenantCode,
                ShopName = shopName,
                OwnerName = ownerName,
                ContactEmail = contactEmail,
                Phone = phone,
                Address = address,
                GSTIN = gstin,
                PackageId = packageId,
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _context.Tenants.InsertOneAsync(tenant);

            // Create Clean Shop Admin User with newly created tenant.Id
            var shopAdmin = new User
            {
                TenantId = tenant.Id,
                Username = string.IsNullOrWhiteSpace(adminUsername) ? contactEmail : adminUsername,
                Email = contactEmail,
                FullName = ownerName,
                PhoneNumber = phone,
                Role = Role.Admin,
                EmployeeId = "EMP-0001",
                IsLocked = false,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword),
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };

            await _context.Users.InsertOneAsync(shopAdmin);

            // Create License for Shop
            await _licenseService.RenewOrExtendLicenseAsync(tenant.Id, packageId, durationDays);

            TempData["ToastMessage"] = $"New shop '{shopName}' ({tenantCode}) created successfully!";
            TempData["ToastType"] = "success";
            return RedirectToAction(nameof(Tenants));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleTenantStatus(string tenantId, string status, string? returnUrl = null)
        {
            var tenant = await _context.Tenants.Find(t => t.Id == tenantId).FirstOrDefaultAsync();
            if (tenant != null)
            {
                tenant.Status = status;
                tenant.UpdatedAt = DateTime.UtcNow;
                await _context.Tenants.ReplaceOneAsync(t => t.Id == tenant.Id, tenant);

                TempData["ToastMessage"] = $"Shop '{tenant.ShopName}' status updated to {status}.";
                TempData["ToastType"] = "info";
            }
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(TenantDetails), new { id = tenantId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteTenant(string tenantId, string adminPassword, string? returnUrl = null)
        {
            if (string.IsNullOrWhiteSpace(adminPassword))
            {
                TempData["ToastMessage"] = "Security Check Failed: Super Admin password is required to delete a shop.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(Tenants));
            }

            // Verify Super Admin Password
            var currentUsername = User.Identity?.Name;
            var superAdmin = await _context.Users.Find(u => u.Role == Role.SuperAdmin && (u.Username == currentUsername || u.Email == currentUsername)).FirstOrDefaultAsync();

            if (superAdmin == null)
            {
                superAdmin = await _context.Users.Find(u => u.Role == Role.SuperAdmin).FirstOrDefaultAsync();
            }

            if (superAdmin == null || string.IsNullOrEmpty(superAdmin.PasswordHash) || !BCrypt.Net.BCrypt.Verify(adminPassword, superAdmin.PasswordHash))
            {
                TempData["ToastMessage"] = "Security Check Failed: Incorrect Super Admin Password. Shop deletion cancelled.";
                TempData["ToastType"] = "danger";
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                return RedirectToAction(nameof(Tenants));
            }

            var tenant = await _context.Tenants.Find(t => t.Id == tenantId).FirstOrDefaultAsync();
            if (tenant == null)
            {
                TempData["ToastMessage"] = "Shop not found.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(Tenants));
            }

            // Cascade delete any custom packages created specifically for or assigned to this shop
            await _context.SubscriptionPackages.DeleteManyAsync(p => 
                p.AssignedTenantId == tenantId || 
                (p.Id == tenant.PackageId && (p.IsCustom || p.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase)))
            );

            // Clean up all collections for this tenant
            await _context.Tenants.DeleteOneAsync(t => t.Id == tenantId);
            await _context.TenantLicenses.DeleteManyAsync(l => l.TenantId == tenantId);
            await _context.Users.DeleteManyAsync(u => u.TenantId == tenantId && u.Role != Role.SuperAdmin);
            await _context.Suppliers.DeleteManyAsync(s => s.TenantId == tenantId);
            await _context.Products.DeleteManyAsync(p => p.TenantId == tenantId);
            await _context.Customers.DeleteManyAsync(c => c.TenantId == tenantId);
            await _context.Sales.DeleteManyAsync(s => s.TenantId == tenantId);
            await _context.Categories.DeleteManyAsync(c => c.TenantId == tenantId);
            await _context.Devices.DeleteManyAsync(d => d.TenantId == tenantId);
            await _context.StockTransactions.DeleteManyAsync(st => st.TenantId == tenantId);
            await _context.ReturnRecords.DeleteManyAsync(rr => rr.TenantId == tenantId);
            await _context.ExchangeRecords.DeleteManyAsync(er => er.TenantId == tenantId);
            await _context.RepairTickets.DeleteManyAsync(rt => rt.TenantId == tenantId);
            await _context.SupplierOrders.DeleteManyAsync(so => so.TenantId == tenantId);
            await _context.SupplierPurchaseReturns.DeleteManyAsync(spr => spr.TenantId == tenantId);
            await _context.AuditLogs.DeleteManyAsync(al => al.TenantId == tenantId);
            await _context.Notifications.DeleteManyAsync(n => n.TenantId == tenantId);
            await _context.Settings.DeleteManyAsync(s => s.TenantId == tenantId);
            await _context.SaaSPaymentTransactions.DeleteManyAsync(spt => spt.TenantId == tenantId);

            TempData["ToastMessage"] = $"Shop '{tenant.ShopName}' ({tenant.TenantCode}) and all associated data deleted successfully.";
            TempData["ToastType"] = "success";

            return RedirectToAction(nameof(Tenants));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ExtendLicense(string tenantId, string packageId, int extraDays, string? returnUrl = null)
        {
            await _licenseService.RenewOrExtendLicenseAsync(tenantId, packageId, extraDays);
            TempData["ToastMessage"] = $"License extended by {extraDays} days successfully.";
            TempData["ToastType"] = "success";
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(TenantDetails), new { id = tenantId });
        }

        [HttpGet]
        public async Task<IActionResult> Packages()
        {
            var packages = await _licenseService.GetStandardPackagesAsync();
            var tenants = await _context.Tenants.Find(_ => true).ToListAsync();

            var subscriberCounts = new Dictionary<string, int>();
            foreach (var pkg in packages)
            {
                subscriberCounts[pkg.Id] = tenants.Count(t => t.PackageId == pkg.Id);
            }
            ViewBag.SubscriberCounts = subscriberCounts;

            return View(packages);
        }

        [HttpGet]
        public async Task<IActionResult> CustomPackages()
        {
            var customPackages = await _licenseService.GetCustomPackagesAsync();
            var tenants = await _context.Tenants.Find(_ => true).ToListAsync();

            var tenantDict = tenants.ToDictionary(t => t.Id, t => t);
            ViewBag.Tenants = tenantDict;
            ViewBag.AllTenantsList = tenants;

            var subscriberCounts = new Dictionary<string, int>();
            foreach (var pkg in customPackages)
            {
                subscriberCounts[pkg.Id] = tenants.Count(t => t.PackageId == pkg.Id);
            }
            ViewBag.SubscriberCounts = subscriberCounts;
            ViewBag.GroupedFeatures = await _featureCatalogService.GetGroupedFeaturesAsync();

            return View(customPackages);
        }

        [HttpGet]
        public async Task<IActionResult> PackageDetails(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                TempData["ToastMessage"] = "Package ID is required.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(CustomPackages));
            }

            var package = await _context.SubscriptionPackages.Find(p => p.Id == id).FirstOrDefaultAsync();
            if (package == null)
            {
                TempData["ToastMessage"] = "Subscription package not found.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(CustomPackages));
            }

            // Find all shops subscribed to this package or explicitly assigned to it
            var subscribedShops = await _context.Tenants.Find(t => t.PackageId == id || t.Id == package.AssignedTenantId).ToListAsync();
            var allTenants = await _context.Tenants.Find(_ => true).ToListAsync();

            // Fetch active/recent licenses for subscribed shops
            var tenantIds = subscribedShops.Select(s => s.Id).ToList();
            var licenses = await _context.TenantLicenses.Find(l => tenantIds.Contains(l.TenantId)).ToListAsync();
            var licenseDict = licenses
                .GroupBy(l => l.TenantId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(l => l.ExpiryDate).FirstOrDefault());

            ViewBag.SubscribedShops = subscribedShops;
            ViewBag.Licenses = licenseDict;
            ViewBag.AllTenants = allTenants;
            ViewBag.GroupedFeatures = await _featureCatalogService.GetGroupedFeaturesAsync();

            Tenant? assignedTenant = null;
            if (!string.IsNullOrEmpty(package.AssignedTenantId))
            {
                assignedTenant = allTenants.FirstOrDefault(t => t.Id == package.AssignedTenantId);
            }
            ViewBag.AssignedTenant = assignedTenant;

            return View(package);
        }

        [HttpGet]
        public async Task<IActionResult> CreateCustomPackage(string? tenantId = null)
        {
            ViewBag.GroupedFeatures = await _featureCatalogService.GetGroupedFeaturesAsync();
            ViewBag.Tenants = await _context.Tenants.Find(_ => true).ToListAsync();
            ViewBag.TargetTenantId = tenantId;

            Tenant? targetTenant = null;
            if (!string.IsNullOrEmpty(tenantId))
            {
                targetTenant = await _context.Tenants.Find(t => t.Id == tenantId).FirstOrDefaultAsync();
            }
            ViewBag.TargetTenant = targetTenant;

            return View(new SubscriptionPackage
            {
                Name = targetTenant != null ? $"Custom Plan - {targetTenant.ShopName}" : "Custom VIP Plan",
                Description = targetTenant != null ? $"Exclusive custom subscription package tailored for {targetTenant.ShopName}" : "Exclusive shop-tailored subscription package",
                MonthlyPrice = 2999,
                YearlyPrice = 29990,
                TrialDays = 14,
                BillingCycle = "Monthly",
                MaxEmployees = 25,
                MaxProducts = 15000,
                MaxSuppliers = 250,
                IsActive = true,
                IsCustom = true,
                AssignedTenantId = tenantId,
                DisplayOrder = 99
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateCustomPackage(SubscriptionPackage package, List<string> selectedFeatures, bool allotNow = false, int extensionDays = 30)
        {
            if (string.IsNullOrWhiteSpace(package.Name))
            {
                ModelState.AddModelError("Name", "Custom Package Name is required.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.GroupedFeatures = await _featureCatalogService.GetGroupedFeaturesAsync();
                ViewBag.Tenants = await _context.Tenants.Find(_ => true).ToListAsync();
                return View(package);
            }

            package.IsCustom = true;
            package.EnabledFeatures = selectedFeatures ?? new List<string>();
            package.EnabledModules = package.EnabledFeatures;
            package.CreatedAt = DateTime.UtcNow;

            await _licenseService.CreateOrUpdatePackageAsync(package);

            if (allotNow && !string.IsNullOrEmpty(package.AssignedTenantId))
            {
                await _licenseService.RenewOrExtendLicenseAsync(package.AssignedTenantId, package.Id, extensionDays);
                TempData["ToastMessage"] = $"Custom Package '{package.Name}' created and immediately allotted to shop for {extensionDays} days!";
            }
            else
            {
                TempData["ToastMessage"] = $"Custom Package '{package.Name}' created successfully!";
            }

            TempData["ToastType"] = "success";
            return RedirectToAction(nameof(CustomPackages));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AllotCustomPackage(string tenantId, string packageId, int extensionDays = 30, string? returnUrl = null)
        {
            var tenant = await _context.Tenants.Find(t => t.Id == tenantId).FirstOrDefaultAsync();
            var package = await _context.SubscriptionPackages.Find(p => p.Id == packageId).FirstOrDefaultAsync();

            if (tenant == null || package == null)
            {
                TempData["ToastMessage"] = "Invalid shop or package selection.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(CustomPackages));
            }

            if (package.IsCustom && string.IsNullOrEmpty(package.AssignedTenantId))
            {
                package.AssignedTenantId = tenantId;
                await _context.SubscriptionPackages.ReplaceOneAsync(p => p.Id == package.Id, package);
            }

            await _licenseService.RenewOrExtendLicenseAsync(tenantId, packageId, extensionDays);

            TempData["ToastMessage"] = $"Custom Package '{package.Name}' allotted to '{tenant.ShopName}' for {extensionDays} days successfully.";
            TempData["ToastType"] = "success";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction(nameof(TenantDetails), new { id = tenantId });
        }

        [HttpGet]
        public async Task<IActionResult> CreatePackage()
        {
            ViewBag.GroupedFeatures = await _featureCatalogService.GetGroupedFeaturesAsync();
            return View(new SubscriptionPackage
            {
                MonthlyPrice = 1999,
                YearlyPrice = 19990,
                TrialDays = 14,
                BillingCycle = "Monthly",
                MaxEmployees = 10,
                MaxProducts = 5000,
                MaxSuppliers = 100,
                IsActive = true,
                DisplayOrder = 1
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePackage(SubscriptionPackage package, List<string> selectedFeatures)
        {
            if (string.IsNullOrWhiteSpace(package.Name))
            {
                ModelState.AddModelError("Name", "Package Name is required.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.GroupedFeatures = await _featureCatalogService.GetGroupedFeaturesAsync();
                return View(package);
            }

            package.EnabledFeatures = selectedFeatures ?? new List<string>();
            package.EnabledModules = package.EnabledFeatures;
            package.CreatedAt = DateTime.UtcNow;

            await _licenseService.CreateOrUpdatePackageAsync(package);

            TempData["ToastMessage"] = $"SaaS Package '{package.Name}' created successfully with {package.EnabledFeatures.Count} enabled features!";
            TempData["ToastType"] = "success";

            if (package.IsCustom || !string.IsNullOrEmpty(package.AssignedTenantId) || package.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(nameof(CustomPackages));
            }
            return RedirectToAction(nameof(Packages));
        }

        [HttpGet]
        public async Task<IActionResult> EditPackage(string id)
        {
            var package = await _context.SubscriptionPackages.Find(p => p.Id == id).FirstOrDefaultAsync();
            if (package == null) return NotFound();

            ViewBag.GroupedFeatures = await _featureCatalogService.GetGroupedFeaturesAsync();
            ViewBag.Tenants = await _context.Tenants.Find(_ => true).ToListAsync();
            return View(package);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditPackage(SubscriptionPackage package, List<string> selectedFeatures)
        {
            if (string.IsNullOrWhiteSpace(package.Name))
            {
                ModelState.AddModelError("Name", "Package Name is required.");
            }

            if (!ModelState.IsValid)
            {
                ViewBag.GroupedFeatures = await _featureCatalogService.GetGroupedFeaturesAsync();
                ViewBag.Tenants = await _context.Tenants.Find(_ => true).ToListAsync();
                return View(package);
            }

            var existing = await _context.SubscriptionPackages.Find(p => p.Id == package.Id).FirstOrDefaultAsync();
            if (existing != null)
            {
                // Guarantee custom package persistence
                if (existing.IsCustom || !string.IsNullOrEmpty(existing.AssignedTenantId) || existing.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase))
                {
                    package.IsCustom = true;
                    if (string.IsNullOrEmpty(package.AssignedTenantId))
                    {
                        package.AssignedTenantId = existing.AssignedTenantId;
                    }
                }
            }

            package.EnabledFeatures = selectedFeatures ?? new List<string>();
            package.EnabledModules = package.EnabledFeatures;

            await _licenseService.CreateOrUpdatePackageAsync(package);

            TempData["ToastMessage"] = $"Package '{package.Name}' updated successfully!";
            TempData["ToastType"] = "success";

            if (package.IsCustom || !string.IsNullOrEmpty(package.AssignedTenantId) || package.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(nameof(CustomPackages));
            }

            return RedirectToAction(nameof(Packages));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SavePackage(SubscriptionPackage package, List<string> selectedFeatures, string enabledModulesCsv)
        {
            if (selectedFeatures != null && selectedFeatures.Any())
            {
                package.EnabledFeatures = selectedFeatures;
                package.EnabledModules = selectedFeatures;
            }
            else if (!string.IsNullOrWhiteSpace(enabledModulesCsv))
            {
                package.EnabledFeatures = enabledModulesCsv
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                package.EnabledModules = package.EnabledFeatures;
            }

            var existing = await _context.SubscriptionPackages.Find(p => p.Id == package.Id).FirstOrDefaultAsync();
            if (existing != null && (existing.IsCustom || !string.IsNullOrEmpty(existing.AssignedTenantId) || existing.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase)))
            {
                package.IsCustom = true;
                if (string.IsNullOrEmpty(package.AssignedTenantId))
                {
                    package.AssignedTenantId = existing.AssignedTenantId;
                }
            }

            await _licenseService.CreateOrUpdatePackageAsync(package);
            TempData["ToastMessage"] = $"Subscription Package '{package.Name}' saved successfully.";
            TempData["ToastType"] = "success";

            if (package.IsCustom || !string.IsNullOrEmpty(package.AssignedTenantId) || package.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase))
            {
                return RedirectToAction(nameof(CustomPackages));
            }
            return RedirectToAction(nameof(Packages));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DuplicatePackage(string id)
        {
            var source = await _context.SubscriptionPackages.Find(p => p.Id == id).FirstOrDefaultAsync();
            if (source == null) return NotFound();

            var isCustom = source.IsCustom || !string.IsNullOrEmpty(source.AssignedTenantId) || source.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase);

            var clone = new SubscriptionPackage
            {
                Name = $"{source.Name} (Copy)",
                Description = source.Description,
                MonthlyPrice = source.MonthlyPrice,
                YearlyPrice = source.YearlyPrice,
                TrialDays = source.TrialDays,
                BillingCycle = source.BillingCycle,
                MaxAdmins = source.MaxAdmins,
                MaxEmployees = source.MaxEmployees,
                MaxProducts = source.MaxProducts,
                MaxSuppliers = source.MaxSuppliers,
                EnabledFeatures = new List<string>(source.EnabledFeatures ?? new List<string>()),
                EnabledModules = new List<string>(source.EnabledModules ?? new List<string>()),
                IsActive = true,
                IsFeatured = false,
                IsCustom = isCustom,
                AssignedTenantId = source.AssignedTenantId,
                DisplayOrder = source.DisplayOrder + 1,
                CreatedAt = DateTime.UtcNow
            };

            await _context.SubscriptionPackages.InsertOneAsync(clone);

            TempData["ToastMessage"] = $"Duplicated package '{source.Name}' into '{clone.Name}'.";
            TempData["ToastType"] = "info";

            if (isCustom)
            {
                return RedirectToAction(nameof(CustomPackages));
            }
            return RedirectToAction(nameof(Packages));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> TogglePackageActive(string id, string? returnUrl = null)
        {
            var pkg = await _context.SubscriptionPackages.Find(p => p.Id == id).FirstOrDefaultAsync();
            if (pkg != null)
            {
                pkg.IsActive = !pkg.IsActive;
                await _context.SubscriptionPackages.ReplaceOneAsync(p => p.Id == id, pkg);
                TempData["ToastMessage"] = $"Package '{pkg.Name}' is now {(pkg.IsActive ? "Active" : "Inactive")}.";
                TempData["ToastType"] = "info";
            }

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            var isCustom = pkg != null && (pkg.IsCustom || !string.IsNullOrEmpty(pkg.AssignedTenantId) || pkg.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase));
            return isCustom ? RedirectToAction(nameof(CustomPackages)) : RedirectToAction(nameof(Packages));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeletePackage(string id, string? returnUrl = null)
        {
            var pkg = await _context.SubscriptionPackages.Find(p => p.Id == id).FirstOrDefaultAsync();
            if (pkg == null)
            {
                TempData["ToastMessage"] = "Package not found.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(Packages));
            }

            var isCustom = pkg.IsCustom || !string.IsNullOrEmpty(pkg.AssignedTenantId) || pkg.Name.Contains("Custom", StringComparison.OrdinalIgnoreCase);

            var tenantCount = await _context.Tenants.CountDocumentsAsync(t => t.PackageId == id);
            if (tenantCount > 0)
            {
                TempData["ToastMessage"] = $"Cannot delete package: {tenantCount} registered shop(s) are currently subscribed to this package.";
                TempData["ToastType"] = "danger";
                return isCustom ? RedirectToAction(nameof(CustomPackages)) : RedirectToAction(nameof(Packages));
            }

            await _context.SubscriptionPackages.DeleteOneAsync(p => p.Id == id);
            TempData["ToastMessage"] = $"Package '{pkg.Name}' deleted successfully.";
            TempData["ToastType"] = "success";

            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return isCustom ? RedirectToAction(nameof(CustomPackages)) : RedirectToAction(nameof(Packages));
        }

        [HttpGet]
        public async Task<IActionResult> Payments()
        {
            var transactions = await _context.SaaSPaymentTransactions
                .Find(_ => true)
                .SortByDescending(p => p.PaymentDate)
                .ToListAsync();

            var tenants = await _context.Tenants.Find(_ => true).ToListAsync();
            ViewBag.Tenants = tenants.ToDictionary(t => t.Id, t => t.ShopName);

            return View(transactions);
        }

        [HttpGet]
        public async Task<IActionResult> Settings()
        {
            var settings = await _platformSettingsService.GetSettingsAsync();
            return View(settings);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveSettings(
            PlatformSettings model,
            List<string>? maintenancePages,
            Microsoft.AspNetCore.Http.IFormFile? logoFile)
        {
            if (logoFile != null && logoFile.Length > 0)
            {
                try
                {
                    var uploadResult = await _imageService.UploadImageAsync(logoFile, "platform_branding");
                    if (uploadResult != null && uploadResult.IsSuccess && !string.IsNullOrWhiteSpace(uploadResult.SecureUrl))
                    {
                        model.LogoUrl = uploadResult.SecureUrl;
                    }
                }
                catch (Exception)
                {
                    // keep existing LogoUrl if upload fails
                }
            }

            model.MaintenancePages = maintenancePages ?? new List<string>();

            await _platformSettingsService.SaveSettingsAsync(model, User.Identity?.Name ?? "SuperAdmin");
            await _auditLogService.LogActivityAsync("Platform Settings Updated", User.Identity?.Name ?? "SuperAdmin", "SaaS Platform Config", "Updated SaaS branding, contact details, and maintenance schedule.");

            TempData["ToastMessage"] = "SaaS Platform branding and maintenance configurations saved successfully!";
            TempData["ToastType"] = "success";

            return RedirectToAction(nameof(Settings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleMaintenance(string target, bool enabled)
        {
            var settings = await _platformSettingsService.GetSettingsAsync();
            if (target.Equals("website", StringComparison.OrdinalIgnoreCase))
            {
                settings.WebsiteMaintenanceEnabled = enabled;
            }
            else if (target.Equals("shop", StringComparison.OrdinalIgnoreCase))
            {
                settings.ShopDashboardMaintenanceEnabled = enabled;
            }

            await _platformSettingsService.SaveSettingsAsync(settings, User.Identity?.Name ?? "SuperAdmin");
            await _auditLogService.LogActivityAsync("Maintenance Mode Toggled", User.Identity?.Name ?? "SuperAdmin", "SaaS Maintenance", $"Toggled {target} maintenance mode to {enabled}.");

            TempData["ToastMessage"] = $"{(target.Equals("website", StringComparison.OrdinalIgnoreCase) ? "Website" : "Shop Dashboard")} maintenance mode {(enabled ? "activated" : "deactivated")} successfully!";
            TempData["ToastType"] = "success";

            return RedirectToAction(nameof(Settings));
        }
    }
}
