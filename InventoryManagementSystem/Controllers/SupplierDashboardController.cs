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
            ISupplierPurchaseReturnService purchaseReturnService)
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
        public async Task<IActionResult> Orders(string? status, string? search, int page = 1)
        {
            var supplierId = CurrentSupplierId;
            int pageSize = 20;

            var orders = await _supplierOrderService.GetPagedOrdersAsync(search, supplierId, status, page, pageSize);
            var totalCount = await _supplierOrderService.GetFilteredCountAsync(search, supplierId, status);
            var statusCounts = await _supplierOrderService.GetOrderStatusCountsAsync(supplierId);

            ViewBag.Status = status;
            ViewBag.Search = search;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)System.Math.Ceiling((double)totalCount / pageSize);
            ViewBag.TotalCount = totalCount;
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
        public async Task<IActionResult> PurchaseReturns(string? search, string? status, string? reason, int page = 1)
        {
            int pageSize = 15;
            var supplierId = CurrentSupplierId;
            var returns = await _purchaseReturnService.GetPagedReturnsAsync(search, supplierId, status, reason, page, pageSize);
            var totalCount = await _purchaseReturnService.GetFilteredCountAsync(search, supplierId, status, reason);
            var statusCounts = await _purchaseReturnService.GetReturnStatusCountsAsync(supplierId);

            var viewModel = new PurchaseReturnListViewModel
            {
                Returns = returns,
                Search = search,
                SupplierId = supplierId,
                Status = status,
                Reason = reason,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                StatusCounts = statusCounts
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> PurchaseReturnDetails(string id)
        {
            var supplierId = CurrentSupplierId;
            var returnRecord = await _purchaseReturnService.GetReturnByIdAsync(id);

            // Server-side Ownership Validation: Ensure Supplier A cannot access Supplier B's returns
            if (returnRecord == null || !string.Equals(returnRecord.SupplierId, supplierId, StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            var viewModel = new PurchaseReturnDetailsViewModel
            {
                ReturnRecord = returnRecord
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdatePurchaseReturnStatus(string returnId, string newStatus, string? supplierNotes, string? rejectionReason)
        {
            var supplierId = CurrentSupplierId;
            var returnRecord = await _purchaseReturnService.GetReturnByIdAsync(returnId);

            // Server-side Ownership Validation
            if (returnRecord == null || !string.Equals(returnRecord.SupplierId, supplierId, StringComparison.OrdinalIgnoreCase))
            {
                return NotFound();
            }

            var updatedBy = User.Identity?.Name ?? "Supplier";
            var (success, message) = await _purchaseReturnService.UpdateReturnStatusAsync(returnId, newStatus, updatedBy, supplierNotes, rejectionReason);

            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";

            return RedirectToAction(nameof(PurchaseReturnDetails), new { id = returnId });
        }
    }
}
