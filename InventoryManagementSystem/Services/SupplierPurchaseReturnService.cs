using MongoDB.Bson;
using MongoDB.Driver;
using InventoryManagementSystem.Data;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Services
{
    public class SupplierPurchaseReturnService : ISupplierPurchaseReturnService
    {
        private readonly ISupplierPurchaseReturnRepository _returnRepository;
        private readonly ISupplierOrderRepository _orderRepository;
        private readonly ISupplierRepository _supplierRepository;
        private readonly IProductRepository _productRepository;
        private readonly IDeviceRepository _deviceRepository;
        private readonly IStockTransactionRepository _stockTxRepository;
        private readonly IBrevoEmailService _emailService;
        private readonly IAuditLogService _auditLogService;
        private readonly MongoDbContext _context;
        private readonly ILogger<SupplierPurchaseReturnService> _logger;

        public SupplierPurchaseReturnService(
            ISupplierPurchaseReturnRepository returnRepository,
            ISupplierOrderRepository orderRepository,
            ISupplierRepository supplierRepository,
            IProductRepository productRepository,
            IDeviceRepository deviceRepository,
            IStockTransactionRepository stockTxRepository,
            IBrevoEmailService emailService,
            IAuditLogService auditLogService,
            MongoDbContext context,
            ILogger<SupplierPurchaseReturnService> logger)
        {
            _returnRepository = returnRepository;
            _orderRepository = orderRepository;
            _supplierRepository = supplierRepository;
            _productRepository = productRepository;
            _deviceRepository = deviceRepository;
            _stockTxRepository = stockTxRepository;
            _emailService = emailService;
            _auditLogService = auditLogService;
            _context = context;
            _logger = logger;
        }

        public async Task<SupplierPurchaseReturn?> GetReturnByIdAsync(string id) => await _returnRepository.GetByIdAsync(id);

        public async Task<SupplierPurchaseReturn?> GetReturnByNumberAsync(string returnNumber) => await _returnRepository.GetByReturnNumberAsync(returnNumber);

        public async Task<IEnumerable<SupplierPurchaseReturn>> GetPagedReturnsAsync(string? search, string? supplierId, string? status, string? reason, int page, int pageSize)
        {
            await SyncAcceptedReturnsToShopCatalogAsync();
            return await _returnRepository.GetPagedReturnsAsync(search, supplierId, status, reason, page, pageSize);
        }

        public async Task<long> GetFilteredCountAsync(string? search, string? supplierId, string? status, string? reason)
        {
            return await _returnRepository.GetFilteredCountAsync(search, supplierId, status, reason);
        }

        public async Task<IEnumerable<SupplierPurchaseReturn>> GetSupplierReturnsAsync(string supplierId, string? status, int limit = 50)
        {
            return await _returnRepository.GetSupplierReturnsAsync(supplierId, status, limit);
        }

        public async Task<Dictionary<string, long>> GetReturnStatusCountsAsync(string? supplierId = null)
        {
            return await _returnRepository.GetReturnStatusCountsAsync(supplierId);
        }

        public async Task<IEnumerable<SupplierOrder>> GetEligibleOrdersForReturnAsync(string? supplierId = null)
        {
            // Only orders that are Delivered, Completed, Shipped, or Processing (received stock) are returnable
            var eligibleStatuses = new[] { SupplierOrderStatus.Delivered, SupplierOrderStatus.Completed, SupplierOrderStatus.Shipped, SupplierOrderStatus.Accepted, SupplierOrderStatus.Processing };
            var allOrders = await _orderRepository.GetPagedOrdersAsync(null, supplierId, null, 1, 1000);
            return allOrders.Where(o => eligibleStatuses.Contains(o.Status)).OrderByDescending(o => o.CreatedAt);
        }

        public async Task<IEnumerable<Device>> GetEligibleDevicesForOrderAsync(string orderId, string? productId = null)
        {
            if (string.IsNullOrWhiteSpace(orderId)) return new List<Device>();

            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null) return new List<Device>();

            // Query devices created for this supplier/order that are currently InStock or Damaged
            var filterBuilder = Builders<Device>.Filter;
            var filter = filterBuilder.Eq(d => d.SupplierId, order.SupplierId) &
                         filterBuilder.In(d => d.Status, new[] { "InStock", "Damaged", "StockIn" });

            if (!string.IsNullOrWhiteSpace(productId))
            {
                filter &= filterBuilder.Eq(d => d.ProductId, productId);
            }

            var devices = await _context.Devices.Find(filter).ToListAsync();
            return devices.Where(d => d.Status != "ReturnedToSupplier" && d.Status != "Sold");
        }

        public async Task<Dictionary<string, int>> GetReturnedQuantitiesForOrderAsync(string purchaseOrderId)
        {
            var result = new Dictionary<string, int>();
            if (string.IsNullOrWhiteSpace(purchaseOrderId)) return result;

            var existingReturns = await _returnRepository.GetReturnsForOrderAsync(purchaseOrderId);
            var validReturns = existingReturns.Where(r => r.Status != PurchaseReturnStatus.Cancelled && r.Status != PurchaseReturnStatus.SupplierRejected);

            foreach (var ret in validReturns)
            {
                foreach (var item in ret.Items)
                {
                    if (!result.ContainsKey(item.ProductId))
                        result[item.ProductId] = 0;
                    result[item.ProductId] += item.Quantity;
                }
            }

            return result;
        }

        public async Task<(bool Success, string Message, SupplierPurchaseReturn? ReturnRecord)> CreateReturnAsync(SupplierPurchaseReturn returnRecord, List<string> selectedDeviceIds, string executedBy)
        {
            if (returnRecord == null) return (false, "Invalid purchase return payload.", null);
            if (string.IsNullOrWhiteSpace(returnRecord.SupplierId)) return (false, "Supplier selection is required.", null);

            var supplier = await _supplierRepository.GetByIdAsync(returnRecord.SupplierId);
            if (supplier == null) return (false, "Selected supplier record not found.", null);

            SupplierOrder? order = null;
            if (!string.IsNullOrWhiteSpace(returnRecord.PurchaseOrderId))
            {
                order = await _orderRepository.GetByIdAsync(returnRecord.PurchaseOrderId);
            }

            // Populate Supplier & Order snapshots
            returnRecord.SupplierName = supplier.CompanyName;
            returnRecord.SupplierEmail = supplier.Email;
            returnRecord.SupplierPhone = supplier.Phone;
            returnRecord.SupplierAddress = $"{supplier.Address}, {supplier.City}, {supplier.State}";
            returnRecord.PurchaseOrderNumber = order?.OrderNumber ?? returnRecord.PurchaseOrderNumber ?? "-";
            returnRecord.OrderDate = order?.CreatedAt;
            returnRecord.CreatedBy = executedBy;
            returnRecord.CreatedAt = DateTime.UtcNow;
            returnRecord.UpdatedAt = DateTime.UtcNow;
            returnRecord.Status = PurchaseReturnStatus.Submitted;

            // Generate unique return number PR-YYYYMMDD-XXXX
            var todayPrefix = $"PR-{DateTime.UtcNow:yyyyMMdd}-";
            var filter = Builders<SupplierPurchaseReturn>.Filter.Regex(r => r.ReturnNumber, new BsonRegularExpression($"^{todayPrefix}"));
            var count = await _context.SupplierPurchaseReturns.CountDocumentsAsync(filter);
            int seq = (int)count + 1;
            while (true)
            {
                var candidate = $"{todayPrefix}{seq:D4}";
                var existing = await _returnRepository.GetByReturnNumberAsync(candidate);
                if (existing == null)
                {
                    returnRecord.ReturnNumber = candidate;
                    break;
                }
                seq++;
            }

            // Verify Devices if mobile phone IMEIs are selected
            var deviceDetailsList = new List<SupplierReturnDeviceDetail>();
            if (selectedDeviceIds != null && selectedDeviceIds.Any())
            {
                var deviceFilter = Builders<Device>.Filter.In(d => d.Id, selectedDeviceIds);
                var fetchedDevices = await _context.Devices.Find(deviceFilter).ToListAsync();

                foreach (var dev in fetchedDevices)
                {
                    if (dev.Status == "ReturnedToSupplier")
                    {
                        return (false, $"Device IMEI {dev.IMEI1} has already been returned to supplier.", null);
                    }
                    if (dev.Status == "Sold")
                    {
                        return (false, $"Device IMEI {dev.IMEI1} is already sold to a customer and cannot be returned to supplier.", null);
                    }

                    deviceDetailsList.Add(new SupplierReturnDeviceDetail
                    {
                        DeviceId = dev.Id,
                        IMEI1 = dev.IMEI1,
                        IMEI2 = dev.IMEI2 ?? "",
                        SerialNumber = dev.SerialNumber ?? "",
                        Brand = dev.Brand,
                        ModelName = dev.ModelName,
                        Variant = dev.Variant,
                        Color = dev.Color,
                        Ram = dev.Ram,
                        Storage = dev.Storage,
                        PurchasePrice = dev.PurchasePrice,
                        Status = dev.Status
                    });
                }
            }

            returnRecord.DeviceDetails = deviceDetailsList;
            returnRecord.TotalDeviceCount = deviceDetailsList.Count;

            // Calculate totals
            returnRecord.TotalQuantity = returnRecord.Items.Sum(i => i.Quantity);
            returnRecord.TotalReturnValue = returnRecord.Items.Sum(i => i.ReturnValue);

            // Add Initial Timeline Event
            returnRecord.Timeline.Add(new PurchaseReturnTimelineEvent
            {
                Status = PurchaseReturnStatus.Submitted,
                UpdatedBy = executedBy,
                Timestamp = DateTime.UtcNow,
                Remarks = $"Purchase Return #{returnRecord.ReturnNumber} created and submitted to supplier."
            });

            // Save Return Document in MongoDB
            await _returnRepository.CreateAsync(returnRecord);

            // Log System Audit Event
            await _auditLogService.LogActivityAsync(
                "SUPPLIER_RETURN_CREATED",
                executedBy,
                returnRecord.ReturnNumber,
                $"Created Purchase Return #{returnRecord.ReturnNumber} for PO #{returnRecord.PurchaseOrderNumber} (Supplier: {supplier.CompanyName}). Value: ₹{returnRecord.TotalReturnValue:N2}");

            // Send Brevo Email Notification to Supplier
            await SendReturnNotificationEmailAsync(returnRecord, supplier);

            return (true, $"Purchase Return #{returnRecord.ReturnNumber} created successfully!", returnRecord);
        }

        public async Task<(bool Success, string Message)> UpdateReturnStatusAsync(string returnId, string newStatus, string updatedBy, string? remarks = null, string? rejectionReason = null)
        {
            if (string.IsNullOrWhiteSpace(returnId)) return (false, "Return ID is required.");
            if (!PurchaseReturnStatus.AllStatuses.Contains(newStatus)) return (false, $"Invalid return status '{newStatus}'.");

            var returnRecord = await _returnRepository.GetByIdAsync(returnId);
            if (returnRecord == null) return (false, "Purchase return record not found.");

            var oldStatus = returnRecord.Status;
            returnRecord.Status = newStatus;
            returnRecord.UpdatedAt = DateTime.UtcNow;

            if (!string.IsNullOrWhiteSpace(remarks))
            {
                returnRecord.SupplierNotes = remarks;
            }

            if (!string.IsNullOrWhiteSpace(rejectionReason))
            {
                returnRecord.RejectionReason = rejectionReason;
            }

            returnRecord.Timeline.Add(new PurchaseReturnTimelineEvent
            {
                Status = newStatus,
                UpdatedBy = updatedBy,
                Timestamp = DateTime.UtcNow,
                Remarks = rejectionReason ?? remarks ?? $"Status updated from '{oldStatus}' to '{newStatus}'"
            });

            await _returnRepository.UpdateAsync(returnRecord.Id, returnRecord);

            await _auditLogService.LogActivityAsync(
                $"SUPPLIER_RETURN_{newStatus.ToUpper().Replace(" ", "_")}",
                updatedBy,
                returnRecord.ReturnNumber,
                $"Updated Return #{returnRecord.ReturnNumber} status from '{oldStatus}' to '{newStatus}'. Remarks: {remarks ?? rejectionReason ?? "-"}");

            return (true, $"Return #{returnRecord.ReturnNumber} status updated to '{newStatus}'.");
        }

        public async Task<(bool Success, string Message)> AcceptReturnAsync(string returnId, string executedBy, string? supplierNotes = null)
        {
            if (string.IsNullOrWhiteSpace(returnId)) return (false, "Return ID is required.");

            var returnRecord = await _returnRepository.GetByIdAsync(returnId);
            if (returnRecord == null) return (false, "Purchase return record not found.");

            if (returnRecord.Status == PurchaseReturnStatus.SupplierAccepted || returnRecord.Status == PurchaseReturnStatus.Completed)
            {
                return (false, $"Purchase return #{returnRecord.ReturnNumber} has already been accepted.");
            }

            var oldStatus = returnRecord.Status;
            returnRecord.Status = PurchaseReturnStatus.SupplierAccepted;
            returnRecord.UpdatedAt = DateTime.UtcNow;
            if (!string.IsNullOrWhiteSpace(supplierNotes))
            {
                returnRecord.SupplierNotes = supplierNotes;
            }

            // Deduct stock from Admin Shop Catalog (SupplierId == null) if not already deducted
            await DeductStockForReturnAsync(returnRecord, executedBy);

            returnRecord.Timeline.Add(new PurchaseReturnTimelineEvent
            {
                Status = PurchaseReturnStatus.SupplierAccepted,
                UpdatedBy = executedBy,
                Timestamp = DateTime.UtcNow,
                Remarks = supplierNotes ?? $"Supplier accepted purchase return request #{returnRecord.ReturnNumber}. Shop stock deducted."
            });

            await _returnRepository.UpdateAsync(returnRecord.Id, returnRecord);

            await _auditLogService.LogActivityAsync(
                "SUPPLIER_RETURN_ACCEPTED",
                executedBy,
                returnRecord.ReturnNumber,
                $"Supplier accepted Return #{returnRecord.ReturnNumber}. Product stock deducted from Admin Shop Dashboard.");

            return (true, $"Purchase Return #{returnRecord.ReturnNumber} has been accepted by supplier! Product stock has been deducted from shop catalog.");
        }

        public async Task<(bool Success, string Message)> RejectReturnAsync(string returnId, string executedBy, string rejectionReason)
        {
            if (string.IsNullOrWhiteSpace(returnId)) return (false, "Return ID is required.");
            if (string.IsNullOrWhiteSpace(rejectionReason)) return (false, "Rejection reason is required.");

            var returnRecord = await _returnRepository.GetByIdAsync(returnId);
            if (returnRecord == null) return (false, "Purchase return record not found.");

            var oldStatus = returnRecord.Status;
            returnRecord.Status = PurchaseReturnStatus.SupplierRejected;
            returnRecord.RejectionReason = rejectionReason;
            returnRecord.UpdatedAt = DateTime.UtcNow;

            returnRecord.Timeline.Add(new PurchaseReturnTimelineEvent
            {
                Status = PurchaseReturnStatus.SupplierRejected,
                UpdatedBy = executedBy,
                Timestamp = DateTime.UtcNow,
                Remarks = $"Supplier rejected return request: {rejectionReason}"
            });

            await _returnRepository.UpdateAsync(returnRecord.Id, returnRecord);

            await _auditLogService.LogActivityAsync(
                "SUPPLIER_RETURN_REJECTED",
                executedBy,
                returnRecord.ReturnNumber,
                $"Supplier rejected Return #{returnRecord.ReturnNumber}. Reason: {rejectionReason}");

            return (true, $"Purchase Return #{returnRecord.ReturnNumber} rejected.");
        }

        public async Task<(bool Success, string Message)> ShipReturnAsync(string returnId, string executedBy)
        {
            var returnRecord = await _returnRepository.GetByIdAsync(returnId);
            if (returnRecord == null) return (false, "Purchase return record not found.");

            if (returnRecord.Status == PurchaseReturnStatus.Shipped || returnRecord.Status == PurchaseReturnStatus.Completed)
            {
                return (false, "Return has already been shipped or completed.");
            }

            // Deduct stock and update device statuses to ReturnedToSupplier if not already deducted
            if (!returnRecord.StockDeducted)
            {
                foreach (var item in returnRecord.Items)
                {
                    var prod = await _productRepository.GetByIdAsync(item.ProductId);
                    if (prod != null)
                    {
                        int prevStock = prod.CurrentStock;
                        prod.CurrentStock = prod.CurrentStock > item.Quantity ? prod.CurrentStock - item.Quantity : 0;
                        prod.UpdatedDate = DateTime.UtcNow;
                        await _productRepository.UpdateAsync(prod.Id, prod);

                        // Log Stock Transaction Movement (Supplier Return Stock Out)
                        await _stockTxRepository.CreateAsync(new StockTransaction
                        {
                            ProductId = prod.Id,
                            ProductName = prod.Name,
                            ProductCode = prod.Code,
                            ExecutedBy = executedBy,
                            Username = executedBy,
                            Quantity = item.Quantity,
                            Type = "Stock Out",
                            Reason = "Returned to Supplier",
                            PreviousStock = prevStock,
                            CurrentStock = prod.CurrentStock,
                            Source = "Supplier Return",
                            UnitCost = item.UnitPurchasePrice,
                            Brand = prod.Brand,
                            ModelName = prod.ModelName,
                            Variant = prod.Variant,
                            Color = prod.Color,
                            Timestamp = DateTime.UtcNow
                        });
                    }
                }

                // Update individual mobile device records to ReturnedToSupplier
                if (returnRecord.DeviceDetails != null && returnRecord.DeviceDetails.Any())
                {
                    foreach (var devDetail in returnRecord.DeviceDetails)
                    {
                        var dev = await _deviceRepository.GetByIdAsync(devDetail.DeviceId);
                        if (dev != null)
                        {
                            dev.Status = "ReturnedToSupplier";
                            dev.Notes = $"Returned to Supplier ({returnRecord.SupplierName}) via Return #{returnRecord.ReturnNumber} on {DateTime.UtcNow:dd-MMM-yyyy}. Reason: {returnRecord.Reason}";
                            dev.UpdatedDate = DateTime.UtcNow;
                            await _deviceRepository.UpdateAsync(dev.Id, dev);
                        }
                    }
                }

                returnRecord.StockDeducted = true;
            }

            returnRecord.Status = PurchaseReturnStatus.Shipped;
            returnRecord.UpdatedAt = DateTime.UtcNow;
            returnRecord.Timeline.Add(new PurchaseReturnTimelineEvent
            {
                Status = PurchaseReturnStatus.Shipped,
                UpdatedBy = executedBy,
                Timestamp = DateTime.UtcNow,
                Remarks = "Products and mobile devices shipped back to supplier. Inventory deducted."
            });

            await _returnRepository.UpdateAsync(returnRecord.Id, returnRecord);

            await _auditLogService.LogActivityAsync(
                "SUPPLIER_RETURN_SHIPPED",
                executedBy,
                returnRecord.ReturnNumber,
                $"Shipped Purchase Return #{returnRecord.ReturnNumber} back to supplier {returnRecord.SupplierName}. Stock out recorded.");

            return (true, $"Return #{returnRecord.ReturnNumber} marked as Shipped and inventory updated.");
        }

        public async Task<(bool Success, string Message)> CancelReturnAsync(string returnId, string executedBy)
        {
            var returnRecord = await _returnRepository.GetByIdAsync(returnId);
            if (returnRecord == null) return (false, "Purchase return record not found.");

            if (returnRecord.Status == PurchaseReturnStatus.Shipped || returnRecord.Status == PurchaseReturnStatus.Completed)
            {
                return (false, "Cannot cancel a return that has already been shipped or completed.");
            }

            returnRecord.Status = PurchaseReturnStatus.Cancelled;
            returnRecord.UpdatedAt = DateTime.UtcNow;
            returnRecord.Timeline.Add(new PurchaseReturnTimelineEvent
            {
                Status = PurchaseReturnStatus.Cancelled,
                UpdatedBy = executedBy,
                Timestamp = DateTime.UtcNow,
                Remarks = "Purchase Return cancelled by admin."
            });

            await _returnRepository.UpdateAsync(returnRecord.Id, returnRecord);

            await _auditLogService.LogActivityAsync(
                "SUPPLIER_RETURN_CANCELLED",
                executedBy,
                returnRecord.ReturnNumber,
                $"Cancelled Purchase Return #{returnRecord.ReturnNumber}.");

            return (true, $"Purchase Return #{returnRecord.ReturnNumber} cancelled successfully.");
        }

        private async Task SendReturnNotificationEmailAsync(SupplierPurchaseReturn returnRecord, Supplier supplier)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(supplier.Email)) return;

                var sb = new StringBuilder();
                sb.AppendLine("<!DOCTYPE html><html><body style='font-family:sans-serif;background:#f4f6f8;padding:20px;'>");
                sb.AppendLine("<div style='max-width:600px;margin:auto;background:#fff;padding:25px;border-radius:10px;border:1px solid #e2e8f0;'>");
                sb.AppendLine($"<h2 style='color:#1e293b;margin-top:0;'>Purchase Return Request #{returnRecord.ReturnNumber}</h2>");
                sb.AppendLine($"<p>Dear <strong>{supplier.ContactPerson ?? supplier.CompanyName}</strong>,</p>");
                sb.AppendLine($"<p>A new supplier purchase return request has been initiated for PO <strong>#{returnRecord.PurchaseOrderNumber}</strong>.</p>");
                sb.AppendLine("<div style='background:#f8fafc;padding:15px;border-radius:8px;margin:15px 0;border-left:4px solid #ef4444;'>");
                sb.AppendLine($"<p style='margin:4px 0;'><strong>Return Number:</strong> {returnRecord.ReturnNumber}</p>");
                sb.AppendLine($"<p style='margin:4px 0;'><strong>Return Reason:</strong> {returnRecord.Reason}</p>");
                sb.AppendLine($"<p style='margin:4px 0;'><strong>Total Quantity:</strong> {returnRecord.TotalQuantity} Items ({returnRecord.TotalDeviceCount} Mobile Devices)</p>");
                sb.AppendLine($"<p style='margin:4px 0;'><strong>Total Value:</strong> ₹{returnRecord.TotalReturnValue:N2}</p>");
                if (!string.IsNullOrWhiteSpace(returnRecord.AdditionalRemarks))
                {
                    sb.AppendLine($"<p style='margin:4px 0;'><strong>Remarks:</strong> {returnRecord.AdditionalRemarks}</p>");
                }
                sb.AppendLine("</div>");

                sb.AppendLine("<h3>Returned Items Summary</h3>");
                sb.AppendLine("<table style='width:100%;border-collapse:collapse;margin-bottom:20px;' border='1' cellpadding='8' cellspacing='0'>");
                sb.AppendLine("<tr style='background:#f1f5f9;text-align:left;'><th>Product</th><th>Qty</th><th>Unit Cost</th><th>Total</th></tr>");

                foreach (var item in returnRecord.Items)
                {
                    sb.AppendLine($"<tr><td>{item.ProductName} ({item.Brand} {item.Variant})</td><td>{item.Quantity}</td><td>₹{item.UnitPurchasePrice:N2}</td><td>₹{item.ReturnValue:N2}</td></tr>");
                }
                sb.AppendLine("</table>");

                sb.AppendLine("<p>Please log in to your <strong>Supplier Portal</strong> to review, accept, or respond to this purchase return request.</p>");
                sb.AppendLine("<p style='color:#64748b;font-size:12px;margin-top:30px;'>Smart Inventory Management System (SIMS)</p>");
                sb.AppendLine("</div></body></html>");

                var (success, messageId, apiResp, errorMsg) = await _emailService.SendTransactionalEmailAsync(
                    supplier.Email,
                    $"[SIMS] Purchase Return Alert #{returnRecord.ReturnNumber} - {returnRecord.Reason}",
                    sb.ToString()
                );

                if (success)
                {
                    returnRecord.EmailSent = true;
                    returnRecord.EmailSentAt = DateTime.UtcNow;
                    await _returnRepository.UpdateAsync(returnRecord.Id, returnRecord);
                    await _auditLogService.LogActivityAsync("SUPPLIER_RETURN_EMAIL_SENT", "System", returnRecord.ReturnNumber, $"Email sent to {supplier.Email}");
                }
                else
                {
                    returnRecord.EmailError = errorMsg;
                    await _returnRepository.UpdateAsync(returnRecord.Id, returnRecord);
                    await _auditLogService.LogActivityAsync("SUPPLIER_RETURN_EMAIL_FAILED", "System", returnRecord.ReturnNumber, $"Email failed to {supplier.Email}: {errorMsg}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send return notification email for Return #{returnRecord.ReturnNumber}");
            }
        }

        public async Task SyncAcceptedReturnsToShopCatalogAsync()
        {
            try
            {
                var allReturns = await _returnRepository.GetAllAsync();
                var acceptedReturns = allReturns.Where(r =>
                    r.Status == PurchaseReturnStatus.SupplierAccepted ||
                    r.Status == PurchaseReturnStatus.ReceivedBySupplier ||
                    r.Status == PurchaseReturnStatus.Completed
                ).ToList();

                if (!acceptedReturns.Any()) return;

                var allTxs = await _stockTxRepository.GetAllAsync();
                var existingTxReasons = allTxs.Select(t => t.Reason ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var ret in acceptedReturns)
                {
                    string expectedReason = $"Purchase Return Accepted (#{ret.ReturnNumber})";
                    if (!ret.StockDeducted || !existingTxReasons.Contains(expectedReason))
                    {
                        await DeductStockForReturnAsync(ret, ret.CreatedBy ?? "System Auto-Sync");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SyncAcceptedReturnsToShopCatalogAsync");
            }
        }

        private async Task DeductStockForReturnAsync(SupplierPurchaseReturn returnRecord, string executedBy)
        {
            var allProducts = await _productRepository.GetAllAsync();
            var shopProducts = allProducts.Where(p => string.IsNullOrWhiteSpace(p.SupplierId)).ToList();

            foreach (var item in returnRecord.Items)
            {
                var shopProduct = await FindShopProductAsync(item, shopProducts);
                if (shopProduct != null)
                {
                    int prevStock = shopProduct.CurrentStock;
                    shopProduct.CurrentStock = System.Math.Max(0, shopProduct.CurrentStock - item.Quantity);
                    shopProduct.UpdatedDate = DateTime.UtcNow;
                    await _productRepository.UpdateAsync(shopProduct.Id, shopProduct);

                    // Log Stock Transaction Movement (Admin Shop Inventory Stock Out)
                    await _stockTxRepository.CreateAsync(new StockTransaction
                    {
                        ProductId = shopProduct.Id,
                        ProductName = shopProduct.Name,
                        ProductCode = shopProduct.Code,
                        ExecutedBy = executedBy,
                        Username = executedBy,
                        Quantity = item.Quantity,
                        Type = "Stock Out",
                        Reason = $"Purchase Return Accepted (#{returnRecord.ReturnNumber})",
                        PreviousStock = prevStock,
                        CurrentStock = shopProduct.CurrentStock,
                        Source = "Supplier Return Accepted",
                        UnitCost = item.UnitPurchasePrice,
                        Brand = shopProduct.Brand,
                        ModelName = shopProduct.ModelName,
                        Variant = shopProduct.Variant,
                        Color = shopProduct.Color,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }

            // Update individual mobile device records to ReturnedToSupplier
            if (returnRecord.DeviceDetails != null && returnRecord.DeviceDetails.Any())
            {
                foreach (var devDetail in returnRecord.DeviceDetails)
                {
                    var dev = await _deviceRepository.GetByIdAsync(devDetail.DeviceId);
                    if (dev != null && dev.Status != "ReturnedToSupplier")
                    {
                        dev.Status = "ReturnedToSupplier";
                        dev.Notes = $"Returned to Supplier ({returnRecord.SupplierName}) via Return #{returnRecord.ReturnNumber} on {DateTime.UtcNow:dd-MMM-yyyy}. Reason: {returnRecord.Reason}";
                        dev.UpdatedDate = DateTime.UtcNow;
                        await _deviceRepository.UpdateAsync(dev.Id, dev);
                    }
                }
            }

            returnRecord.StockDeducted = true;
            await _returnRepository.UpdateAsync(returnRecord.Id, returnRecord);
        }

        private async Task<Product?> FindShopProductAsync(SupplierPurchaseReturnItem item, List<Product> shopProducts)
        {
            if (shopProducts == null || !shopProducts.Any()) return null;

            // 1. Direct ID match (if item.ProductId is already a Shop Product ID)
            if (!string.IsNullOrEmpty(item.ProductId))
            {
                var matchById = shopProducts.FirstOrDefault(p => p.Id == item.ProductId);
                if (matchById != null) return matchById;
            }

            // 2. Lookup original Supplier Product (if item.ProductId was a Supplier Product ID)
            Product? supplierProduct = null;
            if (!string.IsNullOrEmpty(item.ProductId))
            {
                supplierProduct = await _productRepository.GetByIdAsync(item.ProductId);
            }

            // 3. Match by Product Code / SKU
            if (supplierProduct != null && !string.IsNullOrWhiteSpace(supplierProduct.Code))
            {
                var matchByCode = shopProducts.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Code) && p.Code.Equals(supplierProduct.Code, StringComparison.OrdinalIgnoreCase));
                if (matchByCode != null) return matchByCode;
            }

            // 4. Match by Barcode
            if (supplierProduct != null && !string.IsNullOrWhiteSpace(supplierProduct.Barcode))
            {
                var matchByBarcode = shopProducts.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Barcode) && p.Barcode.Equals(supplierProduct.Barcode, StringComparison.OrdinalIgnoreCase));
                if (matchByBarcode != null) return matchByBarcode;
            }

            // 5. Match by Exact Name
            if (!string.IsNullOrWhiteSpace(item.ProductName))
            {
                var matchByName = shopProducts.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Name) && p.Name.Equals(item.ProductName, StringComparison.OrdinalIgnoreCase));
                if (matchByName != null) return matchByName;
            }

            // 6. Match by Brand + ModelName + Variant / Storage
            var matchBySpecs = shopProducts.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.Brand) && !string.IsNullOrWhiteSpace(item.Brand) && p.Brand.Equals(item.Brand, StringComparison.OrdinalIgnoreCase) &&
                (!string.IsNullOrWhiteSpace(p.ModelName) && p.ModelName.Equals(item.ModelName, StringComparison.OrdinalIgnoreCase)) &&
                (string.IsNullOrWhiteSpace(item.Variant) || string.Equals(p.Variant, item.Variant, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Storage, item.Storage, StringComparison.OrdinalIgnoreCase))
            );
            if (matchBySpecs != null) return matchBySpecs;

            // 7. Match by Brand + ModelName
            var matchByBrandModel = shopProducts.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p.Brand) && !string.IsNullOrWhiteSpace(item.Brand) && p.Brand.Equals(item.Brand, StringComparison.OrdinalIgnoreCase) &&
                (!string.IsNullOrWhiteSpace(p.ModelName) && p.ModelName.Equals(item.ModelName, StringComparison.OrdinalIgnoreCase))
            );
            if (matchByBrandModel != null) return matchByBrandModel;

            // 8. Substring name match
            if (!string.IsNullOrWhiteSpace(item.ProductName))
            {
                var matchSub = shopProducts.FirstOrDefault(p =>
                    !string.IsNullOrWhiteSpace(p.Name) && (p.Name.Contains(item.ProductName, StringComparison.OrdinalIgnoreCase) || item.ProductName.Contains(p.Name, StringComparison.OrdinalIgnoreCase))
                );
                if (matchSub != null) return matchSub;
            }

            return null;
        }
    }
}
