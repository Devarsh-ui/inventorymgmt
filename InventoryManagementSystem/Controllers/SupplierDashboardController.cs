using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using InventoryManagementSystem.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers
{
    [Authorize(Roles = Role.Supplier)]
    public class SupplierDashboardController : Controller
    {
        private readonly ISupplierService _supplierService;
        private readonly ISupplierOrderService _supplierOrderService;
        private readonly IProductRepository _productRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IAccountValidationService _accountValidationService;
        private readonly IAuditLogService _auditLogService;
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IImageService _imageService;
        private readonly INotificationRepository _notificationRepository;
        private readonly IMobileSpecSearchService _specSearchService;
        private readonly ISupplierPurchaseReturnService _purchaseReturnService;
        private readonly IStockService _stockService;
        private readonly IDeviceRepository _deviceRepository;
        private readonly IStockTransactionRepository _transactionRepository;

        public SupplierDashboardController(
            ISupplierService supplierService,
            ISupplierOrderService supplierOrderService,
            IProductRepository productRepository,
            ICategoryRepository categoryRepository,
            IAccountValidationService accountValidationService,
            IAuditLogService auditLogService,
            IProductService productService,
            ICategoryService categoryService,
            IImageService imageService,
            INotificationRepository notificationRepository,
            IMobileSpecSearchService specSearchService,
            ISupplierPurchaseReturnService purchaseReturnService,
            IStockService stockService,
            IDeviceRepository deviceRepository,
            IStockTransactionRepository transactionRepository)
        {
            _supplierService = supplierService;
            _supplierOrderService = supplierOrderService;
            _productRepository = productRepository;
            _categoryRepository = categoryRepository;
            _accountValidationService = accountValidationService;
            _auditLogService = auditLogService;
            _productService = productService;
            _categoryService = categoryService;
            _imageService = imageService;
            _notificationRepository = notificationRepository;
            _specSearchService = specSearchService;
            _purchaseReturnService = purchaseReturnService;
            _stockService = stockService;
            _deviceRepository = deviceRepository;
            _transactionRepository = transactionRepository;
        }

        private string CurrentSupplierId => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? string.Empty;

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var supplierId = CurrentSupplierId;
            var supplier = await _supplierService.GetSupplierByIdAsync(supplierId);
            if (supplier == null) return RedirectToAction("Login", "Account");

            var allProducts = await _productRepository.GetAllAsync();
            var supplierProducts = allProducts.Where(p => p.SupplierId == supplierId).ToList();

            var orderCounts = await _supplierOrderService.GetOrderStatusCountsAsync(supplierId);
            var recentOrders = await _supplierOrderService.GetSupplierOrdersAsync(supplierId, status: null, limit: 10);

            ViewBag.Supplier = supplier;
            ViewBag.TotalProducts = supplierProducts.Count;
            ViewBag.ActiveProducts = supplierProducts.Count(p => p.Status == "Active");
            ViewBag.OrderCounts = orderCounts;

            return View(recentOrders);
        }

        [HttpGet]
        public async Task<IActionResult> Products()
        {
            var supplierId = CurrentSupplierId;
            var allProducts = await _productRepository.GetAllAsync();
            var supplierProducts = allProducts.Where(p => p.SupplierId == supplierId).OrderByDescending(p => p.CreatedDate).ToList();

            ViewBag.Categories = await _categoryRepository.GetAllAsync();
            return View(supplierProducts);
        }

        [HttpGet]
        public async Task<IActionResult> CreateProduct()
        {
            var model = new ProductCreateViewModel();
            await PopulateCategoriesList(model);
            return View("CreateProduct", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateProduct(ProductCreateViewModel model)
        {
            var supplierId = CurrentSupplierId;
            var supplier = await _supplierService.GetSupplierByIdAsync(supplierId);
            if (supplier == null) return RedirectToAction("Login", "Account");

            var existingByCode = await _productService.GetProductByCodeAsync(model.Code, supplierId);
            if (existingByCode != null)
            {
                ModelState.AddModelError(nameof(model.Code), "Product SKU Code is already in use in your catalog.");
            }

            var existingByBarcode = await _productService.GetProductByBarcodeAsync(model.Barcode, supplierId);
            if (existingByBarcode != null)
            {
                ModelState.AddModelError(nameof(model.Barcode), "Barcode is already in use in your catalog.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateCategoriesList(model);
                return View("CreateProduct", model);
            }

            var product = new Product
            {
                Name = model.Name,
                Code = model.Code.ToUpper(),
                Barcode = model.Barcode,
                CategoryId = model.CategoryId,
                ProductType = model.ProductType ?? "Smartphone",
                Brand = model.Brand ?? string.Empty,
                ModelName = model.ModelName ?? string.Empty,
                Variant = model.Variant ?? string.Empty,
                Color = model.Color ?? string.Empty,
                PurchasePrice = model.PurchasePrice,
                SupplierPrice = model.PurchasePrice,
                SellingPrice = model.SellingPrice,
                CurrentStock = model.InitialStock,
                MinimumStock = model.MinimumStock,
                Description = model.Description,
                Status = model.Status,
                Specs = model.Specs ?? new MobileSpecifications(),
                SupplierId = supplierId,
                SupplierName = supplier.CompanyName,
                CreatedDate = DateTime.UtcNow,
                UpdatedDate = DateTime.UtcNow
            };

            var imageUrls = new List<string>();

            if (model.ProductImage != null && model.ProductImage.Length > 0)
            {
                var uploadResult = await _imageService.UploadImageAsync(model.ProductImage, "products");
                if (uploadResult.IsSuccess)
                {
                    product.ImageUrl = uploadResult.SecureUrl;
                    product.ImagePublicId = uploadResult.PublicId;
                    product.ImageOriginalFilename = uploadResult.OriginalFilename;
                    imageUrls.Add(uploadResult.SecureUrl);
                }
                else
                {
                    ModelState.AddModelError(nameof(model.ProductImage), uploadResult.ErrorMessage);
                    await PopulateCategoriesList(model);
                    return View("CreateProduct", model);
                }
            }

            if (model.ProductImages != null && model.ProductImages.Any())
            {
                foreach (var file in model.ProductImages.Take(50))
                {
                    if (file == null || file.Length == 0) continue;
                    var uploadResult = await _imageService.UploadImageAsync(file, "products");
                    if (uploadResult.IsSuccess)
                    {
                        if (string.IsNullOrEmpty(product.ImageUrl))
                        {
                            product.ImageUrl = uploadResult.SecureUrl;
                            product.ImagePublicId = uploadResult.PublicId;
                            product.ImageOriginalFilename = uploadResult.OriginalFilename;
                        }
                        if (!imageUrls.Contains(uploadResult.SecureUrl)) imageUrls.Add(uploadResult.SecureUrl);
                    }
                }
            }

            product.ImageUrls = imageUrls;

            try
            {
                await _productService.CreateProductAsync(product);
                await _auditLogService.LogActivityAsync("SUPPLIER_PRODUCT_CREATED", supplier.CompanyName, product.Name, $"Added product '{product.Name}' (SKU: {product.Code}) to supplier catalog.");

                TempData["ToastMessage"] = $"Product '{product.Name}' added to your catalog successfully!";
                TempData["ToastType"] = "success";

                return RedirectToAction(nameof(Products));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Failed to add product: {ex.Message}");
                await PopulateCategoriesList(model);
                return View("CreateProduct", model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Details(string id)
        {
            var supplierId = CurrentSupplierId;
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null || product.SupplierId != supplierId) return NotFound();

            var category = !string.IsNullOrEmpty(product.CategoryId) ? await _categoryService.GetCategoryByIdAsync(product.CategoryId) : null;
            ViewBag.CategoryName = category?.Name ?? "Uncategorized";

            return View(product);
        }

        [HttpGet]
        public async Task<IActionResult> EditProduct(string id)
        {
            var supplierId = CurrentSupplierId;
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null || product.SupplierId != supplierId) return NotFound();

            var model = new ProductEditViewModel
            {
                Id = product.Id,
                Name = product.Name,
                Code = product.Code,
                Barcode = product.Barcode,
                CategoryId = product.CategoryId,
                ProductType = product.ProductType ?? "Smartphone",
                Brand = product.Brand,
                ModelName = product.ModelName,
                Variant = product.Variant,
                Color = product.Color,
                PurchasePrice = product.SupplierPrice > 0 ? product.SupplierPrice : product.PurchasePrice,
                SellingPrice = product.SellingPrice,
                MinimumStock = product.MinimumStock,
                Description = product.Description,
                Status = product.Status,
                CurrentImageUrl = product.ImageUrl,
                ExistingImageUrls = product.ImageUrls ?? new List<string>(),
                Specs = product.Specs ?? new MobileSpecifications()
            };

            await PopulateCategoriesList(model);
            return View("EditProduct", model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditProduct(ProductEditViewModel model)
        {
            var supplierId = CurrentSupplierId;
            var supplier = await _supplierService.GetSupplierByIdAsync(supplierId);

            if (string.IsNullOrWhiteSpace(model.Id)) return RedirectToAction(nameof(Products));

            var existingProduct = await _productService.GetProductByIdAsync(model.Id);
            if (existingProduct == null || existingProduct.SupplierId != supplierId)
            {
                return NotFound();
            }

            var existingByCode = await _productService.GetProductByCodeAsync(model.Code, supplierId);
            if (existingByCode != null && existingByCode.Id != model.Id)
            {
                ModelState.AddModelError(nameof(model.Code), "Product SKU Code is already in use in your catalog.");
            }

            var existingByBarcode = await _productService.GetProductByBarcodeAsync(model.Barcode, supplierId);
            if (existingByBarcode != null && existingByBarcode.Id != model.Id)
            {
                ModelState.AddModelError(nameof(model.Barcode), "Barcode is already in use in your catalog.");
            }

            if (!ModelState.IsValid)
            {
                await PopulateCategoriesList(model);
                return View("EditProduct", model);
            }

            existingProduct.Name = model.Name;
            existingProduct.Code = model.Code.ToUpper();
            existingProduct.Barcode = model.Barcode;
            existingProduct.CategoryId = model.CategoryId;
            existingProduct.ProductType = model.ProductType ?? "Smartphone";
            existingProduct.Brand = model.Brand ?? string.Empty;
            existingProduct.ModelName = model.ModelName ?? string.Empty;
            existingProduct.Variant = model.Variant ?? string.Empty;
            existingProduct.Color = model.Color ?? string.Empty;
            existingProduct.PurchasePrice = model.PurchasePrice;
            existingProduct.SupplierPrice = model.PurchasePrice;
            existingProduct.SellingPrice = model.SellingPrice;
            existingProduct.MinimumStock = model.MinimumStock;
            existingProduct.Description = model.Description ?? string.Empty;
            existingProduct.Status = model.Status;
            existingProduct.Specs = model.Specs ?? new MobileSpecifications();
            existingProduct.UpdatedDate = DateTime.UtcNow;

            var imageUrls = existingProduct.ImageUrls != null ? new List<string>(existingProduct.ImageUrls) : new List<string>();

            if (model.ProductImage != null && model.ProductImage.Length > 0)
            {
                var uploadResult = await _imageService.UploadImageAsync(model.ProductImage, "products");
                if (uploadResult.IsSuccess)
                {
                    existingProduct.ImageUrl = uploadResult.SecureUrl;
                    existingProduct.ImagePublicId = uploadResult.PublicId;
                    existingProduct.ImageOriginalFilename = uploadResult.OriginalFilename;
                    if (!imageUrls.Contains(uploadResult.SecureUrl)) imageUrls.Add(uploadResult.SecureUrl);
                }
                else
                {
                    ModelState.AddModelError(nameof(model.ProductImage), uploadResult.ErrorMessage);
                    await PopulateCategoriesList(model);
                    return View("EditProduct", model);
                }
            }

            if (model.ProductImages != null && model.ProductImages.Any())
            {
                foreach (var file in model.ProductImages.Take(50))
                {
                    if (file == null || file.Length == 0) continue;
                    var uploadResult = await _imageService.UploadImageAsync(file, "products");
                    if (uploadResult.IsSuccess)
                    {
                        if (string.IsNullOrEmpty(existingProduct.ImageUrl))
                        {
                            existingProduct.ImageUrl = uploadResult.SecureUrl;
                            existingProduct.ImagePublicId = uploadResult.PublicId;
                            existingProduct.ImageOriginalFilename = uploadResult.OriginalFilename;
                        }
                        if (!imageUrls.Contains(uploadResult.SecureUrl)) imageUrls.Add(uploadResult.SecureUrl);
                    }
                }
            }

            existingProduct.ImageUrls = imageUrls;

            try
            {
                await _productService.UpdateProductAsync(existingProduct);
                await _auditLogService.LogActivityAsync("SUPPLIER_PRODUCT_UPDATED", supplier?.CompanyName ?? "Supplier", existingProduct.Name, $"Updated supplier product details for {existingProduct.Name}.");

                TempData["ToastMessage"] = "Product updated successfully!";
                TempData["ToastType"] = "success";

                return RedirectToAction(nameof(Products));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Failed to update product: {ex.Message}");
                await PopulateCategoriesList(model);
                return View("EditProduct", model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProduct(string id)
        {
            var supplierId = CurrentSupplierId;
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null || product.SupplierId != supplierId) return NotFound();

            if (!string.IsNullOrEmpty(product.ImagePublicId))
            {
                await _imageService.DeleteImageAsync(product.ImagePublicId);
            }

            await _productService.DeleteProductAsync(id);
            await _auditLogService.LogActivityAsync("SUPPLIER_PRODUCT_DELETED", User.Identity?.Name ?? "Supplier", product.Name, $"Deleted product SKU: {product.Code}.");

            TempData["ToastMessage"] = "Product removed from catalog successfully.";
            TempData["ToastType"] = "success";

            return RedirectToAction(nameof(Products));
        }

        [HttpGet]
        public async Task<IActionResult> SearchSpecsOnline(string brand, string modelName, string variant, bool allowThirdPartyFallback = false, string? customUrl = null)
        {
            var user = User.Identity?.Name ?? "Supplier";
            var result = await _specSearchService.SearchSpecificationsAsync(brand, modelName, variant, allowThirdPartyFallback, customUrl);
            return Json(result);
        }

        private async Task PopulateCategoriesList(ProductCreateViewModel model)
        {
            var supplierId = CurrentSupplierId;
            var categories = await _categoryService.GetActiveCategoriesForUserAsync(supplierId);
            model.Categories = categories.Select(c => new SelectListItem
            {
                Value = c.Id,
                Text = c.Name
            }).ToList();
        }

        private async Task PopulateCategoriesList(ProductEditViewModel model)
        {
            var supplierId = CurrentSupplierId;
            var categories = await _categoryService.GetActiveCategoriesForUserAsync(supplierId);
            model.Categories = categories.Select(c => new SelectListItem
            {
                Value = c.Id,
                Text = c.Name
            }).ToList();
        }

        [HttpGet]
        public async Task<IActionResult> Orders(
            string? status, 
            string? search, 
            DateTime? fromDate, 
            DateTime? toDate, 
            decimal? minAmount, 
            decimal? maxAmount, 
            string? sortBy, 
            int page = 1)
        {
            var supplierId = CurrentSupplierId;
            int pageSize = 10;

            var orders = await _supplierOrderService.GetPagedOrdersAsync(
                search, supplierId, status, page, pageSize, fromDate, toDate, minAmount, maxAmount, sortBy);

            var totalCount = await _supplierOrderService.GetFilteredCountAsync(
                search, supplierId, status, fromDate, toDate, minAmount, maxAmount);

            var statusCounts = await _supplierOrderService.GetOrderStatusCountsAsync(supplierId);

            ViewBag.Status = status;
            ViewBag.Search = search;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");
            ViewBag.MinAmount = minAmount;
            ViewBag.MaxAmount = maxAmount;
            ViewBag.SortBy = sortBy;
            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalCount = totalCount;
            ViewBag.TotalPages = (int)System.Math.Ceiling((double)totalCount / pageSize);
            ViewBag.StatusCounts = statusCounts;

            return View(orders);
        }

        [HttpGet]
        public async Task<IActionResult> OrderDetails(string id)
        {
            var supplierId = CurrentSupplierId;
            var order = await _supplierOrderService.GetOrderByIdAsync(id);
            if (order == null || order.SupplierId != supplierId)
            {
                return NotFound();
            }

            return View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateOrderStatus(string orderId, string newStatus, string? supplierNotes, DateTime? expectedDeliveryDate)
        {
            var supplierId = CurrentSupplierId;
            var order = await _supplierOrderService.GetOrderByIdAsync(orderId);
            if (order == null || order.SupplierId != supplierId)
            {
                TempData["ToastMessage"] = "Purchase order not found or access denied.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(Orders));
            }

            // Server-side validation of allowed supplier status transitions
            if (newStatus != SupplierOrderStatus.Accepted &&
                newStatus != SupplierOrderStatus.Rejected &&
                newStatus != SupplierOrderStatus.Processing &&
                newStatus != SupplierOrderStatus.Shipped &&
                newStatus != SupplierOrderStatus.Delivered)
            {
                TempData["ToastMessage"] = "Invalid order status transition.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(OrderDetails), new { id = orderId });
            }

            var (success, message) = await _supplierOrderService.UpdateOrderStatusAsync(orderId, newStatus, User.Identity?.Name ?? "Supplier", supplierNotes, expectedDeliveryDate);
            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";

            return RedirectToAction(nameof(OrderDetails), new { id = orderId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteOrder(string orderId)
        {
            var supplierId = CurrentSupplierId;
            var (success, message) = await _supplierOrderService.DeleteOrderAsync(orderId, User.Identity?.Name ?? "Supplier", supplierId);
            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";
            return RedirectToAction(nameof(Orders));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDeleteOrders([FromForm] List<string> orderIds)
        {
            var supplierId = CurrentSupplierId;
            var (success, message, count) = await _supplierOrderService.BulkDeleteOrdersAsync(orderIds, User.Identity?.Name ?? "Supplier", supplierId);
            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";
            return RedirectToAction(nameof(Orders));
        }

        [HttpGet]
        public Task<IActionResult> PurchaseReturns(
            string? search, string? reason, string? resolution, string? status,
            DateTime? fromDate, DateTime? toDate, string? brand, string? categoryId,
            string sortBy = "newest", int page = 1, int pageSize = 10)
        {
            return Returns(search, reason, resolution, status, fromDate, toDate, brand, categoryId, sortBy, page, pageSize);
        }

        [HttpGet]
        public Task<IActionResult> PurchaseReturnDetails(string id)
        {
            return ReturnDetails(id);
        }

        [HttpGet]
        public async Task<IActionResult> Profile()
        {
            var supplierId = CurrentSupplierId;
            var supplier = await _supplierService.GetSupplierByIdAsync(supplierId);
            if (supplier == null) return NotFound();

            return View(supplier);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Profile(Supplier model, string? newPassword)
        {
            var supplierId = CurrentSupplierId;
            var existing = await _supplierService.GetSupplierByIdAsync(supplierId);
            if (existing == null) return NotFound();

            if (string.IsNullOrWhiteSpace(model.CompanyName))
            {
                ModelState.AddModelError(nameof(model.CompanyName), "Company Name is required.");
            }

            if (!string.IsNullOrWhiteSpace(model.Email))
            {
                bool isDuplicate = await _accountValidationService.IsEmailAlreadyRegisteredAsync(model.Email, excludeSupplierId: supplierId);
                if (isDuplicate)
                {
                    ModelState.AddModelError(nameof(model.Email), "This email address is already registered with another account.");
                }
            }

            if (!ModelState.IsValid)
            {
                return View(existing);
            }

            existing.CompanyName = model.CompanyName;
            existing.ContactPerson = model.ContactPerson;
            existing.Phone = model.Phone;
            existing.Email = model.Email;
            existing.Address = model.Address;
            existing.City = model.City;
            existing.State = model.State;
            existing.Country = model.Country;
            existing.Gstin = model.Gstin;

            if (!string.IsNullOrWhiteSpace(newPassword) && newPassword.Length >= 6)
            {
                existing.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            }

            await _supplierService.SaveSupplierAsync(existing, User.Identity?.Name ?? existing.CompanyName);
            TempData["ToastMessage"] = "Your supplier portal profile was updated successfully!";
            TempData["ToastType"] = "success";

            return RedirectToAction(nameof(Profile));
        }

        [HttpGet]
        public async Task<IActionResult> Inventory(string? search, string? categoryId, string? stockStatus, int page = 1)
        {
            var supplierId = CurrentSupplierId;
            if (string.IsNullOrEmpty(supplierId))
            {
                return RedirectToAction("Login", "Account");
            }

            var allCategories = (await _categoryRepository.GetAllAsync()).OrderBy(c => c.Name).ToList();
            var allProducts = (await _productRepository.GetAllAsync())
                .Where(p => p.SupplierId == supplierId)
                .ToList();

            // Overall Stock Metrics
            int totalSuppliedProducts = allProducts.Count;
            int totalStockUnits = allProducts.Sum(p => p.CurrentStock);
            decimal totalStockValuation = allProducts.Sum(p => p.CurrentStock * p.PurchasePrice);
            int lowStockCount = allProducts.Count(p => p.CurrentStock > 0 && p.CurrentStock <= (p.MinimumStock > 0 ? p.MinimumStock : 5));
            int outOfStockCount = allProducts.Count(p => p.CurrentStock <= 0);

            // Filtering
            var filtered = allProducts.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLowerInvariant();
                filtered = filtered.Where(p =>
                    (p.Name != null && p.Name.ToLowerInvariant().Contains(q)) ||
                    (p.Brand != null && p.Brand.ToLowerInvariant().Contains(q)) ||
                    (p.ModelName != null && p.ModelName.ToLowerInvariant().Contains(q)) ||
                    (p.Code != null && p.Code.ToLowerInvariant().Contains(q))
                );
            }

            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                filtered = filtered.Where(p => p.CategoryId == categoryId);
            }

            if (!string.IsNullOrWhiteSpace(stockStatus))
            {
                switch (stockStatus.ToLowerInvariant())
                {
                    case "instock":
                        filtered = filtered.Where(p => p.CurrentStock > (p.MinimumStock > 0 ? p.MinimumStock : 5));
                        break;
                    case "lowstock":
                        filtered = filtered.Where(p => p.CurrentStock > 0 && p.CurrentStock <= (p.MinimumStock > 0 ? p.MinimumStock : 5));
                        break;
                    case "outofstock":
                        filtered = filtered.Where(p => p.CurrentStock <= 0);
                        break;
                }
            }

            int pageSize = 12;
            int totalFiltered = filtered.Count();
            int totalPages = (int)System.Math.Ceiling((double)totalFiltered / pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var pagedProducts = filtered
                .OrderByDescending(p => p.CreatedDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var viewModel = new SupplierInventoryViewModel
            {
                Products = pagedProducts,
                Categories = allCategories,
                SearchQuery = search,
                CategoryId = categoryId,
                StockStatus = stockStatus,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                TotalFilteredCount = totalFiltered,
                TotalSuppliedProducts = totalSuppliedProducts,
                TotalStockUnits = totalStockUnits,
                TotalStockValuation = totalStockValuation,
                LowStockCount = lowStockCount,
                OutOfStockCount = outOfStockCount
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Stats(string rangePreset = "30days", DateTime? startDate = null, DateTime? endDate = null, string? statusFilter = null)
        {
            var supplierId = CurrentSupplierId;
            if (string.IsNullOrEmpty(supplierId))
            {
                return RedirectToAction("Login", "Account");
            }

            // Fetch all orders for this supplier
            var orders = (await _supplierOrderService.GetSupplierOrdersAsync(supplierId, null, limit: 1000)).ToList();

            // Date filtering range calculation
            DateTime now = DateTime.UtcNow;
            DateTime start = now.AddDays(-30);
            DateTime end = now;

            switch (rangePreset?.ToLowerInvariant())
            {
                case "today":
                    start = now.Date;
                    end = now.Date.AddDays(1).AddTicks(-1);
                    break;
                case "7days":
                    start = now.AddDays(-7).Date;
                    end = now;
                    break;
                case "30days":
                    start = now.AddDays(-30).Date;
                    end = now;
                    break;
                case "thismonth":
                    start = new DateTime(now.Year, now.Month, 1);
                    end = now;
                    break;
                case "custom":
                    if (startDate.HasValue) start = startDate.Value.Date;
                    if (endDate.HasValue) end = endDate.Value.Date.AddDays(1).AddTicks(-1);
                    break;
                default:
                    rangePreset = "30days";
                    start = now.AddDays(-30).Date;
                    end = now;
                    break;
            }

            var filteredOrders = orders.Where(o => o.CreatedAt >= start && o.CreatedAt <= end);

            if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "All")
            {
                filteredOrders = filteredOrders.Where(o => o.Status.Equals(statusFilter, StringComparison.OrdinalIgnoreCase));
            }

            var orderList = filteredOrders.OrderByDescending(o => o.CreatedAt).ToList();

            // KPI Calculations
            int totalOrdersCount = orderList.Count;
            var deliveredOrders = orderList.Where(o => o.Status == SupplierOrderStatus.Delivered || o.Status == SupplierOrderStatus.Completed).ToList();
            int pendingOrdersCount = orderList.Count(o => o.Status == SupplierOrderStatus.Pending);
            int rejectedOrdersCount = orderList.Count(o => o.Status == SupplierOrderStatus.Rejected);

            decimal totalRevenue = deliveredOrders.Sum(o => o.GrandTotal);
            int totalUnitsSupplied = deliveredOrders.Sum(o => o.TotalQuantity);

            double acceptanceRate = totalOrdersCount > 0
                ? System.Math.Round((double)(totalOrdersCount - rejectedOrdersCount) / totalOrdersCount * 100.0, 1)
                : 100.0;

            decimal avgOrderValue = deliveredOrders.Any() ? System.Math.Round(totalRevenue / deliveredOrders.Count, 2) : 0m;

            // Status Distribution Dictionary
            var statusDistribution = orderList
                .GroupBy(o => o.Status)
                .ToDictionary(g => g.Key, g => g.Count());

            // Daily Sales Timeline (Grouped by date)
            var timelineStats = orderList
                .Where(o => o.Status == SupplierOrderStatus.Delivered || o.Status == SupplierOrderStatus.Completed || o.Status == SupplierOrderStatus.Shipped || o.Status == SupplierOrderStatus.Accepted || o.Status == SupplierOrderStatus.Processing)
                .GroupBy(o => o.CreatedAt.ToString("dd MMM"))
                .Select(g => new DailySalesStat
                {
                    DateLabel = g.Key,
                    Revenue = g.Sum(o => o.GrandTotal),
                    UnitsSold = g.Sum(o => o.TotalQuantity),
                    OrdersCount = g.Count()
                })
                .ToList();

            // Top Supplied Products calculation
            var productSalesMap = new Dictionary<string, (int Units, decimal Revenue, string Name, string Brand, string CatId)>();
            var allProductsMap = (await _productRepository.GetAllAsync()).ToDictionary(p => p.Id, p => p);
            var categoriesMap = (await _categoryRepository.GetAllAsync()).ToDictionary(c => c.Id, c => c.Name);

            foreach (var order in deliveredOrders)
            {
                foreach (var item in order.Items)
                {
                    if (!productSalesMap.ContainsKey(item.ProductId))
                    {
                        var prod = allProductsMap.GetValueOrDefault(item.ProductId);
                        productSalesMap[item.ProductId] = (0, 0m, item.ProductName, prod?.Brand ?? "-", prod?.CategoryId ?? "");
                    }

                    var current = productSalesMap[item.ProductId];
                    productSalesMap[item.ProductId] = (
                        current.Units + item.Quantity,
                        current.Revenue + item.Subtotal,
                        current.Name,
                        current.Brand,
                        current.CatId
                    );
                }
            }

            var topProducts = productSalesMap
                .Select(kv => {
                    var prod = allProductsMap.GetValueOrDefault(kv.Key);
                    return new TopSuppliedProductStat
                    {
                        ProductId = kv.Key,
                        ProductName = kv.Value.Name,
                        Brand = kv.Value.Brand,
                        CategoryName = categoriesMap.GetValueOrDefault(kv.Value.CatId, "General"),
                        ImageUrl = prod?.ImageUrl,
                        UnitsSold = kv.Value.Units,
                        TotalRevenue = kv.Value.Revenue,
                        CurrentStock = prod?.CurrentStock ?? 0
                    };
                })
                .OrderByDescending(p => p.TotalRevenue)
                .Take(5)
                .ToList();

            // Category Performance Breakdown
            var categorySalesMap = new Dictionary<string, (int Units, decimal Revenue)>();
            foreach (var top in productSalesMap.Values)
            {
                string catName = categoriesMap.GetValueOrDefault(top.CatId, "General");
                if (!categorySalesMap.ContainsKey(catName))
                    categorySalesMap[catName] = (0, 0m);

                var cur = categorySalesMap[catName];
                categorySalesMap[catName] = (cur.Units + top.Units, cur.Revenue + top.Revenue);
            }

            var categoryBreakdown = categorySalesMap
                .Select(kv => new CategorySalesStat
                {
                    CategoryName = kv.Key,
                    UnitsSold = kv.Value.Units,
                    TotalRevenue = kv.Value.Revenue,
                    PercentageShare = totalRevenue > 0 ? System.Math.Round((double)(kv.Value.Revenue / totalRevenue) * 100.0, 1) : 0.0
                })
                .OrderByDescending(c => c.TotalRevenue)
                .ToList();

            string topCategoryName = categoryBreakdown.FirstOrDefault()?.CategoryName ?? "N/A";

            var viewModel = new SupplierStatsViewModel
            {
                RangePreset = rangePreset ?? "30days",
                StartDate = start,
                EndDate = end,
                StatusFilter = statusFilter,
                TotalRevenue = totalRevenue,
                TotalUnitsSupplied = totalUnitsSupplied,
                TotalOrdersCount = totalOrdersCount,
                CompletedOrdersCount = deliveredOrders.Count,
                PendingOrdersCount = pendingOrdersCount,
                RejectedOrdersCount = rejectedOrdersCount,
                AcceptanceRatePercentage = acceptanceRate,
                AverageOrderValue = avgOrderValue,
                TopCategoryName = topCategoryName,
                TimelineStats = timelineStats,
                StatusDistribution = statusDistribution,
                TopProducts = topProducts,
                CategoryBreakdown = categoryBreakdown,
                RecentOrders = orderList.Take(10).ToList()
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Returns(
            string? search, string? reason, string? resolution, string? status,
            DateTime? fromDate, DateTime? toDate, string? brand, string? categoryId,
            string sortBy = "newest", int page = 1, int pageSize = 10)
        {
            var supplierId = CurrentSupplierId;
            var supplier = await _supplierService.GetSupplierByIdAsync(supplierId);
            if (supplier == null) return RedirectToAction("Login", "Account");

            // Ensure auto-sync of accepted returns stock deductions
            await _purchaseReturnService.SyncAcceptedReturnsToShopCatalogAsync();

            // Fetch ALL returns for this supplier
            var allReturnsEnumerable = await _purchaseReturnService.GetSupplierReturnsAsync(supplierId, status: null, limit: 10000);
            var allReturns = allReturnsEnumerable.ToList();

            // 1. Calculate KPI Metrics across ALL returns for this supplier
            int totalReturnClaims = allReturns.Count;
            int totalDamagedItems = allReturns.Sum(r => r.TotalQuantity);
            decimal totalReturnFinancialValue = allReturns.Sum(r => r.TotalReturnValue);
            int pendingClaimsCount = allReturns.Count(r => r.Status == PurchaseReturnStatus.Submitted || r.Status == PurchaseReturnStatus.SupplierNotified);
            int acceptedClaimsCount = allReturns.Count(r => r.Status == PurchaseReturnStatus.SupplierAccepted || r.Status == PurchaseReturnStatus.ReceivedBySupplier || r.Status == PurchaseReturnStatus.Completed);
            int rejectedClaimsCount = allReturns.Count(r => r.Status == PurchaseReturnStatus.SupplierRejected);
            decimal totalRefundValue = allReturns.Where(r => r.ResolutionType == PurchaseReturnResolution.Refund).Sum(r => r.TotalReturnValue);
            int totalReplacementItems = allReturns.Where(r => r.ResolutionType == PurchaseReturnResolution.Replacement).Sum(r => r.TotalQuantity);

            // 2. Compute Reason Breakdown across all returns
            var reasonGroups = allReturns
                .GroupBy(r => string.IsNullOrWhiteSpace(r.Reason) ? "Other" : r.Reason)
                .Select(g => new ReturnReasonStatItem
                {
                    Reason = g.Key,
                    Count = g.Count(),
                    ItemQuantity = g.Sum(r => r.TotalQuantity),
                    TotalValue = g.Sum(r => r.TotalReturnValue),
                    Percentage = totalReturnClaims > 0 ? System.Math.Round((double)g.Count() / totalReturnClaims * 100.0, 1) : 0.0
                })
                .OrderByDescending(r => r.Count)
                .ToList();

            // 3. Compute Resolution Breakdown
            var resolutionGroups = allReturns
                .GroupBy(r => string.IsNullOrWhiteSpace(r.ResolutionType) ? "Pending Settlement" : r.ResolutionType)
                .Select(g => new ReturnResolutionStatItem
                {
                    Resolution = g.Key,
                    Count = g.Count(),
                    ItemQuantity = g.Sum(r => r.TotalQuantity),
                    TotalValue = g.Sum(r => r.TotalReturnValue)
                })
                .OrderByDescending(r => r.Count)
                .ToList();

            // 4. Compute Top Returned Products
            var allItems = allReturns.SelectMany(r => r.Items.Select(item => new { Item = item, ReturnRecord = r })).ToList();
            var topReturnedProducts = allItems
                .GroupBy(x => new { Name = x.Item.ProductName ?? "Unknown", Brand = x.Item.Brand ?? "", Model = x.Item.ModelName ?? "" })
                .Select(g => new TopReturnedProductStatItem
                {
                    ProductName = g.Key.Name,
                    Brand = g.Key.Brand,
                    ModelName = g.Key.Model,
                    ImageUrl = g.FirstOrDefault()?.Item.ImageUrl ?? "/images/product-placeholder.png",
                    ReturnedQuantity = g.Sum(x => x.Item.Quantity),
                    TotalReturnPrice = g.Sum(x => x.Item.ReturnValue),
                    TopReason = g.GroupBy(x => x.ReturnRecord.Reason)
                                 .OrderByDescending(rg => rg.Count())
                                 .FirstOrDefault()?.Key ?? "Defective"
                })
                .OrderByDescending(p => p.ReturnedQuantity)
                .Take(5)
                .ToList();

            // 5. Apply Detailed Filters
            var filtered = allReturns.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                string query = search.Trim();
                filtered = filtered.Where(r =>
                    (r.ReturnNumber != null && r.ReturnNumber.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (r.PurchaseOrderNumber != null && r.PurchaseOrderNumber.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    (r.CreatedBy != null && r.CreatedBy.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                    r.Items.Any(i =>
                        (i.ProductName != null && i.ProductName.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                        (i.Brand != null && i.Brand.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                        (i.ModelName != null && i.ModelName.Contains(query, StringComparison.OrdinalIgnoreCase))
                    ) ||
                    (r.DeviceDetails != null && r.DeviceDetails.Any(d =>
                        (d.IMEI1 != null && d.IMEI1.Contains(query, StringComparison.OrdinalIgnoreCase)) ||
                        (d.SerialNumber != null && d.SerialNumber.Contains(query, StringComparison.OrdinalIgnoreCase))
                    ))
                );
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                filtered = filtered.Where(r => r.Reason != null && r.Reason.Equals(reason, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(resolution))
            {
                filtered = filtered.Where(r => r.ResolutionType != null && r.ResolutionType.Equals(resolution, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                filtered = filtered.Where(r => r.Status != null && r.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
            }

            if (fromDate.HasValue)
            {
                filtered = filtered.Where(r => r.CreatedAt >= fromDate.Value.Date);
            }

            if (toDate.HasValue)
            {
                filtered = filtered.Where(r => r.CreatedAt <= toDate.Value.Date.AddDays(1).AddTicks(-1));
            }

            if (!string.IsNullOrWhiteSpace(brand))
            {
                filtered = filtered.Where(r => r.Items.Any(i => i.Brand != null && i.Brand.Equals(brand, StringComparison.OrdinalIgnoreCase)));
            }

            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                filtered = filtered.Where(r => r.Items.Any(i => i.CategoryName != null && i.CategoryName.Equals(categoryId, StringComparison.OrdinalIgnoreCase)));
            }

            // 6. Apply Sorting
            filtered = sortBy switch
            {
                "oldest" => filtered.OrderBy(r => r.CreatedAt),
                "value_desc" => filtered.OrderByDescending(r => r.TotalReturnValue),
                "value_asc" => filtered.OrderBy(r => r.TotalReturnValue),
                "qty_desc" => filtered.OrderByDescending(r => r.TotalQuantity),
                "qty_asc" => filtered.OrderBy(r => r.TotalQuantity),
                _ => filtered.OrderByDescending(r => r.CreatedAt)
            };

            var filteredList = filtered.ToList();
            int totalFilteredItems = filteredList.Count;

            // 7. Paginate
            page = System.Math.Max(1, page);
            pageSize = System.Math.Min(System.Math.Max(pageSize, 5), 100);
            var paginatedReturns = filteredList.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            // Populate Dropdown Select Lists
            var categories = await _categoryRepository.GetAllAsync();
            var allBrands = allReturns.SelectMany(r => r.Items).Select(i => i.Brand).Where(b => !string.IsNullOrWhiteSpace(b)).Distinct().OrderBy(b => b).ToList();

            var viewModel = new SupplierReturnStatsViewModel
            {
                TotalReturnClaims = totalReturnClaims,
                TotalDamagedItems = totalDamagedItems,
                TotalReturnFinancialValue = totalReturnFinancialValue,
                PendingClaimsCount = pendingClaimsCount,
                AcceptedClaimsCount = acceptedClaimsCount,
                RejectedClaimsCount = rejectedClaimsCount,
                TotalRefundValue = totalRefundValue,
                TotalReplacementItems = totalReplacementItems,

                ReasonBreakdown = reasonGroups,
                ResolutionBreakdown = resolutionGroups,
                TopReturnedProducts = topReturnedProducts,

                Search = search,
                Reason = reason,
                Resolution = resolution,
                Status = status,
                FromDate = fromDate,
                ToDate = toDate,
                Brand = brand,
                CategoryId = categoryId,
                SortBy = sortBy,

                Returns = paginatedReturns,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalFilteredItems,

                Categories = categories.ToList(),
                Brands = allBrands,
                Reasons = PurchaseReturnReason.AllReasons,
                Resolutions = PurchaseReturnResolution.AllResolutions,
                Statuses = PurchaseReturnStatus.AllStatuses
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> ReturnDetails(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();

            var supplierId = CurrentSupplierId;
            var returnRecord = await _purchaseReturnService.GetReturnByIdAsync(id);

            if (returnRecord == null || !string.Equals(returnRecord.SupplierId, supplierId, StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            return View(returnRecord);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AcceptReturn(string id, string? supplierNotes)
        {
            var supplierId = CurrentSupplierId;
            var returnRecord = await _purchaseReturnService.GetReturnByIdAsync(id);

            if (returnRecord == null || !string.Equals(returnRecord.SupplierId, supplierId, StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            var executedBy = User.Identity?.Name ?? "Supplier";
            var (success, message) = await _purchaseReturnService.AcceptReturnAsync(id, executedBy, supplierNotes);

            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";

            return RedirectToAction(nameof(ReturnDetails), new { id = id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RejectReturn(string id, string rejectionReason)
        {
            if (string.IsNullOrWhiteSpace(rejectionReason))
            {
                TempData["ToastMessage"] = "Please provide a reason for rejecting the return.";
                TempData["ToastType"] = "warning";
                return RedirectToAction(nameof(ReturnDetails), new { id = id });
            }

            var supplierId = CurrentSupplierId;
            var returnRecord = await _purchaseReturnService.GetReturnByIdAsync(id);

            if (returnRecord == null || !string.Equals(returnRecord.SupplierId, supplierId, StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            var executedBy = User.Identity?.Name ?? "Supplier";
            var (success, message) = await _purchaseReturnService.RejectReturnAsync(id, executedBy, rejectionReason);

            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";

            return RedirectToAction(nameof(ReturnDetails), new { id = id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePurchaseReturnStatus(string returnId, string newStatus, string? supplierNotes, string? rejectionReason)
        {
            var supplierId = CurrentSupplierId;
            var returnRecord = await _purchaseReturnService.GetReturnByIdAsync(id: returnId);

            // Server-side Ownership Validation
            if (returnRecord == null || !string.Equals(returnRecord.SupplierId, supplierId, StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            var updatedBy = User.Identity?.Name ?? "Supplier";
            var (success, message) = await _purchaseReturnService.UpdateReturnStatusAsync(returnId, newStatus, updatedBy, supplierNotes, rejectionReason);

            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";

            return RedirectToAction(nameof(ReturnDetails), new { id = returnId });
        }

        #region Supplier Stock In & Stock Out Pages

        [HttpGet]
        public async Task<IActionResult> StockIn(string? search, int page = 1)
        {
            var supplierId = CurrentSupplierId;
            if (string.IsNullOrEmpty(supplierId)) return RedirectToAction("Login", "Account");
            var supplier = await _supplierService.GetSupplierByIdAsync(supplierId);

            var allProducts = await _productRepository.GetAllAsync();
            var supplierProducts = allProducts.Where(p => p.SupplierId == supplierId).OrderBy(p => p.Name).ToList();
            var productIds = supplierProducts.Select(p => p.Id).ToHashSet();

            // Load transactions for this supplier's products
            var allTransactions = await _transactionRepository.GetAllAsync();
            var stockInTransactions = allTransactions
                .Where(t => productIds.Contains(t.ProductId) && t.Type == "Stock In")
                .OrderByDescending(t => t.Timestamp)
                .ToList();

            var today = DateTime.UtcNow.Date;
            int totalUnits = stockInTransactions.Sum(t => t.Quantity);
            int todayUnits = stockInTransactions.Where(t => t.Timestamp.Date == today).Sum(t => t.Quantity);
            
            var prodDict = supplierProducts.ToDictionary(p => p.Id);
            decimal totalValuation = stockInTransactions.Sum(t => {
                if (prodDict.TryGetValue(t.ProductId, out var prod)) return prod.PurchasePrice * t.Quantity;
                return t.UnitCost * t.Quantity;
            });

            var filtered = stockInTransactions.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLowerInvariant();
                filtered = filtered.Where(t => 
                    (t.ProductName != null && t.ProductName.ToLowerInvariant().Contains(q)) ||
                    (t.ProductCode != null && t.ProductCode.ToLowerInvariant().Contains(q)) ||
                    (t.Reason != null && t.Reason.ToLowerInvariant().Contains(q)) ||
                    (t.IMEI != null && t.IMEI.ToLowerInvariant().Contains(q))
                );
            }

            int pageSize = 15;
            int totalFiltered = filtered.Count();
            int totalPages = (int)System.Math.Ceiling((double)totalFiltered / pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var pagedList = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var vm = new SupplierStockInViewModel
            {
                Supplier = supplier,
                Products = supplierProducts,
                Transactions = pagedList,
                TotalStockInUnits = totalUnits,
                TodayStockInUnits = todayUnits,
                TotalStockInValuation = totalValuation,
                TotalTransactionsCount = stockInTransactions.Count,
                SearchQuery = search,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                TotalFilteredCount = totalFiltered
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StockIn(SupplierStockInViewModel form, string stockInMode)
        {
            var supplierId = CurrentSupplierId;
            if (string.IsNullOrEmpty(supplierId)) return RedirectToAction("Login", "Account");
            var supplier = await _supplierService.GetSupplierByIdAsync(supplierId);
            var executedBy = supplier?.CompanyName ?? "Supplier";

            if (string.IsNullOrWhiteSpace(form.ProductId))
            {
                TempData["ToastMessage"] = "Please select a valid product to stock in.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(StockIn));
            }

            var product = await _productRepository.GetByIdAsync(form.ProductId);
            if (product == null || product.SupplierId != supplierId)
            {
                TempData["ToastMessage"] = "Unauthorized: Product does not belong to your vendor catalog.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(StockIn));
            }

            // Mode 1: Single Physical IMEI Device
            if (stockInMode == "SingleIMEI")
            {
                if (string.IsNullOrWhiteSpace(form.Imei1))
                {
                    TempData["ToastMessage"] = "IMEI 1 is required for physical mobile unit stock in.";
                    TempData["ToastType"] = "danger";
                    return RedirectToAction(nameof(StockIn));
                }

                var device = new Device
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    ProductCode = product.Code,
                    Brand = product.Brand,
                    ModelName = product.ModelName,
                    Variant = product.Variant,
                    Color = product.Color,
                    IMEI1 = form.Imei1.Trim(),
                    IMEI2 = string.IsNullOrWhiteSpace(form.Imei2) ? null : form.Imei2.Trim(),
                    SerialNumber = string.IsNullOrWhiteSpace(form.SerialNumber) ? null : form.SerialNumber.Trim(),
                    PurchasePrice = product.PurchasePrice,
                    SellingPrice = product.SellingPrice,
                    SupplierId = supplierId,
                    SupplierName = supplier?.CompanyName ?? "Supplier",
                    PurchaseDate = DateTime.UtcNow,
                    Status = "InStock"
                };

                var (success, msg) = await _stockService.StockInDeviceAsync(device, executedBy);
                TempData["ToastMessage"] = msg;
                TempData["ToastType"] = success ? "success" : "danger";
                return RedirectToAction(nameof(StockIn));
            }

            // Mode 2: Batch / Multi-IMEI Scan
            if (stockInMode == "BatchIMEI")
            {
                if (string.IsNullOrWhiteSpace(form.BulkImeis))
                {
                    TempData["ToastMessage"] = "Please provide one or more IMEIs to scan into inventory.";
                    TempData["ToastType"] = "danger";
                    return RedirectToAction(nameof(StockIn));
                }

                var lines = form.BulkImeis.Split(new[] { '\r', '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Distinct()
                    .ToList();

                if (!lines.Any())
                {
                    TempData["ToastMessage"] = "No valid IMEIs found.";
                    TempData["ToastType"] = "danger";
                    return RedirectToAction(nameof(StockIn));
                }

                int successCount = 0;
                var errors = new List<string>();

                foreach (var imei in lines)
                {
                    var dev = new Device
                    {
                        ProductId = product.Id,
                        ProductName = product.Name,
                        ProductCode = product.Code,
                        Brand = product.Brand,
                        ModelName = product.ModelName,
                        Variant = product.Variant,
                        Color = product.Color,
                        IMEI1 = imei,
                        PurchasePrice = product.PurchasePrice,
                        SellingPrice = product.SellingPrice,
                        SupplierId = supplierId,
                        SupplierName = supplier?.CompanyName ?? "Supplier",
                        PurchaseDate = DateTime.UtcNow,
                        Status = "InStock"
                    };

                    var (devSuccess, devMsg) = await _stockService.StockInDeviceAsync(dev, executedBy);
                    if (devSuccess) successCount++;
                    else errors.Add($"{imei}: {devMsg}");
                }

                if (successCount > 0)
                {
                    string alertMsg = $"Batch Intake: {successCount} physical units stocked in successfully.";
                    if (errors.Any()) alertMsg += $" ({errors.Count} skipped due to duplicates or errors).";
                    TempData["ToastMessage"] = alertMsg;
                    TempData["ToastType"] = "success";
                }
                else
                {
                    TempData["ToastMessage"] = $"Failed to stock in batch: {string.Join(" | ", errors.Take(2))}";
                    TempData["ToastType"] = "danger";
                }

                return RedirectToAction(nameof(StockIn));
            }

            // Mode 3: Bulk / Quantity Stock In
            if (form.Quantity <= 0)
            {
                TempData["ToastMessage"] = "Stock in quantity must be at least 1 unit.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(StockIn));
            }

            string reasonText = string.IsNullOrWhiteSpace(form.Reason) ? "Factory Shipment" : form.Reason;
            if (!string.IsNullOrWhiteSpace(form.Notes)) reasonText += $" ({form.Notes.Trim()})";

            var stockSuccess = await _stockService.StockInAsync(product.Id, form.Quantity, reasonText, executedBy);
            if (stockSuccess)
            {
                await _auditLogService.LogActivityAsync(
                    "SUPPLIER_STOCK_IN",
                    executedBy,
                    product.Name,
                    $"Supplier stocked in {form.Quantity} units for '{product.Name}'. Reason: {reasonText}");

                TempData["ToastMessage"] = $"Successfully added {form.Quantity} units to '{product.Name}'.";
                TempData["ToastType"] = "success";
            }
            else
            {
                TempData["ToastMessage"] = "An error occurred while adding stock.";
                TempData["ToastType"] = "danger";
            }

            return RedirectToAction(nameof(StockIn));
        }

        [HttpGet]
        public async Task<IActionResult> StockOut(string? search, int page = 1)
        {
            var supplierId = CurrentSupplierId;
            if (string.IsNullOrEmpty(supplierId)) return RedirectToAction("Login", "Account");
            var supplier = await _supplierService.GetSupplierByIdAsync(supplierId);

            var allProducts = await _productRepository.GetAllAsync();
            var supplierProducts = allProducts.Where(p => p.SupplierId == supplierId).OrderBy(p => p.Name).ToList();
            var productIds = supplierProducts.Select(p => p.Id).ToHashSet();

            // Load Stock Out transactions for this supplier's products
            var allTransactions = await _transactionRepository.GetAllAsync();
            var stockOutTransactions = allTransactions
                .Where(t => productIds.Contains(t.ProductId) && t.Type == "Stock Out")
                .OrderByDescending(t => t.Timestamp)
                .ToList();

            var today = DateTime.UtcNow.Date;
            int totalUnits = stockOutTransactions.Sum(t => t.Quantity);
            int todayUnits = stockOutTransactions.Where(t => t.Timestamp.Date == today).Sum(t => t.Quantity);

            var prodDict = supplierProducts.ToDictionary(p => p.Id);
            decimal totalValuation = stockOutTransactions.Sum(t => {
                if (prodDict.TryGetValue(t.ProductId, out var prod)) return prod.PurchasePrice * t.Quantity;
                return t.UnitCost * t.Quantity;
            });

            var filtered = stockOutTransactions.AsEnumerable();
            if (!string.IsNullOrWhiteSpace(search))
            {
                var q = search.Trim().ToLowerInvariant();
                filtered = filtered.Where(t => 
                    (t.ProductName != null && t.ProductName.ToLowerInvariant().Contains(q)) ||
                    (t.ProductCode != null && t.ProductCode.ToLowerInvariant().Contains(q)) ||
                    (t.Reason != null && t.Reason.ToLowerInvariant().Contains(q)) ||
                    (t.IMEI != null && t.IMEI.ToLowerInvariant().Contains(q))
                );
            }

            int pageSize = 15;
            int totalFiltered = filtered.Count();
            int totalPages = (int)System.Math.Ceiling((double)totalFiltered / pageSize);
            if (totalPages < 1) totalPages = 1;
            if (page < 1) page = 1;
            if (page > totalPages) page = totalPages;

            var pagedList = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var vm = new SupplierStockOutViewModel
            {
                Supplier = supplier,
                Products = supplierProducts,
                Transactions = pagedList,
                TotalStockOutUnits = totalUnits,
                TodayStockOutUnits = todayUnits,
                TotalStockOutValuation = totalValuation,
                TotalTransactionsCount = stockOutTransactions.Count,
                SearchQuery = search,
                CurrentPage = page,
                PageSize = pageSize,
                TotalPages = totalPages,
                TotalFilteredCount = totalFiltered
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StockOut(SupplierStockOutViewModel form, string stockOutMode)
        {
            var supplierId = CurrentSupplierId;
            if (string.IsNullOrEmpty(supplierId)) return RedirectToAction("Login", "Account");
            var supplier = await _supplierService.GetSupplierByIdAsync(supplierId);
            var executedBy = supplier?.CompanyName ?? "Supplier";

            if (string.IsNullOrWhiteSpace(form.ProductId))
            {
                TempData["ToastMessage"] = "Please select a valid product to stock out.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(StockOut));
            }

            var product = await _productRepository.GetByIdAsync(form.ProductId);
            if (product == null || product.SupplierId != supplierId)
            {
                TempData["ToastMessage"] = "Unauthorized: Product does not belong to your vendor catalog.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(StockOut));
            }

            // Mode 1: Specific Device IMEI Issue
            if (stockOutMode == "SpecificDevice")
            {
                if (string.IsNullOrWhiteSpace(form.DeviceId))
                {
                    TempData["ToastMessage"] = "Please select a specific device IMEI to issue out.";
                    TempData["ToastType"] = "danger";
                    return RedirectToAction(nameof(StockOut));
                }

                var device = await _deviceRepository.GetByIdAsync(form.DeviceId);
                if (device == null || (device.SupplierId != supplierId && device.ProductId != product.Id))
                {
                    TempData["ToastMessage"] = "Invalid device selection.";
                    TempData["ToastType"] = "danger";
                    return RedirectToAction(nameof(StockOut));
                }

                string statusReason = string.IsNullOrWhiteSpace(form.Reason) ? "Dispatched" : form.Reason;
                var (devSuccess, devMsg) = await _stockService.StockOutDeviceAsync(device.Id, statusReason, executedBy);
                TempData["ToastMessage"] = devMsg;
                TempData["ToastType"] = devSuccess ? "success" : "danger";
                return RedirectToAction(nameof(StockOut));
            }

            // Mode 2: Quantity Stock Out
            if (form.Quantity <= 0)
            {
                TempData["ToastMessage"] = "Stock out quantity must be at least 1 unit.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(StockOut));
            }

            if (product.CurrentStock < form.Quantity)
            {
                TempData["ToastMessage"] = $"Insufficient stock! Available stock is {product.CurrentStock} units, but requested {form.Quantity} units.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(StockOut));
            }

            string reasonText = string.IsNullOrWhiteSpace(form.Reason) ? "Dispatched to Retailer" : form.Reason;
            if (!string.IsNullOrWhiteSpace(form.Notes)) reasonText += $" ({form.Notes.Trim()})";

            var stockSuccess = await _stockService.StockOutAsync(product.Id, form.Quantity, reasonText, executedBy);
            if (stockSuccess)
            {
                await _auditLogService.LogActivityAsync(
                    "SUPPLIER_STOCK_OUT",
                    executedBy,
                    product.Name,
                    $"Supplier stocked out {form.Quantity} units from '{product.Name}'. Reason: {reasonText}");

                TempData["ToastMessage"] = $"Successfully deducted {form.Quantity} units from '{product.Name}'.";
                TempData["ToastType"] = "success";
            }
            else
            {
                TempData["ToastMessage"] = "An error occurred while stocking out.";
                TempData["ToastType"] = "danger";
            }

            return RedirectToAction(nameof(StockOut));
        }

        [HttpGet]
        public async Task<IActionResult> GetSupplierProductDevices(string productId)
        {
            var supplierId = CurrentSupplierId;
            if (string.IsNullOrEmpty(supplierId) || string.IsNullOrEmpty(productId))
            {
                return Json(new List<object>());
            }

            var product = await _productRepository.GetByIdAsync(productId);
            if (product == null || product.SupplierId != supplierId)
            {
                return Json(new List<object>());
            }

            var allDevices = await _deviceRepository.GetAllAsync();
            var productDevices = allDevices
                .Where(d => d.ProductId == productId && d.Status == "InStock")
                .OrderBy(d => d.IMEI1)
                .Select(d => new
                {
                    id = d.Id,
                    imei1 = d.IMEI1,
                    imei2 = d.IMEI2,
                    serialNumber = d.SerialNumber,
                    displayText = $"IMEI: {d.IMEI1}" + (!string.IsNullOrEmpty(d.Variant) ? $" ({d.Variant})" : "")
                })
                .ToList();

            return Json(productDevices);
        }

        #endregion
    }
}
