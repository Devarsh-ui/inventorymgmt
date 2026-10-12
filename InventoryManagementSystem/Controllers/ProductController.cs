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
    public class ProductController : Controller
    {
        private readonly IProductService _productService;
        private readonly ICategoryService _categoryService;
        private readonly IImageService _imageService;
        private readonly IAuditLogService _auditLogService;
        private readonly INotificationRepository _notificationRepository;
        private readonly IMobileSpecSearchService _specSearchService;
        private readonly IDeviceRepository _deviceRepository;
        private readonly IDeviceService _deviceService;
        private readonly ISupplierService _supplierService;
        private readonly ILicenseService _licenseService;
        private readonly IAuthService _authService;

        public ProductController(
            IProductService productService,
            ICategoryService categoryService,
            IImageService imageService,
            IAuditLogService auditLogService,
            INotificationRepository notificationRepository,
            IMobileSpecSearchService specSearchService,
            IDeviceRepository deviceRepository,
            IDeviceService deviceService,
            ISupplierService supplierService,
            ILicenseService licenseService,
            IAuthService authService)
        {
            _productService = productService;
            _categoryService = categoryService;
            _imageService = imageService;
            _auditLogService = auditLogService;
            _notificationRepository = notificationRepository;
            _specSearchService = specSearchService;
            _deviceRepository = deviceRepository;
            _deviceService = deviceService;
            _supplierService = supplierService;
            _licenseService = licenseService;
            _authService = authService;
        }



        public async Task<IActionResult> Index(
            string? search, string? categoryId, string? brand, string? modelName, 
            string? stockStatus, string? statusFilter, decimal? minPrice, decimal? maxPrice, 
            int? minStock, int? maxStock, string? productSource, string? sortBy, bool isDescending = false, int page = 1)
        {
            const int pageSize = 10;
            var products = await _productService.GetPagedProductsAsync(
                search, categoryId, sortBy, isDescending, page, pageSize, 
                brand, modelName, stockStatus, statusFilter, minPrice, maxPrice, minStock, maxStock, productSource);

            var totalItems = await _productService.GetFilteredCountAsync(
                search, categoryId, brand, modelName, stockStatus, statusFilter, minPrice, maxPrice, minStock, maxStock, productSource);

            var categories = await _categoryService.GetActiveCategoriesAsync();

            var viewModel = new ProductListViewModel
            {
                Products = products,
                Categories = categories,
                Search = search,
                SelectedCategoryId = categoryId,
                Brand = brand,
                ModelName = modelName,
                StockStatus = stockStatus,
                StatusFilter = statusFilter,
                ProductSource = productSource,
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                MinStock = minStock,
                MaxStock = maxStock,
                SortBy = sortBy,
                IsDescending = isDescending,
                CurrentPage = page,
                TotalItems = totalItems,
                PageSize = pageSize,
                TotalPages = (int)System.Math.Ceiling((double)totalItems / pageSize)
            };

            var tenantId = User.FindFirst("TenantId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.GroupSid)?.Value;
            var limitCheck = await _licenseService.CheckPackageLimitAsync(tenantId ?? string.Empty, "Products");
            ViewBag.LimitReached = !limitCheck.Allowed;
            ViewBag.LimitMessage = limitCheck.Message;

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Details(string id)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null) return NotFound();

            var categoryName = "N/A";
            if (!string.IsNullOrEmpty(product.CategoryId))
            {
                var category = await _categoryService.GetCategoryByIdAsync(product.CategoryId);
                if (category != null) categoryName = category.Name;
            }

            if (!string.IsNullOrEmpty(product.SupplierId))
            {
                var supplier = await _supplierService.GetSupplierByIdAsync(product.SupplierId);
                ViewBag.Supplier = supplier;
            }

            ViewBag.CategoryName = categoryName;
            return View(product);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var tenantId = User.FindFirst("TenantId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.GroupSid)?.Value;
            var limitCheck = await _licenseService.CheckPackageLimitAsync(tenantId ?? string.Empty, "Products");
            ViewBag.LimitReached = !limitCheck.Allowed;
            ViewBag.LimitMessage = limitCheck.Message;

            var model = new ProductCreateViewModel();
            await PopulateCategoriesList(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ProductCreateViewModel model)
        {
            // Check Subscription Package Product Limit
            var tenantId = User.FindFirst("TenantId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.GroupSid)?.Value;
            var limitCheck = await _licenseService.CheckPackageLimitAsync(tenantId ?? string.Empty, "Products");
            if (!limitCheck.Allowed)
            {
                ModelState.AddModelError(string.Empty, limitCheck.Message);
                await PopulateCategoriesList(model);
                return View(model);
            }

            // Validate SKU Code uniqueness & default SKU/Barcode
            if (string.IsNullOrWhiteSpace(model.Code))
            {
                model.Code = !string.IsNullOrWhiteSpace(model.ModelName) ? $"{model.Brand}-{model.ModelName}".Replace(" ", "-").ToUpper() : $"PROD-{Guid.NewGuid().ToString("N")[..8].ToUpper()}";
                ModelState.Remove(nameof(model.Code));
            }
            else
            {
                var existingByCode = await _productService.GetProductByCodeAsync(model.Code);
                if (existingByCode != null)
                {
                    ModelState.AddModelError(nameof(model.Code), "Product SKU Code is already in use.");
                }
            }

            if (string.IsNullOrWhiteSpace(model.Barcode))
            {
                model.Barcode = "890" + Random.Shared.Next(100000000, 999999999).ToString();
                ModelState.Remove(nameof(model.Barcode));
            }
            else
            {
                var existingByBarcode = await _productService.GetProductByBarcodeAsync(model.Barcode);
                if (existingByBarcode != null)
                {
                    ModelState.AddModelError(nameof(model.Barcode), "Barcode is already in use by another product.");
                }
            }

            ModelState.Remove("Ram");
            ModelState.Remove(nameof(model.Ram));
            foreach (var key in ModelState.Keys.Where(k => k.Contains("Ram") || k.Contains("Sku") || k.Contains("Variant")).ToList())
            {
                ModelState.Remove(key);
            }

            if (!ModelState.IsValid)
            {
                var errorSummary = string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                Console.WriteLine($"[PRODUCT CREATE ERROR] Validation failed: {errorSummary}");
                TempData["ToastMessage"] = $"Validation failed: {errorSummary}";
                TempData["ToastType"] = "danger";
                await PopulateCategoriesList(model);
                return View(model);
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
                SellingPrice = model.SellingPrice,
                CurrentStock = model.InitialStock,
                MinimumStock = model.MinimumStock,
                Description = model.Description ?? string.Empty,
                Status = model.Status,
                Specs = model.Specs ?? new MobileSpecifications()
            };

            // Sync top-level fields with Specs
            if (model.Specs != null)
            {
                if (!string.IsNullOrWhiteSpace(model.Specs.OperatingSystem))
                    product.OperatingSystem = model.Specs.OperatingSystem;
                if (!string.IsNullOrWhiteSpace(model.Specs.ProcessorName))
                    product.Processor = model.Specs.ProcessorName;
                if (!string.IsNullOrWhiteSpace(model.Specs.DisplaySize))
                    product.DisplaySize = model.Specs.DisplaySize;
                if (model.Specs.BatteryCapacityMah > 0)
                    product.BatteryCapacity = $"{model.Specs.BatteryCapacityMah} mAh";
                if (!string.IsNullOrWhiteSpace(model.Specs.SimType))
                    product.SimType = model.Specs.SimType;
            }

            // Image Uploads (Primary + Up to 50 Gallery Images)
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
                    return View(model);
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

            // Process Variants & Colors
            var processedVariants = await ProcessVariantsAsync(model.Variants, model.ProductImages);
            if (processedVariants.Any())
            {
                product.Variants = processedVariants;
                int variantStockSum = processedVariants.Sum(v => v.CurrentStock > 0 ? v.CurrentStock : v.InitialStock);
                if (variantStockSum > 0 || product.CurrentStock == 0)
                {
                    product.CurrentStock = variantStockSum;
                }
                var primaryVariant = processedVariants.First();
                var primaryColor = primaryVariant.Colors.FirstOrDefault();

                product.Ram = primaryVariant.Ram ?? string.Empty;
                product.Storage = primaryVariant.Storage ?? string.Empty;
                product.Variant = primaryVariant.DisplayVariantName;

                decimal effectivePurchase = primaryVariant.PurchasePrice > 0 ? primaryVariant.PurchasePrice : (primaryColor?.PurchasePrice ?? 0);
                decimal effectiveSelling = primaryVariant.SellingPrice > 0 ? primaryVariant.SellingPrice : (primaryColor?.SellingPrice ?? 0);

                if (effectivePurchase > 0) product.PurchasePrice = effectivePurchase;
                if (effectiveSelling > 0) product.SellingPrice = effectiveSelling;
                if (primaryVariant.SupplierPrice > 0) product.SupplierPrice = primaryVariant.SupplierPrice;

                if (primaryColor != null)
                {
                    product.Color = primaryColor.Name;
                    if (!string.IsNullOrEmpty(primaryColor.ImageUrl)) product.ImageUrl = primaryColor.ImageUrl;
                    if (primaryColor.ImageUrls.Any()) product.ImageUrls = primaryColor.ImageUrls;
                }
            }

            // Check if Apple / iOS
            var categoryObj = !string.IsNullOrEmpty(product.CategoryId) ? await _categoryService.GetCategoryByIdAsync(product.CategoryId) : null;
            bool isApple = (categoryObj?.Name?.Contains("ios", StringComparison.OrdinalIgnoreCase) == true) ||
                           (categoryObj?.Name?.Contains("apple", StringComparison.OrdinalIgnoreCase) == true) ||
                           (product.Brand?.Contains("apple", StringComparison.OrdinalIgnoreCase) == true) ||
                           (product.Name?.Contains("iphone", StringComparison.OrdinalIgnoreCase) == true) ||
                           (product.Name?.Contains("ipad", StringComparison.OrdinalIgnoreCase) == true) ||
                           (product.Name?.Contains("apple", StringComparison.OrdinalIgnoreCase) == true) ||
                           (product.ModelName?.Contains("iphone", StringComparison.OrdinalIgnoreCase) == true) ||
                           (product.ModelName?.Contains("ipad", StringComparison.OrdinalIgnoreCase) == true) ||
                           (product.Specs?.OperatingSystem?.Equals("iOS", StringComparison.OrdinalIgnoreCase) == true);

            if (isApple)
            {
                product.Ram = string.Empty;
                product.OperatingSystem = "iOS";
                if (product.Specs != null)
                {
                    product.Specs.OperatingSystem = "iOS";
                    product.Specs.RamType = string.Empty;
                }
                if (product.Variants != null)
                {
                    foreach (var v in product.Variants)
                    {
                        v.Ram = string.Empty;
                    }
                }
                if (processedVariants.Any())
                {
                    product.Variant = processedVariants.First().DisplayVariantName;
                }
            }

            product.ImageUrls = imageUrls;

            try
            {
                await _productService.CreateProductAsync(product);
                await _auditLogService.LogActivityAsync("Product Added", User.Identity?.Name ?? "System", $"Product: {product.Name}", $"Added SKU: {product.Code} with initial stock {product.CurrentStock}.");

                // Save system notification
                await _notificationRepository.CreateAsync(new Notification
                {
                    Type = "Success",
                    Title = "Product Added",
                    Message = $"Product '{product.Name}' has been added with {product.CurrentStock} units.",
                    Timestamp = DateTime.UtcNow
                });

                TempData["ToastMessage"] = "Product added successfully!";
                TempData["ToastType"] = "success";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Failed to add product: {ex.Message}");
                await PopulateCategoriesList(model);
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> Edit(string id)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null) return NotFound();

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
                Ram = product.Ram,
                Storage = product.Storage,
                PurchasePrice = product.PurchasePrice,
                SellingPrice = product.SellingPrice,
                MinimumStock = product.MinimumStock,
                Description = product.Description,
                Status = product.Status,
                CurrentImageUrl = product.ImageUrl,
                ExistingImageUrls = product.ImageUrls ?? new List<string>(),
                Specs = product.Specs ?? new MobileSpecifications()
            };

            var effectiveVariants = product.GetEffectiveVariants();
            if (effectiveVariants != null && effectiveVariants.Any())
            {
                model.Variants = effectiveVariants.Select(v => new ProductVariantInputModel
                {
                    VariantId = v.VariantId,
                    Ram = v.Ram,
                    Storage = v.Storage,
                    Sku = v.Sku,
                    SupplierPrice = v.SupplierPrice,
                    PurchasePrice = v.PurchasePrice,
                    SellingPrice = v.SellingPrice,
                    Mrp = v.Mrp,
                    InitialStock = v.InitialStock,
                    CurrentStock = v.CurrentStock,
                    Colors = (v.Colors ?? new List<ProductColor>()).Select(c => new ProductColorInputModel
                    {
                        ColorId = c.ColorId,
                        Name = c.Name,
                        Code = c.Code,
                        ImageUrl = c.ImageUrl,
                        ExistingImageUrls = c.ImageUrls ?? new List<string>(),
                        InitialStock = c.InitialStock,
                        CurrentStock = c.CurrentStock,
                        PurchasePrice = c.PurchasePrice,
                        SellingPrice = c.SellingPrice
                    }).ToList()
                }).ToList();
            }

            await PopulateCategoriesList(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(ProductEditViewModel model)
        {
            if (string.IsNullOrWhiteSpace(model.Id))
            {
                TempData["ToastMessage"] = "Invalid product ID. Please try again.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(Index));
            }

            var existingProduct = await _productService.GetProductByIdAsync(model.Id);
            if (existingProduct == null)
            {
                return NotFound();
            }

            if (string.IsNullOrWhiteSpace(model.Code))
            {
                model.Code = existingProduct.Code;
                ModelState.Remove(nameof(model.Code));
            }
            else
            {
                var existingByCode = await _productService.GetProductByCodeAsync(model.Code);
                if (existingByCode != null && existingByCode.Id != model.Id)
                {
                    ModelState.AddModelError(nameof(model.Code), "Product SKU Code is already in use by another product.");
                }
            }

            if (string.IsNullOrWhiteSpace(model.Barcode))
            {
                model.Barcode = !string.IsNullOrWhiteSpace(existingProduct.Barcode) ? existingProduct.Barcode : "890" + Random.Shared.Next(100000000, 999999999).ToString();
                ModelState.Remove(nameof(model.Barcode));
            }
            else
            {
                var existingByBarcode = await _productService.GetProductByBarcodeAsync(model.Barcode);
                if (existingByBarcode != null && existingByBarcode.Id != model.Id)
                {
                    ModelState.AddModelError(nameof(model.Barcode), "Barcode is already in use by another product.");
                }
            }

            ModelState.Remove("Ram");
            ModelState.Remove(nameof(model.Ram));
            foreach (var key in ModelState.Keys.Where(k => k.Contains("Ram") || k.Contains("Sku") || k.Contains("Variant")).ToList())
            {
                ModelState.Remove(key);
            }

            if (!ModelState.IsValid)
            {
                var errorSummary = string.Join("; ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                Console.WriteLine($"[PRODUCT EDIT ERROR] Validation failed: {errorSummary}");
                TempData["ToastMessage"] = $"Validation failed: {errorSummary}";
                TempData["ToastType"] = "danger";
                await PopulateCategoriesList(model);
                return View(model);
            }

            existingProduct.Name = model.Name;
            existingProduct.Code = model.Code?.ToUpper() ?? existingProduct.Code;
            existingProduct.Barcode = model.Barcode ?? existingProduct.Barcode;
            existingProduct.CategoryId = model.CategoryId;
            existingProduct.ProductType = model.ProductType ?? "Smartphone";
            existingProduct.Brand = model.Brand ?? string.Empty;
            existingProduct.ModelName = model.ModelName ?? string.Empty;
            existingProduct.Variant = model.Variant ?? string.Empty;
            existingProduct.Color = model.Color ?? string.Empty;
            existingProduct.PurchasePrice = model.PurchasePrice;
            existingProduct.SellingPrice = model.SellingPrice;
            existingProduct.MinimumStock = model.MinimumStock;
            existingProduct.Description = model.Description ?? string.Empty;
            existingProduct.Status = model.Status;
            existingProduct.Specs = model.Specs ?? new MobileSpecifications();
            existingProduct.UpdatedDate = DateTime.UtcNow;

            // Sync top-level fields with Specs
            if (model.Specs != null)
            {
                if (!string.IsNullOrWhiteSpace(model.Specs.OperatingSystem))
                    existingProduct.OperatingSystem = model.Specs.OperatingSystem;
                if (!string.IsNullOrWhiteSpace(model.Specs.ProcessorName))
                    existingProduct.Processor = model.Specs.ProcessorName;
                if (!string.IsNullOrWhiteSpace(model.Specs.DisplaySize))
                    existingProduct.DisplaySize = model.Specs.DisplaySize;
                if (model.Specs.BatteryCapacityMah > 0)
                    existingProduct.BatteryCapacity = $"{model.Specs.BatteryCapacityMah} mAh";
                if (!string.IsNullOrWhiteSpace(model.Specs.SimType))
                    existingProduct.SimType = model.Specs.SimType;
            }

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
                    return View(model);
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

            // Process Variants & Colors for Edit
            var updatedVariants = await ProcessVariantsAsync(model.Variants, model.ProductImages);
            if (updatedVariants.Any())
            {
                existingProduct.Variants = updatedVariants;
                int variantStockSum = updatedVariants.Sum(v => v.CurrentStock > 0 ? v.CurrentStock : v.InitialStock);
                if (variantStockSum > 0 || existingProduct.CurrentStock == 0)
                {
                    existingProduct.CurrentStock = variantStockSum;
                }
                var primaryVariant = updatedVariants.First();
                var primaryColor = primaryVariant.Colors.FirstOrDefault();

                existingProduct.Ram = primaryVariant.Ram ?? string.Empty;
                existingProduct.Storage = primaryVariant.Storage ?? string.Empty;
                existingProduct.Variant = primaryVariant.DisplayVariantName;

                decimal effectivePurchase = primaryVariant.PurchasePrice > 0 ? primaryVariant.PurchasePrice : (primaryColor?.PurchasePrice ?? 0);
                decimal effectiveSelling = primaryVariant.SellingPrice > 0 ? primaryVariant.SellingPrice : (primaryColor?.SellingPrice ?? 0);

                if (effectivePurchase > 0) existingProduct.PurchasePrice = effectivePurchase;
                if (effectiveSelling > 0) existingProduct.SellingPrice = effectiveSelling;
                if (primaryVariant.SupplierPrice > 0) existingProduct.SupplierPrice = primaryVariant.SupplierPrice;

                if (primaryColor != null)
                {
                    existingProduct.Color = primaryColor.Name;
                    if (!string.IsNullOrEmpty(primaryColor.ImageUrl)) existingProduct.ImageUrl = primaryColor.ImageUrl;
                    if (primaryColor.ImageUrls.Any()) existingProduct.ImageUrls = primaryColor.ImageUrls;
                }
            }

            // Check if Apple / iOS
            var categoryObj = !string.IsNullOrEmpty(existingProduct.CategoryId) ? await _categoryService.GetCategoryByIdAsync(existingProduct.CategoryId) : null;
            bool isApple = (categoryObj?.Name?.Contains("ios", StringComparison.OrdinalIgnoreCase) == true) ||
                           (categoryObj?.Name?.Contains("apple", StringComparison.OrdinalIgnoreCase) == true) ||
                           (existingProduct.Brand?.Contains("apple", StringComparison.OrdinalIgnoreCase) == true) ||
                           (existingProduct.Name?.Contains("iphone", StringComparison.OrdinalIgnoreCase) == true) ||
                           (existingProduct.Name?.Contains("ipad", StringComparison.OrdinalIgnoreCase) == true) ||
                           (existingProduct.Name?.Contains("apple", StringComparison.OrdinalIgnoreCase) == true) ||
                           (existingProduct.ModelName?.Contains("iphone", StringComparison.OrdinalIgnoreCase) == true) ||
                           (existingProduct.ModelName?.Contains("ipad", StringComparison.OrdinalIgnoreCase) == true) ||
                           (existingProduct.Specs?.OperatingSystem?.Equals("iOS", StringComparison.OrdinalIgnoreCase) == true);

            if (isApple)
            {
                existingProduct.Ram = string.Empty;
                existingProduct.OperatingSystem = "iOS";
                if (existingProduct.Specs != null)
                {
                    existingProduct.Specs.OperatingSystem = "iOS";
                    existingProduct.Specs.RamType = string.Empty;
                }
                if (existingProduct.Variants != null)
                {
                    foreach (var v in existingProduct.Variants)
                    {
                        v.Ram = string.Empty;
                    }
                }
                if (updatedVariants.Any())
                {
                    existingProduct.Variant = updatedVariants.First().DisplayVariantName;
                }
            }

            existingProduct.ImageUrls = imageUrls;

            try
            {
                await _productService.UpdateProductAsync(existingProduct);
                await _auditLogService.LogActivityAsync("Product Updated", User.Identity?.Name ?? "System", $"Product ID: {model.Id}", $"Updated product details for {model.Name}.");

                TempData["ToastMessage"] = "Product updated successfully!";
                TempData["ToastType"] = "success";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, $"Failed to update product: {ex.Message}");
                await PopulateCategoriesList(model);
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> GetProductVariants(string productId)
        {
            if (string.IsNullOrWhiteSpace(productId)) return Json(new List<object>());
            var product = await _productService.GetProductByIdAsync(productId);
            if (product == null) return Json(new List<object>());

            var variants = product.GetEffectiveVariants();
            var result = new List<object>();

            foreach (var v in variants)
            {
                var colorList = new List<object>();
                foreach (var c in v.Colors ?? new List<ProductColor>())
                {
                    var availableDevices = await _deviceService.GetAvailableDevicesForVariantAsync(product.Id, v.VariantId, c.Name);
                    int stockCount = availableDevices.Count();

                    colorList.Add(new
                    {
                        colorId = c.ColorId,
                        name = c.Name,
                        code = c.Code,
                        imageUrl = !string.IsNullOrEmpty(c.ImageUrl) ? c.ImageUrl : product.ImageUrl,
                        imageUrls = c.ImageUrls != null && c.ImageUrls.Any() ? c.ImageUrls : product.ImageUrls,
                        availableStock = stockCount
                    });
                }

                result.Add(new
                {
                    variantId = v.VariantId,
                    ram = v.Ram,
                    storage = v.Storage,
                    displayVariant = v.DisplayVariantName,
                    sku = v.Sku,
                    purchasePrice = v.PurchasePrice > 0 ? v.PurchasePrice : product.PurchasePrice,
                    sellingPrice = v.SellingPrice > 0 ? v.SellingPrice : product.SellingPrice,
                    supplierPrice = v.SupplierPrice > 0 ? v.SupplierPrice : product.SupplierPrice,
                    mrp = v.Mrp > 0 ? v.Mrp : product.Mrp,
                    colors = colorList
                });
            }

            return Json(result);
        }

        private async Task<List<ProductVariant>> ProcessVariantsAsync(List<ProductVariantInputModel>? variantInputs, List<IFormFile>? productImages)
        {
            var result = new List<ProductVariant>();
            if (variantInputs == null || !variantInputs.Any()) return result;

            var seenCombinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var vInput in variantInputs)
            {
                var ram = (vInput.Ram ?? string.Empty).Trim();
                var storage = (vInput.Storage ?? string.Empty).Trim();
                var sku = (vInput.Sku ?? string.Empty).Trim();

                var variant = new ProductVariant
                {
                    VariantId = !string.IsNullOrWhiteSpace(vInput.VariantId) ? vInput.VariantId : Guid.NewGuid().ToString("N"),
                    Ram = ram,
                    Storage = storage,
                    Sku = sku,
                    SupplierPrice = vInput.SupplierPrice,
                    PurchasePrice = vInput.PurchasePrice,
                    SellingPrice = vInput.SellingPrice,
                    Mrp = vInput.Mrp,
                    InitialStock = vInput.InitialStock,
                    CurrentStock = vInput.CurrentStock > 0 ? vInput.CurrentStock : vInput.InitialStock,
                    Colors = new List<ProductColor>()
                };

                if (vInput.Colors != null && vInput.Colors.Any())
                {
                    foreach (var cInput in vInput.Colors)
                    {
                        var colorName = (cInput.Name ?? string.Empty).Trim();
                        if (string.IsNullOrWhiteSpace(colorName)) continue;

                        var comboKey = $"{ram}|{storage}|{colorName}";
                        if (seenCombinations.Contains(comboKey))
                        {
                            continue; // Prevent duplicate RAM+Storage+Color combination
                        }
                        seenCombinations.Add(comboKey);

                        var colorObj = new ProductColor
                        {
                            ColorId = !string.IsNullOrWhiteSpace(cInput.ColorId) ? cInput.ColorId : Guid.NewGuid().ToString("N"),
                            Name = colorName,
                            Code = (cInput.Code ?? string.Empty).Trim(),
                            ImageUrl = cInput.ImageUrl ?? string.Empty,
                            ImageUrls = cInput.ExistingImageUrls != null ? new List<string>(cInput.ExistingImageUrls) : new List<string>(),
                            InitialStock = cInput.InitialStock,
                            CurrentStock = cInput.CurrentStock > 0 ? cInput.CurrentStock : cInput.InitialStock,
                            PurchasePrice = cInput.PurchasePrice > 0 ? cInput.PurchasePrice : vInput.PurchasePrice,
                            SellingPrice = cInput.SellingPrice > 0 ? cInput.SellingPrice : vInput.SellingPrice
                        };

                        if (cInput.ImageFiles != null && cInput.ImageFiles.Any())
                        {
                            foreach (var file in cInput.ImageFiles)
                            {
                                if (file == null || file.Length == 0) continue;
                                var uploadResult = await _imageService.UploadImageAsync(file, "products");
                                if (uploadResult.IsSuccess)
                                {
                                    if (string.IsNullOrEmpty(colorObj.ImageUrl))
                                    {
                                        colorObj.ImageUrl = uploadResult.SecureUrl;
                                    }
                                    if (!colorObj.ImageUrls.Contains(uploadResult.SecureUrl))
                                    {
                                        colorObj.ImageUrls.Add(uploadResult.SecureUrl);
                                    }
                                }
                            }
                        }

                        variant.Colors.Add(colorObj);
                    }
                }

                if (variant.Colors.Any())
                {
                    int colorInitialSum = variant.Colors.Sum(c => c.InitialStock);
                    int colorCurrentSum = variant.Colors.Sum(c => c.CurrentStock);
                    if (colorInitialSum > 0 || colorCurrentSum > 0 || variant.InitialStock == 0)
                    {
                        variant.InitialStock = colorInitialSum;
                        variant.CurrentStock = colorCurrentSum;
                    }
                }

                result.Add(variant);
            }

            return result;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            var product = await _productService.GetProductByIdAsync(id);
            if (product == null) return NotFound();

            // Cleanup image in Cloudinary
            if (!string.IsNullOrEmpty(product.ImagePublicId))
            {
                await _imageService.DeleteImageAsync(product.ImagePublicId);
            }

            // Cascade delete all physical IMEI devices linked to this Product
            await _deviceRepository.DeleteByProductIdAsync(id);

            await _productService.DeleteProductAsync(id);
            await _auditLogService.LogActivityAsync("Product Deleted", User.Identity?.Name ?? "System", $"Product: {product.Name}", $"Deleted product SKU: {product.Code} and all associated IMEI devices.");

            await _notificationRepository.CreateAsync(new Notification
            {
                Type = "Danger",
                Title = "Product Deleted",
                Message = $"Product '{product.Name}' (SKU: {product.Code}) has been deleted.",
                Timestamp = DateTime.UtcNow
            });

            TempData["ToastMessage"] = "Product deleted successfully.";
            TempData["ToastType"] = "success";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> SearchSpecsOnline(string brand, string modelName, string variant, bool allowThirdPartyFallback = false, string? customUrl = null)
        {
            var user = User.Identity?.Name ?? "Admin";
            await _auditLogService.LogActivityAsync(
                "PRODUCT_SPECIFICATION_SEARCH_STARTED",
                user,
                $"Brand: {brand}, Model: {modelName}",
                $"Search started. Target: Official Manufacturer First. FallbackAllowed: {allowThirdPartyFallback}, CustomUrl: {customUrl}");

            var result = await _specSearchService.SearchSpecificationsAsync(brand, modelName, variant, allowThirdPartyFallback, customUrl);

            if (result.Success)
            {
                await _auditLogService.LogActivityAsync(
                    "PRODUCT_SPECIFICATION_SEARCH_COMPLETED",
                    user,
                    $"Brand: {brand}, Model: {modelName}",
                    $"Specs retrieved successfully. SourceType: {result.PrimarySourceType}, ExactMatched: {result.ExactModelMatched}, Confidence: {result.ConfidenceMatch}");
            }
            else
            {
                await _auditLogService.LogActivityAsync(
                    "PRODUCT_SPECIFICATION_SEARCH_FAILED",
                    user,
                    $"Brand: {brand}, Model: {modelName}",
                    $"Search failed: {result.ErrorMessage}");
            }

            return Json(result);
        }

        public class LogSpecAppliedRequest
        {
            public string Brand { get; set; } = string.Empty;
            public string ModelName { get; set; } = string.Empty;
            public string Variant { get; set; } = string.Empty;
            public string SourceUrl { get; set; } = string.Empty;
            public string SourceType { get; set; } = "Official Manufacturer";
        }

        [HttpPost]
        public async Task<IActionResult> LogSpecsApplied([FromBody] LogSpecAppliedRequest req)
        {
            var user = User.Identity?.Name ?? "Admin";
            await _auditLogService.LogActivityAsync(
                "PRODUCT_SPECIFICATIONS_APPLIED",
                user,
                $"Brand: {req.Brand}, Model: {req.ModelName}",
                $"Admin confirmed and applied specs to form. SourceType: {req.SourceType}, SourceURL: {req.SourceUrl}");

            return Json(new { success = true });
        }

        private async Task PopulateCategoriesList(ProductCreateViewModel model)
        {
            var categories = await _categoryService.GetActiveCategoriesAsync();
            model.Categories = categories.Select(c => new SelectListItem
            {
                Value = c.Id,
                Text = c.Name
            }).ToList();
        }

        private async Task PopulateCategoriesList(ProductEditViewModel model)
        {
            var categories = await _categoryService.GetActiveCategoriesAsync();
            model.Categories = categories.Select(c => new SelectListItem
            {
                Value = c.Id,
                Text = c.Name
            }).ToList();
        }
    }
}

// Simple ceiling helper since Math.Ceiling returns double/decimal and needs casting
public static class Math
{
    public static int CeRounding(double value)
    {
        return (int)global::System.Math.Ceiling(value);
    }
}
