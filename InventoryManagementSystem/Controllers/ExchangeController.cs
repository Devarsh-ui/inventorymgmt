using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InventoryManagementSystem.Extensions;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using System;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers
{
    [Authorize]
    public class ExchangeController : Controller
    {
        private readonly IExchangeService _exchangeService;

        public ExchangeController(IExchangeService exchangeService)
        {
            _exchangeService = exchangeService;
        }

        [HttpGet]
        public IActionResult Create()
        {
            return RedirectToAction("Index", new { openModal = true });
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? search, string? brand, string? model, string? color, string? condition, string? destinationStatus, decimal? minValuation, decimal? maxValuation, int page = 1)
        {
            int pageSize = 20;
            var exchanges = await _exchangeService.GetFilteredExchangesAsync(search, brand, color, condition, destinationStatus, page, pageSize);
            var totalCount = await _exchangeService.GetFilteredCountExAsync(search, brand, color, condition, destinationStatus);

            ViewBag.Search = search;
            ViewBag.Brand = brand;
            ViewBag.Model = model;
            ViewBag.Color = color;
            ViewBag.Condition = condition;
            ViewBag.DestinationStatus = destinationStatus;
            ViewBag.MinValuation = minValuation;
            ViewBag.MaxValuation = maxValuation;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)System.Math.Ceiling((double)totalCount / pageSize);
            ViewBag.TotalCount = totalCount;

            return View(exchanges);
        }

        [HttpGet]
        public async Task<IActionResult> GetDetails(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return Json(new { success = false, message = "Trade-in ID is required." });

            var record = await _exchangeService.GetExchangeByIdAsync(id.Trim());
            if (record == null) return Json(new { success = false, message = "Trade-in record not found." });

            return Json(new
            {
                success = true,
                id = record.Id,
                exchangeNumber = record.ExchangeNumber,
                date = record.Date.ToIstString("yyyy-MM-dd HH:mm:ss IST"),
                customerName = record.CustomerName,
                customerPhone = record.CustomerPhone,
                customerEmail = record.CustomerEmail,
                customerAddress = record.CustomerAddress,
                customerId = record.CustomerId,
                brand = record.OldBrand,
                model = record.OldModel,
                color = record.OldColor,
                storage = record.OldStorage,
                ram = record.OldRam,
                imei1 = record.OldImei1,
                imei2 = record.OldImei2,
                serialNumber = record.SerialNumber,
                deviceType = record.DeviceType,
                simType = record.SimType,
                condition = record.Condition,
                screenCondition = record.ScreenCondition,
                bodyCondition = record.BodyCondition,
                cameraCondition = record.CameraCondition,
                speakerCondition = record.SpeakerCondition,
                microphoneCondition = record.MicrophoneCondition,
                chargingPortCondition = record.ChargingPortCondition,
                buttonsCondition = record.ButtonsCondition,
                faceIdFingerprintCondition = record.FaceIdFingerprintCondition,
                networkSimCondition = record.NetworkSimCondition,
                wifiBluetoothCondition = record.WifiBluetoothCondition,
                batteryHealthPercentage = record.BatteryHealthPercentage,
                batteryCondition = record.BatteryCondition,
                isDeviceUnlocked = record.IsDeviceUnlocked,
                appleActivationLock = record.AppleActivationLock,
                googleFRPAccountLock = record.GoogleFRPAccountLock,
                hasAccountLock = record.HasAccountLock,
                accessoriesReceived = record.AccessoriesReceived ?? new System.Collections.Generic.List<string>(),
                otherAccessoriesNotes = record.OtherAccessoriesNotes,
                originalPurchaseDate = record.OriginalPurchaseDate?.ToIstString("yyyy-MM-dd"),
                originalPurchaseInvoice = record.OriginalPurchaseInvoice,
                originalSupplierStore = record.OriginalSupplierStore,
                warrantyStatus = record.WarrantyStatus,
                warrantyExpiryDate = record.WarrantyExpiryDate?.ToIstString("yyyy-MM-dd"),
                hasPurchaseProof = record.HasPurchaseProof,
                estimatedValue = record.EstimatedValue,
                finalExchangeValue = record.FinalExchangeValue,
                remarks = record.Remarks,
                inventoryDestinationStatus = record.InventoryDestinationStatus,
                deviceId = record.DeviceId,
                productId = record.ProductId,
                inspectionStatus = record.InspectionStatus,
                inspectorName = record.InspectorName,
                executedBy = record.ExecutedBy,
                devicePhotos = record.DevicePhotos ?? new System.Collections.Generic.List<string>()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessExchange([FromForm] ExchangeRecord exchangeRecord)
        {
            var executedBy = User.Identity?.Name ?? "Admin";
            var (success, message, result) = await _exchangeService.ProcessExchangeAsync(exchangeRecord, executedBy);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success, message, record = result });
            }

            if (success)
            {
                TempData["ToastMessage"] = message;
                TempData["ToastType"] = "success";
            }
            else
            {
                TempData["ToastMessage"] = message;
                TempData["ToastType"] = "danger";
            }

            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateExchange([FromForm] ExchangeRecord exchangeRecord)
        {
            var updatedBy = User.Identity?.Name ?? "Admin";
            var (success, message, result) = await _exchangeService.UpdateExchangeAsync(exchangeRecord, updatedBy);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success, message, record = result });
            }

            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteExchange(string id)
        {
            var deletedBy = User.Identity?.Name ?? "Admin";
            var (success, message) = await _exchangeService.DeleteExchangeAsync(id, deletedBy);

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return Json(new { success, message });
            }

            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";
            return RedirectToAction("Index");
        }
    }
}
