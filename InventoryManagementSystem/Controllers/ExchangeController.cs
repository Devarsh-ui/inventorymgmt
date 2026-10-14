using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using InventoryManagementSystem.Extensions;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers
{
    [Authorize]
    public class ExchangeController : Controller
    {
        private readonly IExchangeService _exchangeService;
        private readonly IImageService _imageService;

        public ExchangeController(IExchangeService exchangeService, IImageService imageService)
        {
            _exchangeService = exchangeService;
            _imageService = imageService;
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
                devicePhotos = record.DevicePhotos ?? new List<string>()
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessExchange(
            [FromForm] ExchangeRecord exchangeRecord,
            [FromForm] List<IFormFile>? devicePhotos,
            [FromForm] List<string>? capturedPhotos)
        {
            var uploadedPhotos = await ProcessPhotosAsync(devicePhotos, capturedPhotos);
            if (exchangeRecord.DevicePhotos == null) exchangeRecord.DevicePhotos = new List<string>();
            exchangeRecord.DevicePhotos.AddRange(uploadedPhotos);

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
        public async Task<IActionResult> UpdateExchange(
            [FromForm] ExchangeRecord exchangeRecord,
            [FromForm] List<IFormFile>? devicePhotos,
            [FromForm] List<string>? capturedPhotos,
            [FromForm] List<string>? existingPhotos)
        {
            var existingRecord = await _exchangeService.GetExchangeByIdAsync(exchangeRecord.Id);
            var retainedPhotos = existingPhotos != null 
                ? existingPhotos.Where(p => !string.IsNullOrWhiteSpace(p)).ToList() 
                : (existingRecord?.DevicePhotos ?? new List<string>());

            var newPhotos = await ProcessPhotosAsync(devicePhotos, capturedPhotos);
            retainedPhotos.AddRange(newPhotos);
            exchangeRecord.DevicePhotos = retainedPhotos.Distinct().ToList();

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

        private async Task<List<string>> ProcessPhotosAsync(List<IFormFile>? files, List<string>? base64Photos)
        {
            var photoUrls = new List<string>();

            if (files != null && files.Count > 0)
            {
                foreach (var file in files)
                {
                    if (file != null && file.Length > 0)
                    {
                        var uploadResult = await _imageService.UploadImageAsync(file, "trade_ins");
                        if (uploadResult.IsSuccess && !string.IsNullOrWhiteSpace(uploadResult.SecureUrl))
                        {
                            photoUrls.Add(uploadResult.SecureUrl);
                        }
                    }
                }
            }

            if (base64Photos != null && base64Photos.Count > 0)
            {
                foreach (var b64 in base64Photos)
                {
                    if (string.IsNullOrWhiteSpace(b64)) continue;
                    try
                    {
                        var clean = b64.Trim();
                        if (clean.Contains(","))
                        {
                            clean = clean.Substring(clean.IndexOf(",") + 1);
                        }
                        var bytes = Convert.FromBase64String(clean);
                        if (bytes.Length > 0)
                        {
                            var stream = new MemoryStream(bytes);
                            var formFile = new FormFile(stream, 0, bytes.Length, "device_photo", $"camera_{Guid.NewGuid():N}.jpg")
                            {
                                Headers = new HeaderDictionary(),
                                ContentType = "image/jpeg"
                            };
                            var uploadResult = await _imageService.UploadImageAsync(formFile, "trade_ins");
                            if (uploadResult.IsSuccess && !string.IsNullOrWhiteSpace(uploadResult.SecureUrl))
                            {
                                photoUrls.Add(uploadResult.SecureUrl);
                            }
                        }
                    }
                    catch
                    {
                        // Ignore corrupt or invalid base64 data
                    }
                }
            }

            return photoUrls;
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
