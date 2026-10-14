using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using InventoryManagementSystem.ViewModels;
using InventoryManagementSystem.Data;
using MongoDB.Driver;
using System.Security.Claims;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ICategoryRepository _categoryRepository;
        private readonly IProductRepository _productRepository;
        private readonly ISaleRepository _saleRepository;
        private readonly IAuditLogService _auditLogService;
        private readonly INotificationRepository _notificationRepository;
        private readonly ISupplierService _supplierService;

        public HomeController(
            ICategoryRepository categoryRepository,
            IProductRepository productRepository,
            ISaleRepository saleRepository,
            IAuditLogService auditLogService,
            INotificationRepository notificationRepository,
            ISupplierService supplierService)
        {
            _categoryRepository = categoryRepository;
            _productRepository = productRepository;
            _saleRepository = saleRepository;
            _auditLogService = auditLogService;
            _notificationRepository = notificationRepository;
            _supplierService = supplierService;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                await _supplierService.CleanupOrphanedSupplierDataAsync();

                var todayUtc = DateTime.UtcNow.Date;
                var firstOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

                // Run all independent queries in parallel using Task.WhenAll
                var categoryCountTask = _categoryRepository.CountAsync(c => c.SupplierId == null);
                var stockMetricsTask = _productRepository.GetStockMetricsAsync();
                var salesMetricsTask = _saleRepository.GetDashboardSalesMetricsAsync(todayUtc, firstOfMonth, new Dictionary<string, decimal>());
                var recentLogsTask = _auditLogService.GetRecentActivityAsync(6);
                var notificationsTask = _notificationRepository.GetUnreadNotificationsAsync();

                await Task.WhenAll(
                    categoryCountTask,
                    stockMetricsTask,
                    salesMetricsTask,
                    recentLogsTask,
                    notificationsTask
                );

                var totalCategories = (int)await categoryCountTask;
                var stockMetrics = await stockMetricsTask;
                var salesMetrics = await salesMetricsTask;
                var recentLogs = (await recentLogsTask) ?? new List<AuditLog>();
                var unreadNotifications = (await notificationsTask) ?? new List<Notification>();

                var viewModel = new DashboardViewModel
                {
                    TotalCategories = totalCategories,
                    TotalProducts = stockMetrics.TotalProducts,
                    CurrentStock = stockMetrics.CurrentStockSum,
                    TodaysSales = salesMetrics.TodaysSales,
                    MonthlySales = salesMetrics.MonthlySales,
                    Revenue = salesMetrics.MonthlySales,
                    Profit = salesMetrics.MonthlyProfit,
                    LowStockCount = stockMetrics.LowStockCount,
                    OutOfStockCount = stockMetrics.OutOfStockCount,
                    RecentActivities = recentLogs,
                    UnreadNotifications = unreadNotifications
                };

                return View(viewModel);
            }
            catch (Exception)
            {
                var fallbackModel = new DashboardViewModel
                {
                    RecentActivities = new List<AuditLog>(),
                    UnreadNotifications = new List<Notification>()
                };
                return View(fallbackModel);
            }
        }

        [AllowAnonymous]
        public IActionResult Error(string? message)
        {
            var requestId = System.Diagnostics.Activity.Current?.Id ?? HttpContext.TraceIdentifier;
            var model = new ErrorViewModel
            {
                RequestId = requestId,
                ExceptionMessage = message
            };

            return View("~/Views/Shared/Error.cshtml", model);
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> Maintenance([FromServices] IPlatformSettingsService platformSettingsService)
        {
            var (isMaint, msg, endTime) = await platformSettingsService.CheckShopDashboardMaintenanceAsync();
            if (!isMaint)
            {
                return RedirectToAction("Index", "Home");
            }

            var settings = await platformSettingsService.GetSettingsAsync();
            ViewBag.Settings = settings;
            return View();
        }

        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> ShopMaintenanceStatus([FromServices] IPlatformSettingsService platformSettingsService)
        {
            var (isMaint, msg, endTime) = await platformSettingsService.CheckShopDashboardMaintenanceAsync();
            return Json(new { isInMaintenance = isMaint });
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Suspended([FromServices] IPlatformSettingsService platformSettingsService, [FromServices] MongoDbContext context)
        {
            var tenantId = User.FindFirst("TenantId")?.Value ?? User.FindFirst(ClaimTypes.GroupSid)?.Value;
            if (string.IsNullOrEmpty(tenantId) && User.IsInRole(Role.Supplier))
            {
                var supplierId = User.FindFirst("SupplierId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(supplierId))
                {
                    var supp = await context.Suppliers.Find(s => s.Id == supplierId).FirstOrDefaultAsync();
                    tenantId = supp?.TenantId;
                }
            }

            var tenant = !string.IsNullOrEmpty(tenantId)
                ? await context.Tenants.Find(t => t.Id == tenantId || t.TenantCode == tenantId).FirstOrDefaultAsync()
                : null;

            var platformSettings = await platformSettingsService.GetSettingsAsync();
            ViewBag.PlatformSettings = platformSettings;
            ViewBag.Tenant = tenant;
            ViewBag.Role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            return View("~/Views/Shared/ShopSuspended.cshtml");
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> LicenseExpired([FromServices] IPlatformSettingsService platformSettingsService, [FromServices] MongoDbContext context)
        {
            var tenantId = User.FindFirst("TenantId")?.Value ?? User.FindFirst(ClaimTypes.GroupSid)?.Value;
            if (string.IsNullOrEmpty(tenantId) && User.IsInRole(Role.Supplier))
            {
                var supplierId = User.FindFirst("SupplierId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(supplierId))
                {
                    var supp = await context.Suppliers.Find(s => s.Id == supplierId).FirstOrDefaultAsync();
                    tenantId = supp?.TenantId;
                }
            }

            var tenant = !string.IsNullOrEmpty(tenantId)
                ? await context.Tenants.Find(t => t.Id == tenantId || t.TenantCode == tenantId).FirstOrDefaultAsync()
                : null;

            var platformSettings = await platformSettingsService.GetSettingsAsync();
            ViewBag.PlatformSettings = platformSettings;
            ViewBag.Tenant = tenant;
            ViewBag.Role = User.FindFirst(ClaimTypes.Role)?.Value ?? string.Empty;

            return View("~/Views/Shared/LicenseExpired.cshtml");
        }
    }
}
