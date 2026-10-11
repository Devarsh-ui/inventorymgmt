using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using InventoryManagementSystem.Data;
using MongoDB.Driver;
using System;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers
{
    [AllowAnonymous]
    public class LandingController : Controller
    {
        private readonly ILicenseService _licenseService;
        private readonly MongoDbContext _context;
        private readonly IEmailService _emailService;
        private readonly IFeatureCatalogService _featureCatalogService;
        private readonly IPlatformSettingsService _platformSettingsService;

        public LandingController(
            ILicenseService licenseService,
            MongoDbContext context,
            IEmailService emailService,
            IFeatureCatalogService featureCatalogService,
            IPlatformSettingsService platformSettingsService)
        {
            _licenseService = licenseService;
            _context = context;
            _emailService = emailService;
            _featureCatalogService = featureCatalogService;
            _platformSettingsService = platformSettingsService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                if (User.IsInRole(Role.SuperAdmin))
                {
                    return RedirectToAction("Dashboard", "PlatformAdmin");
                }
                if (User.IsInRole(Role.Supplier))
                {
                    return RedirectToAction("Index", "SupplierDashboard");
                }
                return RedirectToAction("Index", "Home");
            }

            var packages = await _licenseService.GetPublicPackagesAsync();
            var settings = await _platformSettingsService.GetSettingsAsync();
            ViewBag.Packages = packages;
            ViewBag.Settings = settings;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Pricing()
        {
            var packages = await _licenseService.GetPublicPackagesAsync();
            var settings = await _platformSettingsService.GetSettingsAsync();
            ViewBag.GroupedFeatures = await _featureCatalogService.GetGroupedFeaturesAsync();
            ViewBag.AllFeatures = await _featureCatalogService.GetActiveFeaturesAsync();
            ViewBag.Settings = settings;
            return View(packages);
        }

        [HttpGet]
        public async Task<IActionResult> Features()
        {
            var settings = await _platformSettingsService.GetSettingsAsync();
            ViewBag.Settings = settings;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Contact()
        {
            var settings = await _platformSettingsService.GetSettingsAsync();
            ViewBag.Settings = settings;
            return View();
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Maintenance()
        {
            var (isMaint, msg, endTime) = await _platformSettingsService.CheckWebsiteMaintenanceAsync("HomePage");
            if (!isMaint)
            {
                return RedirectToAction("Index", "Landing");
            }

            var settings = await _platformSettingsService.GetSettingsAsync();
            ViewBag.Settings = settings;
            return View();
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> MaintenanceStatus()
        {
            var (isMaint, msg, endTime) = await _platformSettingsService.CheckWebsiteMaintenanceAsync("HomePage");
            return Json(new { isInMaintenance = isMaint });
        }

        [HttpGet]
        public async Task<IActionResult> Register(string? packageId = null)
        {
            ViewBag.Packages = await _licenseService.GetPublicPackagesAsync();
            ViewBag.SelectedPackageId = packageId;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterShop(
            string shopName, string ownerName, string contactEmail, string phone,
            string address, string gstin, string packageId, string password)
        {
            if (string.IsNullOrWhiteSpace(shopName) || string.IsNullOrWhiteSpace(contactEmail) || string.IsNullOrWhiteSpace(password))
            {
                TempData["ToastMessage"] = "Please fill in all required shop registration fields.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(Register), new { packageId });
            }

            // Check if user email already exists
            var existingUser = await _context.Users.Find(u => u.Email == contactEmail || u.Username == contactEmail).FirstOrDefaultAsync();
            if (existingUser != null)
            {
                TempData["ToastMessage"] = "An account with this email address already exists. Please login instead.";
                TempData["ToastType"] = "warning";
                return RedirectToAction("Login", "Account");
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

            // Create Clean Shop Admin User
            var shopAdmin = new User
            {
                TenantId = tenant.Id,
                Username = contactEmail,
                Email = contactEmail,
                FullName = ownerName,
                PhoneNumber = phone,
                Role = Role.Admin,
                EmployeeId = "EMP-0001",
                IsLocked = false,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };

            await _context.Users.InsertOneAsync(shopAdmin);

            // Create 14-Day Free Trial / Active License
            await _licenseService.CreateTrialLicenseAsync(tenant.Id, packageId, 14);

            // Send Welcome Confirmation Email
            try
            {
                var welcomeSubject = $"Welcome to SIMS SaaS — {shopName} Account Created";
                var welcomeBody = $@"
                    <div style='font-family: Arial, sans-serif; background-color: #0f172a; color: #f8fafc; padding: 30px; border-radius: 10px;'>
                        <h2 style='color: #38bdf8;'>Welcome to SIMS SaaS, {ownerName}!</h2>
                        <p>Your shop <strong>{shopName}</strong> (Code: <code>{tenantCode}</code>) has been successfully created and your 14-day free trial is now active.</p>
                        <div style='background-color: #1e293b; padding: 20px; border-radius: 8px; margin: 20px 0;'>
                            <h4 style='color: #e2e8f0; margin-top: 0;'>Your Login Credentials:</h4>
                            <p style='margin-bottom: 5px;'><strong>Login Email:</strong> {contactEmail}</p>
                            <p style='margin-top: 0;'><strong>Shop Code:</strong> {tenantCode}</p>
                        </div>
                        <p>Click below to sign in and set up your mobile inventory, products, and staff:</p>
                        <p><a href='{Url.Action("Login", "Account", null, Request.Scheme)}' style='display: inline-block; background-color: #0284c7; color: white; padding: 12px 24px; text-decoration: none; border-radius: 6px; font-weight: bold;'>Sign In to Your Workspace</a></p>
                        <hr style='border-color: #334155;' />
                        <p style='font-size: 12px; color: #94a3b8;'>SIMS Multi-Tenant SaaS Platform</p>
                    </div>";

                await _emailService.SendEmailAsync(contactEmail, welcomeSubject, welcomeBody);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[REGISTRATION EMAIL NOTICE] {ex.Message}");
            }

            TempData["ToastMessage"] = $"Congratulations! Your shop '{shopName}' is registered successfully. A welcome email has been sent to {contactEmail}. You can now login.";
            TempData["ToastType"] = "success";

            return RedirectToAction("Login", "Account");
        }
    }
}
