using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers
{
    [Authorize(Roles = Role.SuperAdmin)]
    public class InventoryAlertController : Controller
    {
        private readonly IInventoryAlertService _alertService;
        private readonly IAuditLogService _auditLogService;

        public InventoryAlertController(
            IInventoryAlertService alertService,
            IAuditLogService auditLogService)
        {
            _alertService = alertService;
            _auditLogService = auditLogService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(
            string? keyword,
            string? alertType,
            string? status,
            int page = 1)
        {
            const int pageSize = 15;
            var settings = await _alertService.GetSettingsAsync();
            var stats = await _alertService.GetDashboardStatsAsync();
            var (logs, totalCount) = await _alertService.GetFilteredLogsAsync(keyword, alertType, status, page, pageSize);

            ViewBag.Settings = settings;
            ViewBag.Stats = stats;
            ViewBag.Keyword = keyword ?? "";
            ViewBag.AlertType = alertType ?? "";
            ViewBag.Status = status ?? "";
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = (int)global::System.Math.Ceiling((double)totalCount / pageSize);

            return View(logs);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateSettings(InventoryAlertSettings settings, string? recipientsRaw)
        {
            var existingSettings = await _alertService.GetSettingsAsync();

            existingSettings.AdminEmail = settings.AdminEmail?.Trim() ?? "admin@sims.com";
            existingSettings.LowStockThreshold = settings.LowStockThreshold > 0 ? settings.LowStockThreshold : 5;
            existingSettings.EnableLowStockAlerts = settings.EnableLowStockAlerts;
            existingSettings.EnableOutOfStockAlerts = settings.EnableOutOfStockAlerts;
            existingSettings.EnableStockRestoredAlerts = settings.EnableStockRestoredAlerts;
            existingSettings.EnableDailySummary = settings.EnableDailySummary;
            existingSettings.NotificationFrequency = settings.NotificationFrequency ?? "Immediate";
            existingSettings.UpdatedBy = User.Identity?.Name ?? "Admin";

            if (!string.IsNullOrWhiteSpace(recipientsRaw))
            {
                var list = recipientsRaw.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(r => r.Trim())
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                existingSettings.AlertRecipients = list;
            }
            else
            {
                existingSettings.AlertRecipients = new List<string> { existingSettings.AdminEmail };
            }

            await _alertService.SaveSettingsAsync(existingSettings);

            await _auditLogService.LogEmployeeActivityAsync(
                action: "Update Alert Settings",
                module: "InventoryAlert",
                target: "System Alert Settings",
                details: $"Updated low stock threshold to {existingSettings.LowStockThreshold}, admin email to {existingSettings.AdminEmail}."
            );

            TempData["SuccessMessage"] = "Inventory alert settings updated successfully!";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendTestEmail(string recipientEmail)
        {
            if (string.IsNullOrWhiteSpace(recipientEmail))
            {
                return Json(new { success = false, message = "Recipient email address is required." });
            }

            var (success, message) = await _alertService.SendTestEmailAsync(recipientEmail.Trim());

            if (success)
            {
                await _auditLogService.LogEmployeeActivityAsync(
                    action: "Send Test Email",
                    module: "InventoryAlert",
                    target: recipientEmail.Trim(),
                    details: "Triggered Brevo REST API connection test email."
                );
            }

            return Json(new { success = success, message = message });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendEmail(string logId)
        {
            if (string.IsNullOrWhiteSpace(logId))
            {
                return Json(new { success = false, message = "Invalid log ID." });
            }

            var (success, message) = await _alertService.ResendEmailLogAsync(logId);
            return Json(new { success = success, message = message });
        }
    }
}
