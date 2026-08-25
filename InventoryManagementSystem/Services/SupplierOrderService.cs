using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;

namespace InventoryManagementSystem.Services
{
    public class SupplierOrderService : ISupplierOrderService
    {
        private readonly ISupplierOrderRepository _orderRepository;
        private readonly ISupplierRepository _supplierRepository;
        private readonly IProductRepository _productRepository;
        private readonly IStockTransactionRepository _stockTxRepository;
        private readonly IBrevoEmailService _emailService;
        private readonly IAuditLogService _auditLogService;

        public SupplierOrderService(
            ISupplierOrderRepository orderRepository,
            ISupplierRepository supplierRepository,
            IProductRepository productRepository,
            IStockTransactionRepository stockTxRepository,
            IBrevoEmailService emailService,
            IAuditLogService auditLogService)
        {
            _orderRepository = orderRepository;
            _supplierRepository = supplierRepository;
            _productRepository = productRepository;
            _stockTxRepository = stockTxRepository;
            _emailService = emailService;
            _auditLogService = auditLogService;
        }

        public async Task<SupplierOrder?> GetOrderByIdAsync(string id)
        {
            return await _orderRepository.GetByIdAsync(id);
        }

        public async Task<SupplierOrder?> GetOrderByNumberAsync(string orderNumber)
        {
            return await _orderRepository.GetByOrderNumberAsync(orderNumber);
        }

        public async Task<IEnumerable<SupplierOrder>> GetPagedOrdersAsync(
            string? search, string? supplierId, string? status, int page, int pageSize,
            DateTime? fromDate = null, DateTime? toDate = null, decimal? minAmount = null, decimal? maxAmount = null, string? sortBy = null)
        {
            await SyncDeliveredOrdersToShopCatalogAsync();
            return await _orderRepository.GetPagedOrdersAsync(search, supplierId, status, page, pageSize, fromDate, toDate, minAmount, maxAmount, sortBy);
        }

        public async Task SyncDeliveredOrdersToShopCatalogAsync()
        {
            try
            {
                var deliveredOrders = await _orderRepository.GetPagedOrdersAsync(null, null, SupplierOrderStatus.Delivered, 1, 1000);
                var completedOrders = await _orderRepository.GetPagedOrdersAsync(null, null, SupplierOrderStatus.Completed, 1, 1000);
                var allDelivered = deliveredOrders.Concat(completedOrders).GroupBy(o => o.Id).Select(g => g.First()).ToList();

                if (!allDelivered.Any()) return;

                var allProducts = await _productRepository.GetAllAsync();
                var shopProducts = allProducts.Where(p => string.IsNullOrWhiteSpace(p.SupplierId)).ToList();

                foreach (var order in allDelivered)
                {
                    foreach (var item in order.Items)
                    {
                        var supplierProduct = allProducts.FirstOrDefault(p => p.Id == item.ProductId);

                        var shopProduct = shopProducts.FirstOrDefault(p =>
                            (!string.IsNullOrEmpty(supplierProduct?.Code) && p.Code.Equals(supplierProduct.Code, StringComparison.OrdinalIgnoreCase)) ||
                            (p.Brand.Equals(item.Brand, StringComparison.OrdinalIgnoreCase) &&
                             p.ModelName.Equals(item.Model, StringComparison.OrdinalIgnoreCase) &&
                             (string.IsNullOrEmpty(item.Variant) || p.Variant.Equals(item.Variant, StringComparison.OrdinalIgnoreCase)) &&
                             (string.IsNullOrEmpty(item.Color) || p.Color.Equals(item.Color, StringComparison.OrdinalIgnoreCase)))
                        );

                        if (shopProduct == null)
                        {
                            var newShopProduct = new Product
                            {
                                Name = item.ProductName,
                                Code = (supplierProduct != null && !string.IsNullOrWhiteSpace(supplierProduct.Code))
                                    ? supplierProduct.Code
                                    : $"PROD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}",
                                Barcode = supplierProduct?.Barcode ?? string.Empty,
                                CategoryId = supplierProduct?.CategoryId,
                                ProductType = supplierProduct?.ProductType ?? "Smartphone",
                                Brand = item.Brand,
                                ModelName = item.Model,
                                Variant = item.Variant,
                                Color = item.Color,
                                Ram = item.Ram,
                                Storage = item.Storage,
                                SupplierPrice = item.UnitPrice,
                                PurchasePrice = item.UnitPrice,
                                SellingPrice = (supplierProduct != null && supplierProduct.SellingPrice > 0)
                                    ? supplierProduct.SellingPrice
                                    : item.UnitPrice * 1.15m,
                                CurrentStock = item.Quantity,
                                MinimumStock = supplierProduct?.MinimumStock ?? 5,
                                SupplierId = null, // Store/Shop Catalog Product (Admin Dashboard)
                                SupplierName = order.SupplierName,
                                ImageUrl = item.ImageUrl,
                                ImageUrls = supplierProduct?.ImageUrls ?? new List<string>(),
                                Specs = supplierProduct?.Specs ?? new MobileSpecifications(),
                                CreatedDate = DateTime.UtcNow,
                                UpdatedDate = DateTime.UtcNow
                            };

                            await _productRepository.CreateAsync(newShopProduct);
                            shopProducts.Add(newShopProduct);

                            await _stockTxRepository.CreateAsync(new StockTransaction
                            {
                                ProductId = newShopProduct.Id,
                                ProductName = newShopProduct.Name,
                                ProductCode = newShopProduct.Code,
                                ExecutedBy = "System Auto-Sync",
                                Username = "System Auto-Sync",
                                Quantity = item.Quantity,
                                Type = "Stock In",
                                Reason = $"Supplier Purchase Order Delivery Sync (#{order.OrderNumber})",
                                PreviousStock = 0,
                                CurrentStock = item.Quantity,
                                Source = "Supplier Purchase Order",
                                UnitCost = item.UnitPrice,
                                Brand = newShopProduct.Brand,
                                ModelName = newShopProduct.ModelName,
                                Variant = newShopProduct.Variant,
                                Color = newShopProduct.Color,
                                Timestamp = DateTime.UtcNow
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ORDER_SERVICE] SyncDeliveredOrdersToShopCatalogAsync failed: {ex.Message}");
            }
        }

        public async Task<long> GetFilteredCountAsync(
            string? search, string? supplierId, string? status,
            DateTime? fromDate = null, DateTime? toDate = null, decimal? minAmount = null, decimal? maxAmount = null)
        {
            return await _orderRepository.GetFilteredCountAsync(search, supplierId, status, fromDate, toDate, minAmount, maxAmount);
        }

        public async Task<IEnumerable<SupplierOrder>> GetSupplierOrdersAsync(string supplierId, string? status, int limit = 50)
        {
            return await _orderRepository.GetSupplierOrdersAsync(supplierId, status, limit);
        }

        public async Task<Dictionary<string, long>> GetOrderStatusCountsAsync(string? supplierId = null)
        {
            return await _orderRepository.GetOrderStatusCountsAsync(supplierId);
        }

        public async Task<(bool Success, string Message, SupplierOrder? Order)> CreateOrderAsync(SupplierOrder order, string executedBy)
        {
            if (order == null) return (false, "Order data is null.", null);
            if (string.IsNullOrWhiteSpace(order.SupplierId)) return (false, "Supplier selection is required.", null);
            if (order.Items == null || !order.Items.Any()) return (false, "Purchase Order must contain at least one product item.", null);

            var supplier = await _supplierRepository.GetByIdAsync(order.SupplierId);
            if (supplier == null) return (false, "Selected supplier vendor record was not found.", null);

            order.SupplierName = supplier.CompanyName;
            order.SupplierEmail = supplier.Email;
            order.SupplierPhone = supplier.Phone;
            order.CreatedBy = executedBy;
            order.CreatedAt = DateTime.UtcNow;
            order.UpdatedAt = DateTime.UtcNow;
            order.Status = SupplierOrderStatus.Pending;

            // Generate unique PO-YYYYMMDD-XXXX number
            order.OrderNumber = await _orderRepository.GetNextOrderNumberAsync();

            // Populate & snapshot item details and validate stock limit
            int totalQty = 0;
            decimal subtotal = 0;

            foreach (var item in order.Items)
            {
                if (item.Quantity <= 0) item.Quantity = 1;
                var product = await _productRepository.GetByIdAsync(item.ProductId);
                if (product != null)
                {
                    if (product.CurrentStock > 0 && item.Quantity > product.CurrentStock)
                    {
                        return (false, $"Cannot order {item.Quantity} units of '{product.Name}'. Supplier available stock is only {product.CurrentStock} units.", null);
                    }

                    item.ProductName = product.Name;
                    item.Brand = product.Brand;
                    item.Model = product.ModelName;
                    item.Variant = product.Variant;
                    item.Color = product.Color;
                    item.Ram = product.Ram;
                    item.Storage = product.Storage;
                    item.ImageUrl = product.ImageUrl;
                    if (item.UnitPrice <= 0)
                    {
                        item.UnitPrice = product.SupplierPrice > 0 ? product.SupplierPrice : product.PurchasePrice;
                    }
                }

                item.Subtotal = item.Quantity * item.UnitPrice;
                totalQty += item.Quantity;
                subtotal += item.Subtotal;
            }

            order.TotalQuantity = totalQty;
            order.Subtotal = subtotal;
            order.GrandTotal = subtotal + order.Tax - order.Discount;

            // Save to database
            await _orderRepository.CreateAsync(order);

            // Audit log
            await _auditLogService.LogActivityAsync(
                "SUPPLIER_ORDER_CREATED",
                executedBy,
                order.OrderNumber,
                $"Created Purchase Order #{order.OrderNumber} for supplier '{supplier.CompanyName}' with {totalQty} items totaling ₹{order.GrandTotal:N2}.");

            // Brevo Email Notification
            if (!string.IsNullOrWhiteSpace(supplier.Email))
            {
                try
                {
                    var emailSubject = $"New Purchase Order #{order.OrderNumber} from SIMS Mobile Shop";
                    var htmlBuilder = new StringBuilder();
                    htmlBuilder.Append($"<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto;background:#0F172A;color:#F8FAFC;padding:24px;border-radius:12px;'>");
                    htmlBuilder.Append($"<h2 style='color:#38BDF8;margin-top:0;'>New Purchase Order Received</h2>");
                    htmlBuilder.Append($"<p>Dear <strong>{supplier.ContactPerson}</strong> ({supplier.CompanyName}),</p>");
                    htmlBuilder.Append($"<p>A new purchase order <strong>#{order.OrderNumber}</strong> has been issued by SIMS Mobile Shop.</p>");
                    htmlBuilder.Append($"<div style='background:#1E293B;padding:16px;border-radius:8px;margin:16px 0;'>");
                    htmlBuilder.Append($"<p style='margin:4px 0;'><strong>Order Number:</strong> {order.OrderNumber}</p>");
                    htmlBuilder.Append($"<p style='margin:4px 0;'><strong>Date:</strong> {order.CreatedAt:dd-MMM-yyyy HH:mm} IST</p>");
                    htmlBuilder.Append($"<p style='margin:4px 0;'><strong>Total Items:</strong> {order.TotalQuantity} units</p>");
                    htmlBuilder.Append($"<p style='margin:4px 0;'><strong>Grand Total:</strong> ₹{order.GrandTotal:N2}</p>");
                    htmlBuilder.Append($"</div>");
                    htmlBuilder.Append($"<h3>Order Summary:</h3>");
                    htmlBuilder.Append($"<table style='width:100%;border-collapse:collapse;color:#F8FAFC;'>");
                    htmlBuilder.Append($"<thead><tr style='background:#334155;text-align:left;'><th style='padding:8px;'>Product</th><th style='padding:8px;'>Qty</th><th style='padding:8px;'>Unit Price</th><th style='padding:8px;'>Total</th></tr></thead><tbody>");

                    foreach (var it in order.Items)
                    {
                        htmlBuilder.Append($"<tr style='border-bottom:1px solid #334155;'>");
                        htmlBuilder.Append($"<td style='padding:8px;'>{it.ProductName} ({it.Variant} {it.Color})</td>");
                        htmlBuilder.Append($"<td style='padding:8px;'>{it.Quantity}</td>");
                        htmlBuilder.Append($"<td style='padding:8px;'>₹{it.UnitPrice:N2}</td>");
                        htmlBuilder.Append($"<td style='padding:8px;'>₹{it.Subtotal:N2}</td>");
                        htmlBuilder.Append($"</tr>");
                    }
                    htmlBuilder.Append($"</tbody></table>");
                    htmlBuilder.Append($"<p style='margin-top:20px;'>Please log into your <strong>SIMS Supplier Portal</strong> to view, accept, and update status for this order.</p>");
                    htmlBuilder.Append($"<div style='margin-top:24px;font-size:12px;color:#94A3B8;'>SIMS Smart Inventory Management System</div>");
                    htmlBuilder.Append($"</div>");

                    var (emailSuccess, messageId, apiResp, errorMsg) = await _emailService.SendTransactionalEmailAsync(supplier.Email, emailSubject, htmlBuilder.ToString());
                    
                    order.EmailSent = emailSuccess;
                    order.EmailSentAt = emailSuccess ? DateTime.UtcNow : null;
                    order.EmailError = errorMsg;
                    await _orderRepository.UpdateAsync(order.Id, order);

                    await _auditLogService.LogActivityAsync(
                        emailSuccess ? "SUPPLIER_ORDER_EMAIL_SENT" : "SUPPLIER_ORDER_EMAIL_FAILED",
                        executedBy,
                        order.OrderNumber,
                        emailSuccess ? $"Email sent to {supplier.Email} (MessageID: {messageId})" : $"Email to {supplier.Email} failed: {errorMsg}");
                }
                catch (Exception ex)
                {
                    order.EmailSent = false;
                    order.EmailError = ex.Message;
                    await _orderRepository.UpdateAsync(order.Id, order);
                }
            }

            return (true, $"Purchase Order #{order.OrderNumber} created successfully!", order);
        }

        public async Task<(bool Success, string Message)> UpdateOrderStatusAsync(string orderId, string newStatus, string updatedBy, string? supplierNotes = null, DateTime? expectedDeliveryDate = null)
        {
            if (string.IsNullOrWhiteSpace(orderId)) return (false, "Order ID is required.");
            if (!SupplierOrderStatus.AllStatuses.Contains(newStatus)) return (false, $"Invalid order status '{newStatus}'.");

            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null) return (false, "Purchase order record not found.");

            var oldStatus = order.Status;

            // 1. Deduct Supplier's Warehouse Stock when order is accepted/shipped/delivered for the first time
            bool isAcceptedOrFulfilled = newStatus == SupplierOrderStatus.Accepted ||
                                         newStatus == SupplierOrderStatus.Processing ||
                                         newStatus == SupplierOrderStatus.Shipped ||
                                         newStatus == SupplierOrderStatus.Delivered ||
                                         newStatus == SupplierOrderStatus.Completed;

            if (isAcceptedOrFulfilled && !order.SupplierStockDeducted)
            {
                foreach (var item in order.Items)
                {
                    var supplierProduct = await _productRepository.GetByIdAsync(item.ProductId);
                    if (supplierProduct != null)
                    {
                        supplierProduct.CurrentStock = System.Math.Max(0, supplierProduct.CurrentStock - item.Quantity);
                        supplierProduct.UpdatedDate = DateTime.UtcNow;
                        await _productRepository.UpdateAsync(supplierProduct.Id, supplierProduct);
                    }
                }
                order.SupplierStockDeducted = true;
            }
            else if ((newStatus == SupplierOrderStatus.Cancelled || newStatus == SupplierOrderStatus.Rejected) && order.SupplierStockDeducted)
            {
                // Revert supplier stock if order was cancelled/rejected after acceptance
                foreach (var item in order.Items)
                {
                    var supplierProduct = await _productRepository.GetByIdAsync(item.ProductId);
                    if (supplierProduct != null)
                    {
                        supplierProduct.CurrentStock += item.Quantity;
                        supplierProduct.UpdatedDate = DateTime.UtcNow;
                        await _productRepository.UpdateAsync(supplierProduct.Id, supplierProduct);
                    }
                }
                order.SupplierStockDeducted = false;
            }

            // 2. Automatically increment shop product stock in Admin Dashboard when order becomes Delivered or Completed
            bool isTransitioningToDelivered = (newStatus == SupplierOrderStatus.Delivered || newStatus == SupplierOrderStatus.Completed)
                && oldStatus != SupplierOrderStatus.Delivered && oldStatus != SupplierOrderStatus.Completed;

            if (isTransitioningToDelivered)
            {
                var allProducts = await _productRepository.GetAllAsync();
                var shopProducts = allProducts.Where(p => string.IsNullOrWhiteSpace(p.SupplierId)).ToList();

                foreach (var item in order.Items)
                {
                    // 1. Fetch original Supplier Product (if linked)
                    var supplierProduct = await _productRepository.GetByIdAsync(item.ProductId);

                    // 2. Find matching Shop Product (where SupplierId is null/empty) in Admin Store Catalog
                    var shopProduct = shopProducts.FirstOrDefault(p =>
                        (!string.IsNullOrEmpty(supplierProduct?.Code) && p.Code.Equals(supplierProduct.Code, StringComparison.OrdinalIgnoreCase)) ||
                        (p.Brand.Equals(item.Brand, StringComparison.OrdinalIgnoreCase) &&
                         p.ModelName.Equals(item.Model, StringComparison.OrdinalIgnoreCase) &&
                         (string.IsNullOrEmpty(item.Variant) || p.Variant.Equals(item.Variant, StringComparison.OrdinalIgnoreCase)) &&
                         (string.IsNullOrEmpty(item.Color) || p.Color.Equals(item.Color, StringComparison.OrdinalIgnoreCase)))
                    );

                    if (shopProduct != null)
                    {
                        int prevStock = shopProduct.CurrentStock;
                        shopProduct.CurrentStock += item.Quantity;
                        shopProduct.PurchasePrice = item.UnitPrice;
                        if (shopProduct.SellingPrice <= 0)
                        {
                            shopProduct.SellingPrice = (supplierProduct != null && supplierProduct.SellingPrice > 0)
                                ? supplierProduct.SellingPrice
                                : item.UnitPrice * 1.15m;
                        }
                        shopProduct.UpdatedDate = DateTime.UtcNow;
                        await _productRepository.UpdateAsync(shopProduct.Id, shopProduct);

                        await _stockTxRepository.CreateAsync(new StockTransaction
                        {
                            ProductId = shopProduct.Id,
                            ProductName = shopProduct.Name,
                            ProductCode = shopProduct.Code,
                            ExecutedBy = updatedBy,
                            Username = updatedBy,
                            Quantity = item.Quantity,
                            Type = "Stock In",
                            Reason = $"Supplier Purchase Order Delivery (#{order.OrderNumber})",
                            PreviousStock = prevStock,
                            CurrentStock = shopProduct.CurrentStock,
                            Source = "Supplier Purchase Order",
                            UnitCost = item.UnitPrice,
                            Brand = shopProduct.Brand,
                            ModelName = shopProduct.ModelName,
                            Variant = shopProduct.Variant,
                            Color = shopProduct.Color,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        // 3. Auto-create new Shop Product in Admin Product Catalog (SupplierId = null so it appears in Admin Products page)
                        var newShopProduct = new Product
                        {
                            Name = item.ProductName,
                            Code = (supplierProduct != null && !string.IsNullOrWhiteSpace(supplierProduct.Code))
                                ? supplierProduct.Code
                                : $"PROD-{DateTime.UtcNow:yyyyMMddHHmmss}-{Random.Shared.Next(100, 999)}",
                            Barcode = supplierProduct?.Barcode ?? string.Empty,
                            CategoryId = supplierProduct?.CategoryId,
                            ProductType = supplierProduct?.ProductType ?? "Smartphone",
                            Brand = item.Brand,
                            ModelName = item.Model,
                            Variant = item.Variant,
                            Color = item.Color,
                            Ram = item.Ram,
                            Storage = item.Storage,
                            SupplierPrice = item.UnitPrice,
                            PurchasePrice = item.UnitPrice,
                            SellingPrice = (supplierProduct != null && supplierProduct.SellingPrice > 0)
                                ? supplierProduct.SellingPrice
                                : item.UnitPrice * 1.15m,
                            CurrentStock = item.Quantity,
                            MinimumStock = supplierProduct?.MinimumStock ?? 5,
                            SupplierId = null, // IMPORTANT: Must be null so it appears in Admin Product Dashboard!
                            SupplierName = order.SupplierName,
                            ImageUrl = item.ImageUrl,
                            ImageUrls = supplierProduct?.ImageUrls ?? new List<string>(),
                            Specs = supplierProduct?.Specs ?? new MobileSpecifications(),
                            CreatedDate = DateTime.UtcNow,
                            UpdatedDate = DateTime.UtcNow
                        };

                        await _productRepository.CreateAsync(newShopProduct);
                        shopProducts.Add(newShopProduct);

                        await _stockTxRepository.CreateAsync(new StockTransaction
                        {
                            ProductId = newShopProduct.Id,
                            ProductName = newShopProduct.Name,
                            ProductCode = newShopProduct.Code,
                            ExecutedBy = updatedBy,
                            Username = updatedBy,
                            Quantity = item.Quantity,
                            Type = "Stock In",
                            Reason = $"Supplier Purchase Order Delivery (#{order.OrderNumber})",
                            PreviousStock = 0,
                            CurrentStock = item.Quantity,
                            Source = "Supplier Purchase Order",
                            UnitCost = item.UnitPrice,
                            Brand = newShopProduct.Brand,
                            ModelName = newShopProduct.ModelName,
                            Variant = newShopProduct.Variant,
                            Color = newShopProduct.Color,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }
            }

            order.Status = newStatus;
            order.UpdatedAt = DateTime.UtcNow;
            
            if (supplierNotes != null)
            {
                order.SupplierNotes = supplierNotes;
            }
            if (expectedDeliveryDate.HasValue)
            {
                order.ExpectedDeliveryDate = expectedDeliveryDate.Value;
            }

            await _orderRepository.UpdateAsync(order.Id, order);

            await _auditLogService.LogActivityAsync(
                $"SUPPLIER_ORDER_{newStatus.ToUpper()}",
                updatedBy,
                order.OrderNumber,
                $"Updated PO #{order.OrderNumber} status from '{oldStatus}' to '{newStatus}'. Notes: {supplierNotes ?? "-"}");

            return (true, $"Order #{order.OrderNumber} status updated to '{newStatus}'" + (isTransitioningToDelivered ? " and inventory stock has been increased." : "."));
        }

        public async Task<(bool Success, string Message)> DeleteOrderAsync(string orderId, string executedBy, string? supplierIdFilter = null)
        {
            if (string.IsNullOrWhiteSpace(orderId)) return (false, "Order ID is required.");

            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null) return (false, "Purchase order record not found.");

            if (!string.IsNullOrWhiteSpace(supplierIdFilter) && order.SupplierId != supplierIdFilter)
            {
                return (false, "Access denied. You can only delete purchase orders issued to your business.");
            }

            var allowedStatuses = new[] { SupplierOrderStatus.Delivered, SupplierOrderStatus.Completed, SupplierOrderStatus.Rejected, SupplierOrderStatus.Cancelled };
            if (!allowedStatuses.Contains(order.Status))
            {
                return (false, $"Purchase order #{order.OrderNumber} cannot be deleted while in '{order.Status}' status. Orders can only be deleted from history after being Delivered, Completed, Rejected, or Cancelled.");
            }

            await _orderRepository.DeleteAsync(order.Id);

            await _auditLogService.LogActivityAsync(
                "SUPPLIER_ORDER_DELETED",
                executedBy,
                order.OrderNumber,
                $"Deleted purchase order history entry #{order.OrderNumber} (Status: {order.Status}, Total: ₹{order.GrandTotal:N2}). Product stock remained unchanged.");

            return (true, $"Purchase order #{order.OrderNumber} history entry deleted successfully!");
        }

        public async Task<(bool Success, string Message, int DeletedCount)> BulkDeleteOrdersAsync(List<string> orderIds, string executedBy, string? supplierIdFilter = null)
        {
            if (orderIds == null || !orderIds.Any()) return (false, "No purchase orders selected for deletion.", 0);

            int deletedCount = 0;
            var errors = new List<string>();
            var allowedStatuses = new[] { SupplierOrderStatus.Delivered, SupplierOrderStatus.Completed, SupplierOrderStatus.Rejected, SupplierOrderStatus.Cancelled };

            foreach (var id in orderIds.Distinct())
            {
                var order = await _orderRepository.GetByIdAsync(id);
                if (order == null) continue;

                if (!string.IsNullOrWhiteSpace(supplierIdFilter) && order.SupplierId != supplierIdFilter)
                {
                    errors.Add($"Order #{order.OrderNumber}: Access denied.");
                    continue;
                }

                if (!allowedStatuses.Contains(order.Status))
                {
                    errors.Add($"Order #{order.OrderNumber}: Cannot delete order in '{order.Status}' status.");
                    continue;
                }

                await _orderRepository.DeleteAsync(order.Id);
                deletedCount++;

                await _auditLogService.LogActivityAsync(
                    "SUPPLIER_ORDER_BULK_DELETED",
                    executedBy,
                    order.OrderNumber,
                    $"Bulk deleted purchase order history entry #{order.OrderNumber} (Status: {order.Status}). Product stock remained unchanged.");
            }

            if (deletedCount == 0 && errors.Any())
            {
                return (false, string.Join(" ", errors), 0);
            }

            string msg = $"{deletedCount} purchase order(s) deleted from history successfully.";
            if (errors.Any()) msg += $" ({errors.Count} order(s) skipped: in-flight active orders cannot be deleted).";

            return (true, msg, deletedCount);
        }
    }
}
