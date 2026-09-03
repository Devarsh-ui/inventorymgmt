using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    public class StockController : Controller
    {
        private readonly IStockService _stockService;
        private readonly IProductService _productService;
        private readonly IDeviceService _deviceService;
        private readonly ICategoryService _categoryService;
        private readonly ISupplierService _supplierService;
        private readonly IAuditLogService _auditLogService;
        private readonly ISaleRepository _saleRepository;

        public StockController(
            IStockService stockService,
            IProductService productService,
            IDeviceService deviceService,
            ICategoryService categoryService,
            ISupplierService supplierService,
            IAuditLogService auditLogService,
            ISaleRepository saleRepository)
        {
            _stockService = stockService;
            _productService = productService;
            _deviceService = deviceService;
            _categoryService = categoryService;
            _supplierService = supplierService;
            _auditLogService = auditLogService;
            _saleRepository = saleRepository;
        }

        [HttpGet]
        public async Task<IActionResult> StockIn()
        {
            var model = new StockTransactionViewModel { Type = "Stock In" };
            await PopulateProductsList(model);
            ViewBag.Suppliers = await _supplierService.GetAllSuppliersAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StockIn(StockTransactionViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await PopulateProductsList(model);
                ViewBag.Suppliers = await _supplierService.GetAllSuppliersAsync();
                return View(model);
            }

            var success = await _stockService.StockInAsync(model.ProductId, model.Quantity, model.Reason, User.Identity?.Name ?? "System");
            if (success)
            {
                var product = await _productService.GetProductByIdAsync(model.ProductId);
                await _auditLogService.LogActivityAsync("Stock In", User.Identity?.Name ?? "System", $"Product: {product?.Name}", $"Added {model.Quantity} units. Reason: {model.Reason}");

                TempData["ToastMessage"] = "Stock added successfully!";
                TempData["ToastType"] = "success";
                return RedirectToAction(nameof(History));
            }

            ModelState.AddModelError(string.Empty, "Error performing Stock In action.");
            await PopulateProductsList(model);
            ViewBag.Suppliers = await _supplierService.GetAllSuppliersAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StockInDevice([FromForm] Device device)
        {
            var executedBy = User.Identity?.Name ?? "Admin";
            var (success, message) = await _stockService.StockInDeviceAsync(device, executedBy);

            if (success)
            {
                TempData["ToastMessage"] = message;
                TempData["ToastType"] = "success";
                return RedirectToAction(nameof(History));
            }

            TempData["ToastMessage"] = message;
            TempData["ToastType"] = "danger";
            return RedirectToAction(nameof(StockIn));
        }

        [HttpGet]
        public async Task<IActionResult> CheckImeiExists(string imei)
        {
            if (string.IsNullOrWhiteSpace(imei))
            {
                return Json(new { valid = false, exists = false, message = "IMEI is required." });
            }

            var cleanImei = imei.Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(cleanImei, @"^\d{14,16}$"))
            {
                return Json(new { valid = false, exists = false, message = "IMEI must be numeric (14 to 16 digits)." });
            }

            var existingDevice = await _deviceService.GetDeviceByImeiAsync(cleanImei);
            if (existingDevice != null)
            {
                return Json(new {
                    valid = true,
                    exists = true,
                    message = "This IMEI already exists in inventory.",
                    deviceInfo = $"{existingDevice.Brand} {existingDevice.ModelName} (Status: {existingDevice.Status})"
                });
            }

            return Json(new { valid = true, exists = false, message = "IMEI available." });
        }

        [HttpPost]
        public async Task<IActionResult> StockInDeviceBatch([FromBody] BatchDeviceStockInRequest request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.ProductId) || request.Devices == null || !request.Devices.Any())
                {
                    return Json(new { success = false, message = "No valid product or devices provided in batch." });
                }

                var executedBy = User.Identity?.Name ?? "Admin";
                int addedCount = 0;
                var errors = new List<string>();

                var seenImeis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in request.Devices)
                {
                    var imei1 = item.IMEI1?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(imei1)) continue;

                    if (seenImeis.Contains(imei1))
                    {
                        errors.Add($"Duplicate IMEI '{imei1}' found within current batch.");
                        continue;
                    }
                    seenImeis.Add(imei1);

                    string? imei2Clean = !string.IsNullOrWhiteSpace(item.IMEI2) ? item.IMEI2.Trim() : null;
                    if (imei2Clean != null)
                    {
                        if (seenImeis.Contains(imei2Clean))
                        {
                            errors.Add($"Duplicate IMEI 2 '{imei2Clean}' found within current batch.");
                            continue;
                        }
                        seenImeis.Add(imei2Clean);
                    }

                    string? serialClean = !string.IsNullOrWhiteSpace(item.SerialNumber) ? item.SerialNumber.Trim() : null;

                    var device = new Device
                    {
                        ProductId = request.ProductId,
                        SupplierName = request.SupplierName,
                        Variant = request.Variant,
                        Color = request.Color,
                        PurchasePrice = request.PurchasePrice,
                        SellingPrice = request.SellingPrice,
                        IMEI1 = imei1,
                        IMEI2 = imei2Clean,
                        SerialNumber = serialClean
                    };

                    var (success, msg) = await _stockService.StockInDeviceAsync(device, executedBy);
                    if (success)
                    {
                        addedCount++;
                    }
                    else
                    {
                        errors.Add($"IMEI '{imei1}': {msg}");
                    }
                }

                if (addedCount > 0)
                {
                    var responseMsg = $"Successfully received {addedCount} physical mobile units into stock!";
                    if (errors.Any()) responseMsg += $" ({errors.Count} items skipped due to duplicates/validation).";
                    return Json(new { success = true, addedCount, message = responseMsg, errors });
                }

                return Json(new { success = false, message = errors.FirstOrDefault() ?? "Failed to process batch stock-in.", errors });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error processing batch stock-in: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> StockOut()
        {
            var model = new StockTransactionViewModel { Type = "Stock Out" };
            await PopulateProductsList(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StockOut(StockTransactionViewModel model)
        {
            var product = await _productService.GetProductByIdAsync(model.ProductId);
            if (product != null && product.CurrentStock < model.Quantity)
            {
                ModelState.AddModelError(nameof(model.Quantity), $"Insufficient stock! Current stock: {product.CurrentStock}");
            }

            if (!ModelState.IsValid)
            {
                await PopulateProductsList(model);
                return View(model);
            }

            var success = await _stockService.StockOutAsync(model.ProductId, model.Quantity, model.Reason, User.Identity?.Name ?? "System");
            if (success)
            {
                await _auditLogService.LogActivityAsync("Stock Out", User.Identity?.Name ?? "System", $"Product: {product?.Name}", $"Removed {model.Quantity} units. Reason: {model.Reason}");

                TempData["ToastMessage"] = "Stock removed successfully!";
                TempData["ToastType"] = "success";
                return RedirectToAction(nameof(History));
            }

            ModelState.AddModelError(string.Empty, "Error performing Stock Out action.");
            await PopulateProductsList(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StockOutDevice(string deviceId, string statusReason)
        {
            var executedBy = User.Identity?.Name ?? "Admin";
            var (success, message) = await _stockService.StockOutDeviceAsync(deviceId, statusReason, executedBy);

            if (success)
            {
                TempData["ToastMessage"] = message;
                TempData["ToastType"] = "success";
                return RedirectToAction(nameof(History));
            }

            TempData["ToastMessage"] = message;
            TempData["ToastType"] = "danger";
            return RedirectToAction(nameof(StockOut));
        }

        [HttpGet]
        public async Task<IActionResult> GetAvailableDevicesForProduct(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId)) return Json(new List<object>());
            var devices = await _deviceService.GetAvailableDevicesForProductAsync(productId);
            return Json(devices.Select(d => new
            {
                id = d.Id,
                imei1 = d.IMEI1,
                imei2 = d.IMEI2,
                serialNumber = d.SerialNumber,
                variant = d.Variant,
                color = d.Color,
                displayText = $"{d.Brand} {d.ModelName} ({d.Variant} {d.Color}) - IMEI: {d.IMEI1}"
            }));
        }

        [HttpGet]
        public async Task<IActionResult> Adjust()
        {
            var model = new StockTransactionViewModel { Type = "Adjustment" };
            await PopulateProductsList(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Adjust(StockTransactionViewModel model, string adjustDirection)
        {
            int qty = model.Quantity;
            if (adjustDirection == "Decrease")
            {
                qty = -qty;
                var product = await _productService.GetProductByIdAsync(model.ProductId);
                if (product != null && product.CurrentStock < model.Quantity)
                {
                    ModelState.AddModelError(nameof(model.Quantity), $"Insufficient stock to decrease by that amount! Current stock: {product.CurrentStock}");
                }
            }

            if (!ModelState.IsValid)
            {
                await PopulateProductsList(model);
                return View(model);
            }

            var success = await _stockService.AdjustStockAsync(model.ProductId, qty, "Adjustment", model.Reason, User.Identity?.Name ?? "System");
            if (success)
            {
                var product = await _productService.GetProductByIdAsync(model.ProductId);
                await _auditLogService.LogActivityAsync("Stock Adjusted", User.Identity?.Name ?? "System", $"Product: {product?.Name}", $"Adjusted by {qty} units. Reason: {model.Reason}");

                TempData["ToastMessage"] = "Stock adjusted successfully!";
                TempData["ToastType"] = "info";
                return RedirectToAction(nameof(History));
            }

            ModelState.AddModelError(string.Empty, "Error performing Stock Adjustment action.");
            await PopulateProductsList(model);
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> History(
            string? searchTerm,
            string? type,
            string? categoryId,
            string? productId,
            DateTime? startDate,
            DateTime? endDate,
            string? executedBy,
            int page = 1,
            int pageSize = 15)
        {
            if (pageSize < 5) pageSize = 15;
            if (page < 1) page = 1;

            var historyTask = _stockService.GetFilteredHistoryAsync(
                searchTerm, type, categoryId, productId, startDate, endDate, executedBy, page, pageSize);
            var productsTask = _productService.GetAllProductsAsync();
            var categoriesTask = _categoryService.GetAllCategoriesAsync();

            await Task.WhenAll(historyTask, productsTask, categoriesTask);

            var (transactions, totalItems) = await historyTask;
            var products = await productsTask;
            var categories = await categoriesTask;

            var categoryDict = categories.ToDictionary(c => c.Id, c => c.Name);
            var productCategories = products.ToDictionary(
                p => p.Id,
                p => !string.IsNullOrEmpty(p.CategoryId) && categoryDict.TryGetValue(p.CategoryId, out var catName) ? catName : "General"
            );

            var totalPages = (int)global::System.Math.Max(1, (totalItems + pageSize - 1) / pageSize);

            var viewModel = new StockHistoryViewModel
            {
                Transactions = transactions,
                ProductNames = products.ToDictionary(p => p.Id, p => p.Name),
                ProductCodes = products.ToDictionary(p => p.Id, p => p.Code),
                ProductCategories = productCategories,

                Categories = categories.Select(c => new SelectListItem
                {
                    Value = c.Id,
                    Text = c.Name,
                    Selected = c.Id == categoryId
                }),
                Products = products.Select(p => new SelectListItem
                {
                    Value = p.Id,
                    Text = $"{p.Name} ({p.Code})",
                    Selected = p.Id == productId
                }),
                TransactionTypes = new List<SelectListItem>
                {
                    new SelectListItem { Value = "", Text = "All Transaction Types" },
                    new SelectListItem { Value = "Stock In", Text = "Stock In", Selected = type == "Stock In" },
                    new SelectListItem { Value = "Stock Out", Text = "Stock Out", Selected = type == "Stock Out" },
                    new SelectListItem { Value = "Adjustment", Text = "Adjustment", Selected = type == "Adjustment" }
                },

                SearchTerm = searchTerm,
                Type = type,
                CategoryId = categoryId,
                ProductId = productId,
                StartDate = startDate,
                EndDate = endDate,
                ExecutedBy = executedBy,

                CurrentPage = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = totalPages
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Analytics(string? productId)
        {
            var products = await _productService.GetAllProductsAsync();
            ViewBag.ProductsList = products.Select(p => new SelectListItem
            {
                Value = p.Id,
                Text = $"{p.Name} ({p.Code})"
            }).ToList();

            Product? product = null;
            var transactionsList = new List<StockTransaction>();

            if (!string.IsNullOrEmpty(productId))
            {
                product = await _productService.GetProductByIdAsync(productId);
                if (product != null)
                {
                    var txs = await _stockService.GetProductTransactionsAsync(productId);
                    transactionsList = txs.OrderBy(t => t.Timestamp).ToList();
                }
            }

            ViewBag.SelectedProduct = product;
            return View(transactionsList);
        }

        private async Task PopulateProductsList(StockTransactionViewModel model)
        {
            var products = await _productService.GetAllProductsAsync();
            model.Products = products.Where(p => p.Status == "Active").Select(p => new SelectListItem
            {
                Value = p.Id,
                Text = $"{p.Name} (SKU: {p.Code} - Qty: {p.CurrentStock}) - {(p.IsImeiRequired ? "Requires IMEI" : "Accessory")}"
            }).ToList();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest();

            var success = await _stockService.DeleteTransactionAsync(id);
            if (success)
            {
                await _auditLogService.LogActivityAsync("Transaction Deleted", User.Identity?.Name ?? "System", $"ID: {id}", "Single transaction record deleted");
                TempData["ToastMessage"] = "Stock transaction entry deleted successfully!";
                TempData["ToastType"] = "success";
            }
            else
            {
                TempData["ToastMessage"] = "Failed to delete transaction entry.";
                TempData["ToastType"] = "danger";
            }
            return RedirectToAction(nameof(History));
        }

        [HttpPost]
        public async Task<IActionResult> BulkDelete([FromBody] List<string> ids)
        {
            if (ids == null || !ids.Any())
            {
                return Json(new { success = false, message = "No transaction entries selected for deletion." });
            }

            var deletedCount = await _stockService.DeleteTransactionsAsync(ids);
            if (deletedCount > 0)
            {
                await _auditLogService.LogActivityAsync("Bulk Transactions Deleted", User.Identity?.Name ?? "System", $"{deletedCount} entries", $"Deleted {deletedCount} stock history records");
                return Json(new { success = true, count = deletedCount, message = $"{deletedCount} transaction entries deleted successfully!" });
            }

            return Json(new { success = false, message = "No records were deleted." });
        }

        [HttpGet]
        public async Task<IActionResult> InventorySummary(
            string? searchTerm,
            string? categoryId,
            string? stockStatus,
            string? sortBy = "stock_desc",
            int page = 1,
            int pageSize = 15)
        {
            if (pageSize < 5) pageSize = 15;
            if (page < 1) page = 1;

            await _supplierService.CleanupOrphanedSupplierDataAsync();

            var products = (await _productService.GetAllProductsAsync())
                .Where(p => string.IsNullOrWhiteSpace(p.SupplierId))
                .ToList();
            var categories = (await _categoryService.GetCategoriesForUserAsync(null))
                .ToList();
            var sales = (await _saleRepository.GetAllAsync()).ToList();

            var categoryDict = categories.ToDictionary(c => c.Id, c => c.Name);

            var productSalesMap = new Dictionary<string, (int SoldCount, decimal TotalRevenue)>();
            foreach (var sale in sales)
            {
                if (sale?.Items == null) continue;
                foreach (var item in sale.Items)
                {
                    if (item == null || string.IsNullOrEmpty(item.ProductId)) continue;
                    if (!productSalesMap.ContainsKey(item.ProductId))
                    {
                        productSalesMap[item.ProductId] = (0, 0m);
                    }
                    var current = productSalesMap[item.ProductId];
                    productSalesMap[item.ProductId] = (current.SoldCount + item.Quantity, current.TotalRevenue + item.Total);
                }
            }

            int totalProducts = products.Count;
            int totalCategories = categories.Count;
            int totalStockQty = products.Sum(p => p.CurrentStock);
            decimal totalCostVal = products.Sum(p => p.CurrentStock * p.PurchasePrice);
            decimal totalRetailVal = products.Sum(p => p.CurrentStock * p.SellingPrice);

            int healthyCount = products.Count(p => p.Status == "Active" && p.CurrentStock > p.MinimumStock);
            int lowStockCount = products.Count(p => p.Status == "Active" && p.CurrentStock > 0 && p.CurrentStock <= p.MinimumStock);
            int outOfStockCount = products.Count(p => p.Status == "Active" && p.CurrentStock == 0);

            var allItems = products.Select(p =>
            {
                var catName = !string.IsNullOrEmpty(p.CategoryId) && categoryDict.TryGetValue(p.CategoryId, out var cName) ? cName : "General";
                var status = p.CurrentStock == 0 ? "Out of Stock" : (p.CurrentStock <= p.MinimumStock ? "Low Stock" : "Healthy");
                var (sold, rev) = productSalesMap.TryGetValue(p.Id, out var salesTuple) ? salesTuple : (0, 0m);

                return new InventoryItemSummaryViewModel
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    ProductCode = p.Code,
                    Barcode = p.Barcode,
                    CategoryName = catName,
                    ImageUrl = p.ImageUrl,
                    PurchasePrice = p.PurchasePrice,
                    SellingPrice = p.SellingPrice,
                    CurrentStock = p.CurrentStock,
                    MinimumStock = p.MinimumStock,
                    StockStatus = status,
                    TotalUnitsSold = sold,
                    TotalSalesRevenue = rev
                };
            }).AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                var term = searchTerm.Trim().ToLower();
                allItems = allItems.Where(i =>
                    (!string.IsNullOrEmpty(i.ProductName) && i.ProductName.ToLower().Contains(term)) ||
                    (!string.IsNullOrEmpty(i.ProductCode) && i.ProductCode.ToLower().Contains(term)) ||
                    (!string.IsNullOrEmpty(i.Barcode) && i.Barcode.ToLower().Contains(term))
                );
            }

            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                var catName = categoryDict.TryGetValue(categoryId, out var cn) ? cn : "";
                if (!string.IsNullOrEmpty(catName))
                {
                    allItems = allItems.Where(i => i.CategoryName == catName);
                }
            }

            if (!string.IsNullOrWhiteSpace(stockStatus))
            {
                allItems = allItems.Where(i => i.StockStatus.Equals(stockStatus, StringComparison.OrdinalIgnoreCase));
            }

            allItems = sortBy switch
            {
                "stock_asc" => allItems.OrderBy(i => i.CurrentStock),
                "val_desc" => allItems.OrderByDescending(i => i.CostValuation),
                "val_asc" => allItems.OrderBy(i => i.CostValuation),
                "sold_desc" => allItems.OrderByDescending(i => i.TotalUnitsSold),
                "name_asc" => allItems.OrderBy(i => i.ProductName),
                _ => allItems.OrderByDescending(i => i.CurrentStock)
            };

            var totalFilteredItems = allItems.Count();
            var pagedItems = allItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            int calcPages = (int)((totalFilteredItems + pageSize - 1) / pageSize);
            int totalPages = calcPages < 1 ? 1 : calcPages;

            var viewModel = new InventorySummaryViewModel
            {
                TotalProducts = totalProducts,
                TotalCategories = totalCategories,
                TotalStockQuantity = totalStockQty,
                TotalCostValuation = totalCostVal,
                TotalRetailValuation = totalRetailVal,
                HealthyCount = healthyCount,
                LowStockCount = lowStockCount,
                OutOfStockCount = outOfStockCount,

                SearchTerm = searchTerm,
                CategoryId = categoryId,
                StockStatus = stockStatus,
                SortBy = sortBy,

                Categories = categories.Select(c => new SelectListItem
                {
                    Value = c.Id,
                    Text = c.Name,
                    Selected = c.Id == categoryId
                }),
                StockStatuses = new List<SelectListItem>
                {
                    new SelectListItem { Value = "", Text = "All Stock Statuses" },
                    new SelectListItem { Value = "Healthy", Text = "Healthy Stock", Selected = stockStatus == "Healthy" },
                    new SelectListItem { Value = "Low Stock", Text = "Low Stock Alert", Selected = stockStatus == "Low Stock" },
                    new SelectListItem { Value = "Out of Stock", Text = "Out of Stock", Selected = stockStatus == "Out of Stock" }
                },
                SortOptions = new List<SelectListItem>
                {
                    new SelectListItem { Value = "stock_desc", Text = "Stock: High to Low", Selected = sortBy == "stock_desc" },
                    new SelectListItem { Value = "stock_asc", Text = "Stock: Low to High", Selected = sortBy == "stock_asc" },
                    new SelectListItem { Value = "val_desc", Text = "Value: High to Low", Selected = sortBy == "val_desc" },
                    new SelectListItem { Value = "val_asc", Text = "Value: Low to High", Selected = sortBy == "val_asc" },
                    new SelectListItem { Value = "sold_desc", Text = "Sales: Most Sold", Selected = sortBy == "sold_desc" },
                    new SelectListItem { Value = "name_asc", Text = "Name: A to Z", Selected = sortBy == "name_asc" }
                },

                Items = pagedItems,
                CurrentPage = page,
                PageSize = pageSize,
                TotalItems = totalFilteredItems,
                TotalPages = totalPages
            };

            return View(viewModel);
        }
    }
}
