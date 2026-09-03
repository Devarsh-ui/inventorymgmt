using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using InventoryManagementSystem.ViewModels;
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
    }
}
