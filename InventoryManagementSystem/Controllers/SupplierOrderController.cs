using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Controllers
{
    [Authorize(Roles = Role.Admin)]
    public class SupplierOrderController : Controller
    {
        private readonly ISupplierOrderService _supplierOrderService;
        private readonly ISupplierService _supplierService;
        private readonly IProductRepository _productRepository;
        private readonly ICategoryRepository _categoryRepository;

        public SupplierOrderController(
            ISupplierOrderService supplierOrderService,
            ISupplierService supplierService,
            IProductRepository productRepository,
            ICategoryRepository categoryRepository)
        {
            _supplierOrderService = supplierOrderService;
            _supplierService = supplierService;
            _productRepository = productRepository;
            _categoryRepository = categoryRepository;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? status, string? supplierId, string? search, int page = 1)
        {
            await _supplierService.CleanupOrphanedSupplierDataAsync();

            int pageSize = 20;
            var orders = await _supplierOrderService.GetPagedOrdersAsync(search, supplierId, status, page, pageSize);
            var totalCount = await _supplierOrderService.GetFilteredCountAsync(search, supplierId, status);
            var statusCounts = await _supplierOrderService.GetOrderStatusCountsAsync();
            var suppliers = await _supplierService.GetAllSuppliersAsync();

            ViewBag.Status = status;
            ViewBag.SupplierId = supplierId;
            ViewBag.Search = search;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)System.Math.Ceiling((double)totalCount / pageSize);
            ViewBag.TotalCount = totalCount;
            ViewBag.StatusCounts = statusCounts;
            ViewBag.Suppliers = suppliers;

            return View(orders);
        }

        [HttpGet]
        public async Task<IActionResult> Create(
            string? selectedSupplierId, 
            string? categoryId, 
            string? brand, 
            string? productName,
            string? search,
            string? modelName,
            decimal? minPrice,
            decimal? maxPrice,
            string? sortBy,
            string? supplierSort,
            string? brandSort)
        {
            await _supplierService.CleanupOrphanedSupplierDataAsync();

            var suppliers = (await _supplierService.GetAllSuppliersAsync()).ToList();
            var activeSupplierIds = suppliers.Select(s => s.Id).ToHashSet();
            var allProducts = (await _productRepository.GetAllAsync()).ToList();

            // Product counts per supplier
            var supplierProductCounts = allProducts
                .Where(p => !string.IsNullOrWhiteSpace(p.SupplierId) && activeSupplierIds.Contains(p.SupplierId))
                .GroupBy(p => p.SupplierId!)
                .ToDictionary(g => g.Key, g => g.Count());

            // Supplier Sorting
            switch (supplierSort?.ToLower())
            {
                case "products_desc":
                    suppliers = suppliers.OrderByDescending(s => supplierProductCounts.GetValueOrDefault(s.Id, 0)).ThenBy(s => s.CompanyName).ToList();
                    break;
                case "payable_desc":
                    suppliers = suppliers.OrderByDescending(s => s.OutstandingPayable).ThenBy(s => s.CompanyName).ToList();
                    break;
                case "name_desc":
                    suppliers = suppliers.OrderByDescending(s => s.CompanyName).ToList();
                    break;
                case "name_asc":
                default:
                    suppliers = suppliers.OrderBy(s => s.CompanyName).ToList();
                    break;
            }

            Supplier? selectedSupplier = null;
            var availableBrands = new List<BrandSummaryViewModel>();
            var availableCategories = new List<Category>();
            var availableModels = new List<string>();
            var availableProductNames = new List<string>();
            var displayProducts = new List<Product>();

            if (!string.IsNullOrWhiteSpace(selectedSupplierId))
            {
                selectedSupplier = suppliers.FirstOrDefault(s => s.Id == selectedSupplierId);
                if (selectedSupplier != null)
                {
                    // Strictly isolate products belonging to this selected supplier
                    var supplierProducts = allProducts.Where(p => p.SupplierId == selectedSupplierId).ToList();

                    // Calculate brands dynamically for this selected supplier only
                    var brandsQuery = supplierProducts
                        .Where(p => !string.IsNullOrWhiteSpace(p.Brand))
                        .GroupBy(p => p.Brand.Trim(), StringComparer.OrdinalIgnoreCase)
                        .Select(g => new BrandSummaryViewModel
                        {
                            Brand = g.First().Brand.Trim(),
                            ProductCount = g.Count(),
                            SampleModels = g.Select(p => p.ModelName).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().Take(3).ToList()
                        });

                    switch (brandSort?.ToLower())
                    {
                        case "count_desc":
                            availableBrands = brandsQuery.OrderByDescending(b => b.ProductCount).ThenBy(b => b.Brand).ToList();
                            break;
                        case "name_desc":
                            availableBrands = brandsQuery.OrderByDescending(b => b.Brand).ToList();
                            break;
                        case "name_asc":
                        default:
                            availableBrands = brandsQuery.OrderBy(b => b.Brand).ToList();
                            break;
                    }

                    // Available categories for this supplier's products
                    var categoryIds = supplierProducts
                        .Where(p => !string.IsNullOrWhiteSpace(p.CategoryId))
                        .Select(p => p.CategoryId!)
                        .Distinct()
                        .ToHashSet();

                    var allCats = await _categoryRepository.GetAllAsync();
                    availableCategories = allCats.Where(c => categoryIds.Contains(c.Id)).OrderBy(c => c.Name).ToList();

                    // If brand is chosen, narrow down products
                    if (!string.IsNullOrWhiteSpace(brand))
                    {
                        var brandProducts = supplierProducts.Where(p => p.Brand.Equals(brand.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();

                        availableModels = brandProducts
                            .Select(p => p.ModelName)
                            .Where(m => !string.IsNullOrWhiteSpace(m))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(m => m)
                            .ToList();

                        availableProductNames = brandProducts
                            .Select(p => p.Name)
                            .Where(n => !string.IsNullOrWhiteSpace(n))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .OrderBy(n => n)
                            .ToList();

                        var query = brandProducts.AsEnumerable();

                        if (!string.IsNullOrWhiteSpace(categoryId))
                        {
                            query = query.Where(p => p.CategoryId == categoryId);
                        }

                        if (!string.IsNullOrWhiteSpace(modelName))
                        {
                            query = query.Where(p => p.ModelName.Equals(modelName.Trim(), StringComparison.OrdinalIgnoreCase));
                        }

                        if (!string.IsNullOrWhiteSpace(productName))
                        {
                            query = query.Where(p => p.Name.Equals(productName.Trim(), StringComparison.OrdinalIgnoreCase));
                        }

                        if (minPrice.HasValue && minPrice.Value > 0)
                        {
                            query = query.Where(p => (p.SupplierPrice > 0 ? p.SupplierPrice : p.PurchasePrice) >= minPrice.Value);
                        }

                        if (maxPrice.HasValue && maxPrice.Value > 0)
                        {
                            query = query.Where(p => (p.SupplierPrice > 0 ? p.SupplierPrice : p.PurchasePrice) <= maxPrice.Value);
                        }

                        if (!string.IsNullOrWhiteSpace(search))
                        {
                            var s = search.Trim();
                            query = query.Where(p =>
                                (p.Name != null && p.Name.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                                (p.ModelName != null && p.ModelName.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                                (p.Variant != null && p.Variant.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                                (p.Ram != null && p.Ram.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                                (p.Storage != null && p.Storage.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                                (p.Color != null && p.Color.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                                (p.Code != null && p.Code.Contains(s, StringComparison.OrdinalIgnoreCase)));
                        }

                        // Product sorting: price low to high, price high to low, model name, stock, newest
                        switch (sortBy?.ToLower())
                        {
                            case "price_asc":
                                query = query.OrderBy(p => (p.SupplierPrice > 0 ? p.SupplierPrice : p.PurchasePrice));
                                break;
                            case "price_desc":
                                query = query.OrderByDescending(p => (p.SupplierPrice > 0 ? p.SupplierPrice : p.PurchasePrice));
                                break;
                            case "model_asc":
                                query = query.OrderBy(p => p.ModelName).ThenBy(p => p.Name);
                                break;
                            case "model_desc":
                                query = query.OrderByDescending(p => p.ModelName).ThenByDescending(p => p.Name);
                                break;
                            case "stock_desc":
                                query = query.OrderByDescending(p => p.CurrentStock);
                                break;
                            case "newest":
                            default:
                                query = query.OrderByDescending(p => p.CreatedDate);
                                break;
                        }

                        displayProducts = query.ToList();
                    }
                }
                else
                {
                    // Selected supplier was invalid/not found
                    selectedSupplierId = null;
                }
            }

            ViewBag.Suppliers = suppliers;
            ViewBag.SupplierProductCounts = supplierProductCounts;
            ViewBag.SupplierSort = supplierSort;
            ViewBag.SelectedSupplierId = selectedSupplierId;
            ViewBag.SelectedSupplier = selectedSupplier;
            ViewBag.AvailableBrands = availableBrands;
            ViewBag.BrandSort = brandSort;
            ViewBag.SelectedBrand = brand?.Trim();
            ViewBag.Categories = availableCategories;
            ViewBag.CategoryId = categoryId;
            ViewBag.AvailableModels = availableModels;
            ViewBag.ModelName = modelName?.Trim();
            ViewBag.AvailableProductNames = availableProductNames;
            ViewBag.ProductName = productName?.Trim();
            ViewBag.MinPrice = minPrice;
            ViewBag.MaxPrice = maxPrice;
            ViewBag.SortBy = sortBy;
            ViewBag.Search = search;
            ViewBag.SupplierDict = suppliers.ToDictionary(s => s.Id);

            return View(displayProducts);
        }

        [HttpPost]
        public async Task<IActionResult> Review([FromForm] string? supplierId, [FromForm] List<OrderItemFormInput> items, [FromForm] string? notes, [FromForm] DateTime? expectedDeliveryDate)
        {
            var validInputs = items?.Where(i => i.Quantity > 0).ToList() ?? new List<OrderItemFormInput>();

            if (!validInputs.Any())
            {
                TempData["ToastMessage"] = "Please enter an order quantity (> 0) for at least one product item.";
                TempData["ToastType"] = "warning";
                return RedirectToAction(nameof(Create), new { selectedSupplierId = supplierId });
            }

            if (string.IsNullOrWhiteSpace(supplierId))
            {
                TempData["ToastMessage"] = "Please select a supplier vendor for the purchase order.";
                TempData["ToastType"] = "warning";
                return RedirectToAction(nameof(Create));
            }

            var supplier = await _supplierService.GetSupplierByIdAsync(supplierId);
            if (supplier == null) return NotFound();

            var order = new SupplierOrder
            {
                SupplierId = supplierId,
                SupplierName = supplier.DisplayVendorName,
                SupplierVendorName = supplier.DisplayVendorName,
                SupplierCompanyName = supplier.DisplayCompanyName,
                SupplierEmail = supplier.Email,
                SupplierPhone = supplier.Phone,
                Notes = notes ?? string.Empty,
                ExpectedDeliveryDate = expectedDeliveryDate,
                Items = new List<SupplierOrderItem>()
            };

            foreach (var input in validInputs)
            {
                var p = await _productRepository.GetByIdAsync(input.ProductId);
                if (p == null)
                {
                    TempData["ToastMessage"] = $"Product with ID '{input.ProductId}' does not exist.";
                    TempData["ToastType"] = "danger";
                    return RedirectToAction(nameof(Create), new { selectedSupplierId = supplierId });
                }

                // Strict Supplier Security Verification
                if (p.SupplierId != supplierId)
                {
                    TempData["ToastMessage"] = $"Security Violation: Product '{p.Name}' does not belong to supplier '{supplier.CompanyName}'.";
                    TempData["ToastType"] = "danger";
                    return RedirectToAction(nameof(Create), new { selectedSupplierId = supplierId });
                }

                if (p.CurrentStock > 0 && input.Quantity > p.CurrentStock)
                {
                    TempData["ToastMessage"] = $"Cannot order {input.Quantity} units of '{p.Name}'. Available supplier stock is only {p.CurrentStock} units.";
                    TempData["ToastType"] = "danger";
                    return RedirectToAction(nameof(Create), new { selectedSupplierId = supplierId });
                }

                var item = new SupplierOrderItem
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    Brand = p.Brand,
                    Model = p.ModelName,
                    VariantId = input.VariantId ?? string.Empty,
                    ColorId = input.ColorId ?? string.Empty,
                    Variant = !string.IsNullOrWhiteSpace(input.Variant) ? input.Variant.Trim() : p.Variant,
                    Color = !string.IsNullOrWhiteSpace(input.Color) ? input.Color.Trim() : p.Color,
                    Ram = p.Ram,
                    Storage = p.Storage,
                    ImageUrl = p.ImageUrl,
                    Quantity = input.Quantity,
                    AvailableStock = p.CurrentStock,
                    UnitPrice = input.UnitPrice > 0 ? input.UnitPrice : (p.SupplierPrice > 0 ? p.SupplierPrice : p.PurchasePrice),
                };

                // Match variant & color details for accurate specs & stock display in review
                if (p.Variants != null && p.Variants.Any())
                {
                    ProductVariant? vMatch = null;
                    if (!string.IsNullOrWhiteSpace(input.VariantId))
                    {
                        vMatch = p.Variants.FirstOrDefault(v => v.VariantId == input.VariantId);
                    }
                    if (vMatch == null && !string.IsNullOrWhiteSpace(input.Variant))
                    {
                        vMatch = p.Variants.FirstOrDefault(v =>
                            v.DisplayVariantName.Equals(input.Variant.Trim(), StringComparison.OrdinalIgnoreCase) ||
                            v.Storage.Equals(input.Variant.Trim(), StringComparison.OrdinalIgnoreCase) ||
                            v.Storage.Replace("GB", "", StringComparison.OrdinalIgnoreCase).Trim().Equals(input.Variant.Replace("GB", "", StringComparison.OrdinalIgnoreCase).Trim(), StringComparison.OrdinalIgnoreCase) ||
                            $"{v.Ram} / {v.Storage}".Equals(input.Variant.Trim(), StringComparison.OrdinalIgnoreCase));
                    }

                    if (vMatch != null)
                    {
                        item.VariantId = vMatch.VariantId;
                        item.Variant = vMatch.DisplayVariantName;
                        if (!string.IsNullOrWhiteSpace(vMatch.Ram)) item.Ram = vMatch.Ram;
                        if (!string.IsNullOrWhiteSpace(vMatch.Storage)) item.Storage = vMatch.Storage;
                        item.AvailableStock = vMatch.CurrentStock;

                        if (vMatch.Colors != null && vMatch.Colors.Any())
                        {
                            ProductColor? cMatch = null;
                            if (!string.IsNullOrWhiteSpace(input.ColorId))
                            {
                                cMatch = vMatch.Colors.FirstOrDefault(c => c.ColorId == input.ColorId);
                            }
                            if (cMatch == null && !string.IsNullOrWhiteSpace(input.Color))
                            {
                                cMatch = vMatch.Colors.FirstOrDefault(c => c.Name.Trim().Equals(input.Color.Trim(), StringComparison.OrdinalIgnoreCase));
                            }

                            if (cMatch != null)
                            {
                                item.ColorId = cMatch.ColorId;
                                item.Color = cMatch.Name;
                                if (!string.IsNullOrWhiteSpace(cMatch.ImageUrl)) item.ImageUrl = cMatch.ImageUrl;
                                item.AvailableStock = cMatch.CurrentStock;
                            }
                        }
                    }
                }

                item.Subtotal = item.Quantity * item.UnitPrice;
                order.Items.Add(item);
            }

            if (!order.Items.Any())
            {
                TempData["ToastMessage"] = "Please enter an order quantity (> 0) for at least one product before reviewing your order.";
                TempData["ToastType"] = "warning";
                return RedirectToAction(nameof(Create), new { selectedSupplierId = supplierId });
            }

            order.TotalQuantity = order.Items.Sum(i => i.Quantity);
            order.Subtotal = order.Items.Sum(i => i.Subtotal);
            order.GrandTotal = order.Subtotal;

            ViewBag.Supplier = supplier;
            return View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitOrder(SupplierOrder order)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.SupplierId) || order.Items == null || !order.Items.Any())
            {
                TempData["ToastMessage"] = "Order submission failed. Empty purchase order.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(Create));
            }

            var supplier = await _supplierService.GetSupplierByIdAsync(order.SupplierId);
            if (supplier == null)
            {
                TempData["ToastMessage"] = "Order submission failed: Selected supplier does not exist.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(Create));
            }

            // Strict Server-Side Supplier Verification for each product item
            foreach (var item in order.Items)
            {
                var p = await _productRepository.GetByIdAsync(item.ProductId);
                if (p == null || p.SupplierId != order.SupplierId)
                {
                    TempData["ToastMessage"] = $"Order submission failed: Product '{item.ProductName ?? item.ProductId}' does not belong to supplier '{supplier.CompanyName}'.";
                    TempData["ToastType"] = "danger";
                    return RedirectToAction(nameof(Create), new { selectedSupplierId = order.SupplierId });
                }
            }

            var executedBy = User.Identity?.Name ?? "Admin";
            var (success, message, createdOrder) = await _supplierOrderService.CreateOrderAsync(order, executedBy);

            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";

            if (success && createdOrder != null)
            {
                return RedirectToAction(nameof(Details), new { id = createdOrder.Id });
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Details(string id)
        {
            var order = await _supplierOrderService.GetOrderByIdAsync(id);
            if (order == null) return NotFound();

            var supplier = await _supplierService.GetSupplierByIdAsync(order.SupplierId);
            ViewBag.Supplier = supplier;

            return View(order);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(string orderId, string newStatus, string? notes)
        {
            var executedBy = User.Identity?.Name ?? "Admin";
            var (success, message) = await _supplierOrderService.UpdateOrderStatusAsync(orderId, newStatus, executedBy, notes);

            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";
            return RedirectToAction(nameof(Details), new { id = orderId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string id)
        {
            var executedBy = User.Identity?.Name ?? "Admin";
            var (success, message) = await _supplierOrderService.DeleteOrderAsync(id, executedBy);
            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete([FromForm] List<string> orderIds)
        {
            var executedBy = User.Identity?.Name ?? "Admin";
            var (success, message, count) = await _supplierOrderService.BulkDeleteOrdersAsync(orderIds, executedBy);
            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";
            return RedirectToAction(nameof(Index));
        }
    }

    public class BrandSummaryViewModel
    {
        public string Brand { get; set; } = string.Empty;
        public int ProductCount { get; set; }
        public List<string> SampleModels { get; set; } = new List<string>();
    }

    public class OrderItemFormInput
    {
        public string ProductId { get; set; } = string.Empty;
        public string VariantId { get; set; } = string.Empty;
        public string ColorId { get; set; } = string.Empty;
        public string Variant { get; set; } = string.Empty;
        public string Color { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}
