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
        public async Task<IActionResult> Create(string? selectedSupplierId, string? categoryId, string? brand, string? search)
        {
            var suppliers = await _supplierService.GetAllSuppliersAsync();
            var categories = await _categoryRepository.GetAllAsync();
            var allProducts = await _productRepository.GetAllAsync();

            var filteredProducts = allProducts.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(selectedSupplierId))
            {
                filteredProducts = filteredProducts.Where(p => p.SupplierId == selectedSupplierId);
            }
            else
            {
                // Only show products linked to a supplier vendor account
                filteredProducts = filteredProducts.Where(p => !string.IsNullOrWhiteSpace(p.SupplierId));
            }

            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                filteredProducts = filteredProducts.Where(p => p.CategoryId == categoryId);
            }

            if (!string.IsNullOrWhiteSpace(brand))
            {
                filteredProducts = filteredProducts.Where(p => p.Brand.Equals(brand, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                filteredProducts = filteredProducts.Where(p =>
                    p.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    p.Brand.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    p.ModelName.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                    p.Code.Contains(s, StringComparison.OrdinalIgnoreCase));
            }

            ViewBag.Suppliers = suppliers;
            ViewBag.Categories = categories;
            ViewBag.SelectedSupplierId = selectedSupplierId;
            ViewBag.CategoryId = categoryId;
            ViewBag.Brand = brand;
            ViewBag.Search = search;

            return View(filteredProducts.OrderByDescending(p => p.CreatedDate).ToList());
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

            // Auto-detect supplier ID if top filter dropdown was not selected
            if (string.IsNullOrWhiteSpace(supplierId))
            {
                var firstProd = await _productRepository.GetByIdAsync(validInputs.First().ProductId);
                if (firstProd != null && !string.IsNullOrWhiteSpace(firstProd.SupplierId))
                {
                    supplierId = firstProd.SupplierId;
                }
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
                SupplierName = supplier.CompanyName,
                SupplierEmail = supplier.Email,
                SupplierPhone = supplier.Phone,
                Notes = notes ?? string.Empty,
                ExpectedDeliveryDate = expectedDeliveryDate,
                Items = new List<SupplierOrderItem>()
            };

            foreach (var input in validInputs)
            {
                var p = await _productRepository.GetByIdAsync(input.ProductId);
                if (p != null)
                {
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
                        Variant = p.Variant,
                        Color = p.Color,
                        Ram = p.Ram,
                        Storage = p.Storage,
                        ImageUrl = p.ImageUrl,
                        Quantity = input.Quantity,
                        AvailableStock = p.CurrentStock,
                        UnitPrice = input.UnitPrice > 0 ? input.UnitPrice : (p.SupplierPrice > 0 ? p.SupplierPrice : p.PurchasePrice),
                    };
                    item.Subtotal = item.Quantity * item.UnitPrice;
                    order.Items.Add(item);
                }
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

    public class OrderItemFormInput
    {
        public string ProductId { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }
}
