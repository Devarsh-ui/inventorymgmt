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
    public class PurchaseReturnController : Controller
    {
        private readonly ISupplierPurchaseReturnService _purchaseReturnService;
        private readonly ISupplierService _supplierService;
        private readonly ISupplierOrderService _supplierOrderService;
        private readonly IProductRepository _productRepository;

        public PurchaseReturnController(
            ISupplierPurchaseReturnService purchaseReturnService,
            ISupplierService supplierService,
            ISupplierOrderService supplierOrderService,
            IProductRepository productRepository)
        {
            _purchaseReturnService = purchaseReturnService;
            _supplierService = supplierService;
            _supplierOrderService = supplierOrderService;
            _productRepository = productRepository;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? search, string? supplierId, string? status, string? reason, int page = 1)
        {
            int pageSize = 15;
            var returns = await _purchaseReturnService.GetPagedReturnsAsync(search, supplierId, status, reason, page, pageSize);
            var totalCount = await _purchaseReturnService.GetFilteredCountAsync(search, supplierId, status, reason);
            var statusCounts = await _purchaseReturnService.GetReturnStatusCountsAsync();
            var suppliers = await _supplierService.GetAllSuppliersAsync();

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
                StatusCounts = statusCounts,
                Suppliers = suppliers
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> Create(string? supplierId = null, string? orderId = null)
        {
            var suppliers = await _supplierService.GetAllSuppliersAsync();
            var eligibleOrders = await _purchaseReturnService.GetEligibleOrdersForReturnAsync(supplierId);

            SupplierOrder? selectedOrder = null;
            IEnumerable<Device> availableDevices = new List<Device>();
            Dictionary<string, int> previouslyReturned = new Dictionary<string, int>();

            if (!string.IsNullOrWhiteSpace(orderId))
            {
                selectedOrder = await _supplierOrderService.GetOrderByIdAsync(orderId);
                if (selectedOrder != null)
                {
                    supplierId = selectedOrder.SupplierId;
                    availableDevices = await _purchaseReturnService.GetEligibleDevicesForOrderAsync(orderId);
                    previouslyReturned = await _purchaseReturnService.GetReturnedQuantitiesForOrderAsync(orderId);
                }
            }

            var viewModel = new PurchaseReturnCreateViewModel
            {
                SupplierId = supplierId ?? string.Empty,
                PurchaseOrderId = orderId ?? string.Empty,
                Suppliers = suppliers,
                EligibleOrders = eligibleOrders,
                SelectedOrder = selectedOrder,
                AvailableDevices = availableDevices,
                PreviouslyReturnedQuantities = previouslyReturned
            };

            return View(viewModel);
        }

        [HttpGet]
        public async Task<IActionResult> GetEligibleOrders(string supplierId)
        {
            var orders = await _purchaseReturnService.GetEligibleOrdersForReturnAsync(supplierId);
            var result = orders.Select(o => new
            {
                id = o.Id,
                orderNumber = o.OrderNumber,
                createdAt = o.CreatedAt.ToString("dd-MMM-yyyy"),
                status = o.Status,
                totalAmount = o.GrandTotal,
                productCount = o.Items.Count
            });
            return Json(result);
        }

        [HttpGet]
        public async Task<IActionResult> GetOrderReturnDetails(string orderId)
        {
            var order = await _supplierOrderService.GetOrderByIdAsync(orderId);
            if (order == null) return NotFound();

            var availableDevices = await _purchaseReturnService.GetEligibleDevicesForOrderAsync(orderId);
            var previouslyReturned = await _purchaseReturnService.GetReturnedQuantitiesForOrderAsync(orderId);

            var items = order.Items.Select(item => {
                int prevRet = previouslyReturned.ContainsKey(item.ProductId) ? previouslyReturned[item.ProductId] : 0;
                int remRet = item.Quantity > prevRet ? item.Quantity - prevRet : 0;
                return new
                {
                    productId = item.ProductId,
                    productName = item.ProductName,
                    brand = item.Brand,
                    model = item.Model,
                    variant = item.Variant,
                    color = item.Color,
                    ram = item.Ram,
                    storage = item.Storage,
                    unitPrice = item.UnitPrice,
                    orderedQuantity = item.Quantity,
                    previouslyReturned = prevRet,
                    remainingReturnable = remRet
                };
            });

            var devices = availableDevices.Select(d => new
            {
                id = d.Id,
                productId = d.ProductId,
                imei1 = d.IMEI1,
                imei2 = d.IMEI2 ?? "",
                serialNumber = d.SerialNumber ?? "",
                brand = d.Brand,
                model = d.ModelName,
                variant = d.Variant,
                color = d.Color,
                purchasePrice = d.PurchasePrice,
                status = d.Status
            });

            return Json(new { items, devices });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SubmitCreate(PurchaseReturnCreateViewModel model, List<string> selectedDeviceIds, Dictionary<string, int> returnQuantities)
        {
            if (string.IsNullOrWhiteSpace(model.SupplierId))
            {
                TempData["ToastMessage"] = "Please select a valid supplier.";
                TempData["ToastType"] = "danger";
                return RedirectToAction(nameof(Create), new { supplierId = model.SupplierId, orderId = model.PurchaseOrderId });
            }

            var returnRecord = new SupplierPurchaseReturn
            {
                SupplierId = model.SupplierId,
                PurchaseOrderId = model.PurchaseOrderId,
                Reason = model.Reason,
                AdditionalRemarks = model.AdditionalRemarks ?? "",
                ResolutionType = model.ResolutionType
            };

            var itemsList = new List<SupplierPurchaseReturnItem>();

            if (!string.IsNullOrWhiteSpace(model.PurchaseOrderId))
            {
                var order = await _supplierOrderService.GetOrderByIdAsync(model.PurchaseOrderId);
                if (order != null)
                {
                    foreach (var orderItem in order.Items)
                    {
                        int qtyToReturn = 0;

                        // Check if device IDs selected for this product
                        var matchedDeviceIds = new List<string>();
                        if (selectedDeviceIds != null && selectedDeviceIds.Any())
                        {
                            var eligibleDevices = await _purchaseReturnService.GetEligibleDevicesForOrderAsync(order.Id, orderItem.ProductId);
                            matchedDeviceIds = eligibleDevices.Where(d => selectedDeviceIds.Contains(d.Id)).Select(d => d.Id).ToList();
                            if (matchedDeviceIds.Any())
                            {
                                qtyToReturn = matchedDeviceIds.Count;
                            }
                        }

                        // Check if manual quantity entered in returnQuantities dictionary
                        if (qtyToReturn == 0 && returnQuantities != null && returnQuantities.TryGetValue(orderItem.ProductId, out int manualQty) && manualQty > 0)
                        {
                            qtyToReturn = manualQty;
                        }

                        if (qtyToReturn > 0)
                        {
                            var prod = await _productRepository.GetByIdAsync(orderItem.ProductId);
                            itemsList.Add(new SupplierPurchaseReturnItem
                            {
                                ProductId = orderItem.ProductId,
                                ProductName = orderItem.ProductName,
                                Brand = orderItem.Brand,
                                ModelName = orderItem.Model,
                                Variant = orderItem.Variant,
                                Color = orderItem.Color,
                                Ram = orderItem.Ram,
                                Storage = orderItem.Storage,
                                CategoryName = prod?.ProductType ?? "Accessory",
                                ImageUrl = prod?.ImageUrl ?? "/images/product-placeholder.png",
                                Quantity = qtyToReturn,
                                UnitPurchasePrice = orderItem.UnitPrice,
                                ReturnValue = qtyToReturn * orderItem.UnitPrice,
                                DeviceIds = matchedDeviceIds
                            });
                        }
                    }
                }
            }

            if (!itemsList.Any())
            {
                TempData["ToastMessage"] = "Please select at least one product or device to return.";
                TempData["ToastType"] = "warning";
                return RedirectToAction(nameof(Create), new { supplierId = model.SupplierId, orderId = model.PurchaseOrderId });
            }

            returnRecord.Items = itemsList;

            var executedBy = User.Identity?.Name ?? "Admin";
            var (success, message, createdReturn) = await _purchaseReturnService.CreateReturnAsync(returnRecord, selectedDeviceIds, executedBy);

            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";

            if (success && createdReturn != null)
            {
                return RedirectToAction(nameof(Details), new { id = createdReturn.Id });
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Details(string id)
        {
            var returnRecord = await _purchaseReturnService.GetReturnByIdAsync(id);
            if (returnRecord == null) return NotFound();

            var viewModel = new PurchaseReturnDetailsViewModel
            {
                ReturnRecord = returnRecord
            };

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Ship(string id)
        {
            var executedBy = User.Identity?.Name ?? "Admin";
            var (success, message) = await _purchaseReturnService.ShipReturnAsync(id, executedBy);

            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(string id)
        {
            var executedBy = User.Identity?.Name ?? "Admin";
            var (success, message) = await _purchaseReturnService.CancelReturnAsync(id, executedBy);

            TempData["ToastMessage"] = message;
            TempData["ToastType"] = success ? "success" : "danger";

            return RedirectToAction(nameof(Details), new { id });
        }
    }
}
