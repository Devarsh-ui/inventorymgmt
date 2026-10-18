using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using InventoryManagementSystem.ViewModels;
using InventoryManagementSystem.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Services
{
    public class ReportService : IReportService
    {
        private readonly ISaleRepository _saleRepository;
        private readonly IProductRepository _productRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IStockTransactionRepository _stockTransactionRepository;
        private readonly IAuditLogRepository _auditLogRepository;
        private readonly IUserRepository _userRepository;
        private readonly IDeviceRepository _deviceRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly ISupplierRepository _supplierRepository;
        private readonly IReturnRepository _returnRepository;
        private readonly IExchangeRepository _exchangeRepository;
        private readonly IRepairRepository _repairRepository;
        private readonly ISupplierPurchaseReturnRepository _supplierPurchaseReturnRepository;

        public ReportService(
            ISaleRepository saleRepository,
            IProductRepository productRepository,
            ICategoryRepository categoryRepository,
            IStockTransactionRepository stockTransactionRepository,
            IAuditLogRepository auditLogRepository,
            IUserRepository userRepository,
            IDeviceRepository deviceRepository,
            ICustomerRepository customerRepository,
            ISupplierRepository supplierRepository,
            IReturnRepository returnRepository,
            IExchangeRepository exchangeRepository,
            IRepairRepository repairRepository,
            ISupplierPurchaseReturnRepository supplierPurchaseReturnRepository)
        {
            _saleRepository = saleRepository;
            _productRepository = productRepository;
            _categoryRepository = categoryRepository;
            _stockTransactionRepository = stockTransactionRepository;
            _auditLogRepository = auditLogRepository;
            _userRepository = userRepository;
            _deviceRepository = deviceRepository;
            _customerRepository = customerRepository;
            _supplierRepository = supplierRepository;
            _returnRepository = returnRepository;
            _exchangeRepository = exchangeRepository;
            _repairRepository = repairRepository;
            _supplierPurchaseReturnRepository = supplierPurchaseReturnRepository;

            QuestPDF.Settings.License = LicenseType.Community;
        }

        public async Task<ReportResultData> BuildReportDataAsync(ReportFilterRequest request)
        {
            int pSize = request.PageSize < 5 ? 20 : (request.PageSize > 200 ? 200 : request.PageSize);
            int pNum = request.Page < 1 ? 1 : request.Page;

            var result = new ReportResultData
            {
                ReportType = request.ReportType,
                Page = pNum,
                PageSize = pSize,
                GeneratedAt = DateTime.UtcNow
            };

            var (startUtc, endUtc, dateLabel) = ResolveDateRange(request.DatePreset, request.StartDate, request.EndDate);

            var categories = (await _categoryRepository.GetAllAsync()).Where(c => string.IsNullOrWhiteSpace(c.SupplierId)).ToList();
            var categoryDict = categories.ToDictionary(c => c.Id, c => c.Name);
            var products = (await _productRepository.GetAllAsync()).Where(p => string.IsNullOrWhiteSpace(p.SupplierId)).ToList();
            var productDict = products.ToDictionary(p => p.Id, p => p);
            var devices = (await _deviceRepository.GetAllAsync()).ToList();

            var filterParts = new List<string> { $"Date: {dateLabel}" };
            if (!string.IsNullOrWhiteSpace(request.SearchTerm)) filterParts.Add($"Search: '{request.SearchTerm}'");
            if (!string.IsNullOrWhiteSpace(request.CategoryId) && categoryDict.TryGetValue(request.CategoryId, out var catName)) filterParts.Add($"Category: {catName}");
            if (!string.IsNullOrWhiteSpace(request.ProductId) && productDict.TryGetValue(request.ProductId, out var prodItem)) filterParts.Add($"Product: {prodItem.Name}");
            if (!string.IsNullOrWhiteSpace(request.TransactionType)) filterParts.Add($"Type: {request.TransactionType}");
            if (!string.IsNullOrWhiteSpace(request.PaymentStatus)) filterParts.Add($"Payment: {request.PaymentStatus}");
            if (!string.IsNullOrWhiteSpace(request.StockStatus) && request.StockStatus != "All") filterParts.Add($"Stock: {request.StockStatus}");
            if (!string.IsNullOrWhiteSpace(request.ReturnStatus) && request.ReturnStatus != "All") filterParts.Add($"Return Status: {request.ReturnStatus}");
            if (!string.IsNullOrWhiteSpace(request.ReturnDestination) && request.ReturnDestination != "All") filterParts.Add($"Destination: {request.ReturnDestination}");
            result.AppliedFiltersText = string.Join(" | ", filterParts);

            var inStockDevices = devices.Where(d => d.Status == "InStock").ToList();
            var damagedDevices = devices.Where(d => d.Status == "Damaged").ToList();
            var repairDevices = devices.Where(d => d.Status == "UnderRepair").ToList();

            var globalStats = new ReportSummaryStats
            {
                TotalProductsCount = products.Count,
                TotalCategoriesCount = categories.Count,
                CurrentInventoryValue = products.Sum(p => p.CurrentStock * p.PurchasePrice),
                SellableInventoryValue = inStockDevices.Any() ? inStockDevices.Sum(d => d.PurchasePrice) : products.Sum(p => p.CurrentStock * p.PurchasePrice),
                DamagedInventoryCount = damagedDevices.Count,
                DamagedInventoryValue = damagedDevices.Sum(d => d.PurchasePrice),
                UnderRepairInventoryCount = repairDevices.Count,
                UnderRepairInventoryValue = repairDevices.Sum(d => d.PurchasePrice),
                PotentialSalesValue = products.Sum(p => p.CurrentStock * p.SellingPrice),
                TotalInventoryQty = products.Sum(p => p.CurrentStock),
                LowStockCount = products.Count(p => p.CurrentStock > 0 && p.CurrentStock <= p.MinimumStock),
                OutOfStockCount = products.Count(p => p.CurrentStock == 0),
                TotalProfitPotential = products.Sum(p => p.CurrentStock * (p.SellingPrice - p.PurchasePrice))
            };

            switch ((request.ReportType ?? "Sales").Trim().ToLower())
            {
                case "imeiinventory":
                case "device":
                    await BuildImeiInventoryReportAsync(request, result, globalStats);
                    break;

                case "tradein":
                case "exchange":
                    await BuildExchangeReportAsync(request, result, globalStats);
                    break;

                case "returns":
                case "return":
                    await BuildReturnsReportAsync(request, result, globalStats);
                    break;

                case "purchasereturns":
                case "purchasereturn":
                    await BuildSupplierPurchaseReturnsReportAsync(request, result, globalStats);
                    break;

                case "repairs":
                case "repair":
                    await BuildRepairsReportAsync(request, result, globalStats);
                    break;

                case "warranty":
                    await BuildWarrantyReportAsync(request, result, globalStats);
                    break;

                case "customer":
                    await BuildCustomerReportAsync(request, result, globalStats);
                    break;

                case "supplier":
                    await BuildSupplierReportAsync(request, result, globalStats);
                    break;

                case "inventory":
                    BuildInventoryReport(request, result, products, categoryDict, globalStats);
                    break;

                case "stockmovement":
                case "stockhistory":
                    await BuildStockMovementReportAsync(request, result, startUtc, endUtc, products, categoryDict, globalStats);
                    break;

                case "employeeactivity":
                    await BuildEmployeeActivityReportAsync(request, result, startUtc, endUtc, globalStats);
                    break;

                case "alerts":
                    BuildAlertsReport(request, result, products, categoryDict, globalStats);
                    break;

                case "category":
                    await BuildCategoryReportAsync(request, result, startUtc, endUtc, products, categories, globalStats);
                    break;

                case "productperformance":
                    await BuildProductPerformanceReportAsync(request, result, startUtc, endUtc, products, categoryDict, globalStats);
                    break;

                case "executivesummary":
                    await BuildExecutiveSummaryReportAsync(request, result, startUtc, endUtc, products, categories, globalStats);
                    break;

                case "sales":
                default:
                    await BuildSalesReportAsync(request, result, startUtc, endUtc, products, categoryDict, globalStats);
                    break;
            }

            return result;
        }

        #region Report Builders

        private async Task BuildImeiInventoryReportAsync(ReportFilterRequest req, ReportResultData res, ReportSummaryStats stats)
        {
            res.ReportTitle = "Mobile Shop IMEI-Level Physical Device Stock Report";
            res.Headers = new List<string> { "#", "IMEI 1", "IMEI 2", "Brand & Model", "Variant / Color", "Status", "Purchase Cost", "Selling Price", "Invoice #", "Customer Info", "Stock Date" };

            var devices = await _deviceRepository.GetAllAsync();
            var devList = devices.ToList();

            if (!string.IsNullOrWhiteSpace(req.SearchTerm))
            {
                var s = req.SearchTerm.Trim().ToLower();
                devList = devList.Where(d =>
                    (d.IMEI1 != null && d.IMEI1.ToLower().Contains(s)) ||
                    (d.IMEI2 != null && d.IMEI2.ToLower().Contains(s)) ||
                    (d.Brand != null && d.Brand.ToLower().Contains(s)) ||
                    (d.ModelName != null && d.ModelName.ToLower().Contains(s)) ||
                    (d.CustomerName != null && d.CustomerName.ToLower().Contains(s)) ||
                    (d.InvoiceNumber != null && d.InvoiceNumber.ToLower().Contains(s))
                ).ToList();
            }

            if (!string.IsNullOrWhiteSpace(req.StockStatus) && req.StockStatus != "All")
            {
                devList = devList.Where(d => string.Equals(d.Status, req.StockStatus, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(req.ReturnDestination) && req.ReturnDestination != "All")
            {
                devList = devList.Where(d => string.Equals(d.Status, req.ReturnDestination, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            res.SummaryStats = stats;
            res.TotalCount = devList.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = devList.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var d in paged)
            {
                string badgeColor = d.Status == "InStock" ? "bg-success" : (d.Status == "Sold" ? "bg-primary" : (d.Status == "Damaged" ? "bg-danger" : "bg-warning text-dark"));

                res.Rows.Add(new ReportRowItem
                {
                    Id = d.Id,
                    PrimaryText = d.IMEI1,
                    SecondaryText = d.IMEI2 ?? "-",
                    ProductInfo = $"{d.Brand} {d.ModelName}",
                    CategoryName = $"{d.Variant} {d.Color}",
                    BadgeText = d.Status,
                    BadgeClass = badgeColor,
                    CostPrice = d.PurchasePrice,
                    SellingPrice = d.SellingPrice,
                    ReferenceInfo = d.InvoiceNumber ?? "-",
                    CustomerInfo = !string.IsNullOrWhiteSpace(d.CustomerName) ? $"{d.CustomerName} ({d.CustomerPhone})" : "-",
                    DateString = d.CreatedDate.ToIstString("yyyy-MM-dd HH:mm IST"),
                    UserExecutor = d.CreatedBy ?? "System"
                });
            }
        }

        private async Task BuildExchangeReportAsync(ReportFilterRequest req, ReportResultData res, ReportSummaryStats stats)
        {
            res.ReportTitle = "Mobile Trade-In & Phone Exchange Valuation Report";
            res.Headers = new List<string> { "#", "Exchange #", "Date & Time (IST)", "Customer", "Old Phone Brand / Model", "Old IMEI", "Condition", "Exchange Value", "Offset Invoice", "Executed By" };

            var exchanges = await _exchangeRepository.GetAllAsync();
            var exchList = exchanges.ToList();

            if (!string.IsNullOrWhiteSpace(req.SearchTerm))
            {
                var s = req.SearchTerm.Trim().ToLower();
                exchList = exchList.Where(e =>
                    (e.ExchangeNumber != null && e.ExchangeNumber.ToLower().Contains(s)) ||
                    (e.CustomerName != null && e.CustomerName.ToLower().Contains(s)) ||
                    (e.OldBrand != null && e.OldBrand.ToLower().Contains(s)) ||
                    (e.OldModel != null && e.OldModel.ToLower().Contains(s)) ||
                    (e.OldImei1 != null && e.OldImei1.ToLower().Contains(s))
                ).ToList();
            }

            res.SummaryStats = stats;
            res.TotalCount = exchList.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = exchList.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var e in paged)
            {
                res.Rows.Add(new ReportRowItem
                {
                    Id = e.Id,
                    PrimaryText = e.ExchangeNumber,
                    DateString = e.Date.ToIstString("yyyy-MM-dd HH:mm IST"),
                    CustomerInfo = $"{e.CustomerName} ({e.CustomerPhone})",
                    ProductInfo = $"{e.OldBrand} {e.OldModel} ({e.OldStorage})",
                    SecondaryText = e.OldImei1 ?? "-",
                    BadgeText = e.Condition,
                    BadgeClass = "bg-info text-dark",
                    GrandTotal = e.FinalExchangeValue,
                    UserExecutor = e.ExecutedBy ?? "System"
                });
            }
        }

        private async Task BuildReturnsReportAsync(ReportFilterRequest req, ReportResultData res, ReportSummaryStats stats)
        {
            res.ReportTitle = "Customer Device & Accessory Returns Audit Report";
            res.Headers = new List<string> { "#", "Return #", "Invoice #", "Date & Time (IST)", "Customer", "Product / Device", "IMEI", "Reason", "Restock Destination", "Refund Amount", "Processed By" };

            var returns = await _returnRepository.GetAllAsync();
            var retList = returns.ToList();

            if (!string.IsNullOrWhiteSpace(req.SearchTerm))
            {
                var s = req.SearchTerm.Trim().ToLower();
                retList = retList.Where(r =>
                    (r.ReturnNumber != null && r.ReturnNumber.ToLower().Contains(s)) ||
                    (r.InvoiceNumber != null && r.InvoiceNumber.ToLower().Contains(s)) ||
                    (r.CustomerName != null && r.CustomerName.ToLower().Contains(s)) ||
                    (r.ProductName != null && r.ProductName.ToLower().Contains(s)) ||
                    (r.IMEI != null && r.IMEI.ToLower().Contains(s))
                ).ToList();
            }

            if (!string.IsNullOrWhiteSpace(req.ReturnDestination) && req.ReturnDestination != "All")
            {
                retList = retList.Where(r => string.Equals(r.DeviceStatusTarget, req.ReturnDestination, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(req.InvoiceNumber))
            {
                retList = retList.Where(r => r.InvoiceNumber.ToLower().Contains(req.InvoiceNumber.Trim().ToLower())).ToList();
            }
            if (!string.IsNullOrWhiteSpace(req.IMEI))
            {
                retList = retList.Where(r => r.IMEI != null && r.IMEI.Contains(req.IMEI.Trim())).ToList();
            }

            stats.TotalReturnsAmount = retList.Sum(r => r.RefundAmount);
            res.SummaryStats = stats;
            res.TotalCount = retList.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = retList.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var r in paged)
            {
                string destBadge = r.DeviceStatusTarget == "Returned" || r.DeviceStatusTarget == "InStock" ? "bg-success" : (r.DeviceStatusTarget == "Damaged" ? "bg-danger" : "bg-warning text-dark");

                res.Rows.Add(new ReportRowItem
                {
                    Id = r.Id,
                    PrimaryText = r.ReturnNumber,
                    ReferenceInfo = r.InvoiceNumber ?? "-",
                    DateString = r.ReturnDate.ToIstString("yyyy-MM-dd HH:mm IST"),
                    CustomerInfo = $"{r.CustomerName} ({r.CustomerPhone})",
                    ProductInfo = r.ProductName,
                    SecondaryText = r.IMEI ?? "-",
                    Reason = r.Reason,
                    BadgeText = r.DeviceStatusTarget ?? "Returned",
                    BadgeClass = destBadge,
                    GrandTotal = r.RefundAmount,
                    RefundedAmount = r.RefundAmount,
                    UserExecutor = r.ExecutedBy ?? "System"
                });
            }
        }

        private async Task BuildSupplierPurchaseReturnsReportAsync(ReportFilterRequest req, ReportResultData res, ReportSummaryStats stats)
        {
            res.ReportTitle = "Supplier Purchase Returns Audit Report";
            res.Headers = new List<string> { "#", "Return #", "PO Number", "Date & Time (IST)", "Supplier", "Reason", "Resolution", "Total Quantity", "Return Value", "Status", "Created By" };

            var returns = await _supplierPurchaseReturnRepository.GetPagedReturnsAsync(req.SearchTerm, req.SupplierId, req.ReturnStatus != "All" ? req.ReturnStatus : null, null, 1, 50000);
            var retList = returns.ToList();

            stats.TotalReturnsAmount = retList.Sum(r => r.TotalReturnValue);
            res.SummaryStats = stats;
            res.TotalCount = retList.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = retList.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var r in paged)
            {
                string statusBadge = r.Status == "Supplier Accepted" ? "bg-success" : (r.Status == "Supplier Rejected" ? "bg-danger" : (r.Status == "Shipped" ? "bg-info text-dark" : "bg-warning text-dark"));

                res.Rows.Add(new ReportRowItem
                {
                    Id = r.Id,
                    PrimaryText = r.ReturnNumber,
                    ReferenceInfo = r.PurchaseOrderNumber ?? "-",
                    DateString = r.CreatedAt.ToIstString("yyyy-MM-dd HH:mm IST"),
                    CustomerInfo = !string.IsNullOrWhiteSpace(r.SupplierCompanyName) && r.SupplierCompanyName != r.DisplaySupplierVendorName ? $"{r.DisplaySupplierVendorName} ({r.SupplierCompanyName})" : r.DisplaySupplierVendorName,
                    Reason = r.Reason,
                    CategoryName = r.ResolutionType,
                    StockQty = r.TotalQuantity,
                    GrandTotal = r.TotalReturnValue,
                    BadgeText = r.Status,
                    BadgeClass = statusBadge,
                    UserExecutor = r.CreatedBy ?? "System"
                });
            }
        }

        private async Task BuildRepairsReportAsync(ReportFilterRequest req, ReportResultData res, ReportSummaryStats stats)
        {
            res.ReportTitle = "Mobile Service & Repair Job Tickets Report";
            res.Headers = new List<string> { "#", "Ticket #", "Date & Time (IST)", "Customer", "Device Model", "IMEI", "Problem Description", "Technician", "Estimated Cost", "Final Cost", "Status" };

            var repairs = await _repairRepository.GetAllAsync();
            var repList = repairs.ToList();

            if (!string.IsNullOrWhiteSpace(req.SearchTerm))
            {
                var s = req.SearchTerm.Trim().ToLower();
                repList = repList.Where(r =>
                    (r.TicketNumber != null && r.TicketNumber.ToLower().Contains(s)) ||
                    (r.CustomerName != null && r.CustomerName.ToLower().Contains(s)) ||
                    (r.DeviceModel != null && r.DeviceModel.ToLower().Contains(s)) ||
                    (r.IMEI != null && r.IMEI.ToLower().Contains(s)) ||
                    (r.TechnicianName != null && r.TechnicianName.ToLower().Contains(s))
                ).ToList();
            }

            res.SummaryStats = stats;
            res.TotalCount = repList.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = repList.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var r in paged)
            {
                res.Rows.Add(new ReportRowItem
                {
                    Id = r.Id,
                    PrimaryText = r.TicketNumber,
                    DateString = r.CreatedDate.ToIstString("yyyy-MM-dd HH:mm IST"),
                    CustomerInfo = $"{r.CustomerName} ({r.CustomerPhone})",
                    ProductInfo = $"{r.DeviceBrand} {r.DeviceModel}",
                    SecondaryText = r.IMEI ?? "-",
                    Reason = r.ProblemDescription ?? "-",
                    BadgeText = r.Status,
                    BadgeClass = r.Status == "Delivered" ? "bg-success" : (r.Status == "Repairing" ? "bg-warning text-dark" : "bg-info text-dark"),
                    CostPrice = r.EstimatedCost,
                    GrandTotal = r.FinalCost,
                    UserExecutor = r.TechnicianName ?? "Unassigned"
                });
            }
        }

        private async Task BuildWarrantyReportAsync(ReportFilterRequest req, ReportResultData res, ReportSummaryStats stats)
        {
            res.ReportTitle = "Mobile Device Warranty Status & Expiry Report";
            res.Headers = new List<string> { "#", "IMEI 1", "Brand & Model", "Customer Name", "Customer Phone", "Sold Date", "Warranty End Date", "Warranty Status" };

            var devices = await _deviceRepository.GetDevicesByStatusAsync("Sold");
            var devList = devices.ToList();

            if (!string.IsNullOrWhiteSpace(req.SearchTerm))
            {
                var s = req.SearchTerm.Trim().ToLower();
                devList = devList.Where(d =>
                    (d.IMEI1 != null && d.IMEI1.ToLower().Contains(s)) ||
                    (d.CustomerName != null && d.CustomerName.ToLower().Contains(s)) ||
                    (d.CustomerPhone != null && d.CustomerPhone.ToLower().Contains(s)) ||
                    (d.Brand != null && d.Brand.ToLower().Contains(s)) ||
                    (d.ModelName != null && d.ModelName.ToLower().Contains(s))
                ).ToList();
            }

            res.SummaryStats = stats;
            res.TotalCount = devList.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = devList.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var d in paged)
            {
                var soldDateStr = d.SoldDate.HasValue ? d.SoldDate.Value.ToIstString("yyyy-MM-dd") : "-";
                var warrantyEndStr = d.WarrantyEndDate.HasValue ? d.WarrantyEndDate.Value.ToIstString("yyyy-MM-dd") : "N/A";
                var isExpired = d.WarrantyEndDate.HasValue && d.WarrantyEndDate.Value < DateTime.UtcNow;

                res.Rows.Add(new ReportRowItem
                {
                    Id = d.Id,
                    PrimaryText = d.IMEI1,
                    ProductInfo = $"{d.Brand} {d.ModelName}",
                    CustomerInfo = d.CustomerName ?? "-",
                    SecondaryText = d.CustomerPhone ?? "-",
                    DateString = soldDateStr,
                    ReferenceInfo = warrantyEndStr,
                    BadgeText = isExpired ? "Expired" : "Active",
                    BadgeClass = isExpired ? "bg-danger" : "bg-success"
                });
            }
        }

        private async Task BuildCustomerReportAsync(ReportFilterRequest req, ReportResultData res, ReportSummaryStats stats)
        {
            res.ReportTitle = "Customer Accounts & Purchase History Report";
            res.Headers = new List<string> { "#", "Customer Name", "Phone", "Email", "GSTIN", "Total Purchases", "Outstanding Balance", "Created Date" };

            var customers = await _customerRepository.GetAllAsync();
            var custList = customers.ToList();

            if (!string.IsNullOrWhiteSpace(req.SearchTerm))
            {
                var s = req.SearchTerm.Trim().ToLower();
                custList = custList.Where(c =>
                    (c.Name != null && c.Name.ToLower().Contains(s)) ||
                    (c.Phone != null && c.Phone.ToLower().Contains(s)) ||
                    (c.Email != null && c.Email.ToLower().Contains(s))
                ).ToList();
            }

            res.SummaryStats = stats;
            res.TotalCount = custList.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = custList.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var c in paged)
            {
                res.Rows.Add(new ReportRowItem
                {
                    Id = c.Id,
                    PrimaryText = c.Name,
                    SecondaryText = c.Phone,
                    CustomerInfo = c.Email ?? "-",
                    BadgeText = c.Gstin ?? "-",
                    GrandTotal = c.TotalPurchases,
                    SubTotal = c.OutstandingBalance,
                    DateString = c.CreatedDate.ToIstString("yyyy-MM-dd")
                });
            }
        }

        private async Task BuildSupplierReportAsync(ReportFilterRequest req, ReportResultData res, ReportSummaryStats stats)
        {
            res.ReportTitle = "Supplier Directory & Procurement Report";
            res.Headers = new List<string> { "#", "Vendor / Brand Name", "Company / Legal Name", "Date Added", "Phone / Contact", "Payables", "GSTIN" };

            var suppliers = await _supplierRepository.GetAllAsync();
            var supList = suppliers.ToList();

            if (!string.IsNullOrWhiteSpace(req.SearchTerm))
            {
                var s = req.SearchTerm.Trim().ToLower();
                supList = supList.Where(sup =>
                    (!string.IsNullOrEmpty(sup.VendorName) && sup.VendorName.ToLower().Contains(s)) ||
                    (!string.IsNullOrEmpty(sup.CompanyName) && sup.CompanyName.ToLower().Contains(s)) ||
                    (!string.IsNullOrEmpty(sup.ContactPerson) && sup.ContactPerson.ToLower().Contains(s)) ||
                    (!string.IsNullOrEmpty(sup.Phone) && sup.Phone.ToLower().Contains(s)) ||
                    (!string.IsNullOrEmpty(sup.Email) && sup.Email.ToLower().Contains(s)) ||
                    (!string.IsNullOrEmpty(sup.Gstin) && sup.Gstin.ToLower().Contains(s))
                ).ToList();
            }

            res.SummaryStats = stats;
            res.TotalCount = supList.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = supList.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var sup in paged)
            {
                res.Rows.Add(new ReportRowItem
                {
                    Id = sup.Id,
                    PrimaryText = sup.DisplayVendorName,
                    SecondaryText = sup.DisplayCompanyName,
                    DateString = sup.CreatedDate.ToIstString("yyyy-MM-dd"),
                    CustomerInfo = !string.IsNullOrEmpty(sup.Phone) ? $"{sup.Phone} ({sup.ContactPerson})" : sup.ContactPerson,
                    BadgeText = sup.Gstin ?? "-",
                    GrandTotal = sup.OutstandingPayable
                });
            }
        }

        private async Task BuildSalesReportAsync(ReportFilterRequest req, ReportResultData res, DateTime start, DateTime end, List<Product> products, Dictionary<string, string> catDict, ReportSummaryStats stats)
        {
            res.ReportTitle = "Detailed Sales & POS Revenue Report";
            res.Headers = new List<string> { "#", "Invoice #", "Date & Time (IST)", "Customer", "Items / IMEIs Purchased", "Gross Amount", "Discount", "GST Tax", "Refunded", "Net Amount", "Status", "Cashier / User" };

            var (sales, totalCount) = await _saleRepository.GetFilteredSalesAsync(
                req.SearchTerm, customerName: null, start, end, cashier: req.EmployeeId, 1, 50000);

            var salesList = sales.ToList();

            if (!string.IsNullOrWhiteSpace(req.PaymentStatus))
            {
                salesList = salesList.Where(s => string.Equals(s.PaymentStatus, req.PaymentStatus, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(req.ReturnStatus) && req.ReturnStatus != "All")
            {
                salesList = salesList.Where(s => string.Equals(s.ReturnStatus, req.ReturnStatus, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (!string.IsNullOrWhiteSpace(req.InvoiceNumber))
            {
                salesList = salesList.Where(s => s.InvoiceNumber.ToLower().Contains(req.InvoiceNumber.Trim().ToLower())).ToList();
            }
            if (!string.IsNullOrWhiteSpace(req.IMEI))
            {
                salesList = salesList.Where(s => s.Items.Any(i => (i.IMEI1 != null && i.IMEI1.Contains(req.IMEI)) || (i.IMEI2 != null && i.IMEI2.Contains(req.IMEI)))).ToList();
            }
            if (!string.IsNullOrWhiteSpace(req.ProductId))
            {
                salesList = salesList.Where(s => s.Items.Any(i => i.ProductId == req.ProductId)).ToList();
            }

            stats.GrossSales = salesList.Sum(s => s.GrandTotal);
            stats.TotalReturnsAmount = salesList.Sum(s => s.TotalRefundedAmount);
            stats.NetRevenue = System.Math.Max(0m, stats.GrossSales - stats.TotalReturnsAmount);
            stats.TotalSalesRevenue = stats.NetRevenue;
            stats.TotalOrders = salesList.Count;
            stats.CompletedInvoicesCount = salesList.Count(s => s.ReturnStatus != "Fully Returned");
            stats.PartiallyReturnedCount = salesList.Count(s => s.ReturnStatus == "Partially Returned");
            stats.FullyReturnedCount = salesList.Count(s => s.ReturnStatus == "Fully Returned");
            stats.TotalItemsSold = salesList.Sum(s => s.Items.Sum(i => i.Quantity - i.ReturnedQuantity));
            stats.TotalDiscounts = salesList.Sum(s => s.Discount + s.ExchangeDiscount);
            stats.TotalGstTax = salesList.Sum(s => s.GstAmount);
            stats.AverageOrderValue = stats.TotalOrders > 0 ? stats.NetRevenue / stats.TotalOrders : 0.0m;
            res.SummaryStats = stats;

            res.TotalCount = salesList.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = salesList.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var s in paged)
            {
                var itemsSummary = string.Join(", ", s.Items.Select(i => $"{i.ProductName}" + (!string.IsNullOrWhiteSpace(i.IMEI1) ? $" (IMEI: {i.IMEI1})" : $" x{i.Quantity}") + (i.IsReturned ? " [RETURNED]" : "")));

                string badgeText = s.ReturnStatus != "None" ? s.ReturnStatus : s.PaymentStatus;
                string badgeClass = s.ReturnStatus == "Fully Returned" ? "bg-danger" : (s.ReturnStatus == "Partially Returned" ? "bg-warning text-dark" : "bg-success");

                res.Rows.Add(new ReportRowItem
                {
                    Id = s.Id,
                    PrimaryText = s.InvoiceNumber,
                    DateString = s.Date.ToIstString("yyyy-MM-dd HH:mm IST"),
                    CustomerInfo = string.IsNullOrWhiteSpace(s.CustomerName) ? "Walk-in Customer" : s.CustomerName,
                    SecondaryText = itemsSummary,
                    SubTotal = s.SubTotal,
                    Discount = s.Discount + s.ExchangeDiscount,
                    TaxAmount = s.GstAmount,
                    GrandTotal = s.GrandTotal,
                    RefundedAmount = s.TotalRefundedAmount,
                    NetAmount = s.NetTotal,
                    ReturnStatus = s.ReturnStatus,
                    BadgeText = badgeText,
                    BadgeClass = badgeClass,
                    UserExecutor = string.IsNullOrWhiteSpace(s.CreatedBy) ? "System" : s.CreatedBy
                });
            }
        }

        private void BuildInventoryReport(ReportFilterRequest req, ReportResultData res, List<Product> products, Dictionary<string, string> catDict, ReportSummaryStats stats)
        {
            res.ReportTitle = "Complete Inventory Stock Valuation Statement";
            res.Headers = new List<string> { "#", "SKU Code", "Product Name", "Category", "Current Stock", "Min Stock", "Stock Status", "Purchase Cost", "Selling Price", "Cost Valuation", "Sales Value", "Profit Potential" };

            var filtered = products.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(req.SearchTerm))
            {
                var s = req.SearchTerm.Trim().ToLower();
                filtered = filtered.Where(p => p.Name.ToLower().Contains(s) || p.Code.ToLower().Contains(s));
            }
            if (!string.IsNullOrWhiteSpace(req.CategoryId))
            {
                filtered = filtered.Where(p => p.CategoryId == req.CategoryId);
            }

            var prodList = filtered.ToList();
            res.SummaryStats = stats;
            res.TotalCount = prodList.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = prodList.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var p in paged)
            {
                var catName = !string.IsNullOrEmpty(p.CategoryId) && catDict.TryGetValue(p.CategoryId, out var cn) ? cn : "General";
                var status = p.CurrentStock == 0 ? "Out of Stock" : (p.CurrentStock <= p.MinimumStock ? "Low Stock" : "Healthy");

                res.Rows.Add(new ReportRowItem
                {
                    Id = p.Id,
                    PrimaryText = p.Code,
                    SecondaryText = p.Name,
                    CategoryName = catName,
                    StockQty = p.CurrentStock,
                    MinStockQty = p.MinimumStock,
                    BadgeText = status,
                    BadgeClass = status == "Healthy" ? "bg-success" : (status == "Low Stock" ? "bg-warning text-dark" : "bg-danger"),
                    CostPrice = p.PurchasePrice,
                    SellingPrice = p.SellingPrice,
                    CostValuation = p.CurrentStock * p.PurchasePrice,
                    SalesValuation = p.CurrentStock * p.SellingPrice,
                    ProfitPotential = p.CurrentStock * (p.SellingPrice - p.PurchasePrice)
                });
            }
        }

        private async Task BuildStockMovementReportAsync(ReportFilterRequest req, ReportResultData res, DateTime start, DateTime end, List<Product> products, Dictionary<string, string> catDict, ReportSummaryStats stats)
        {
            res.ReportTitle = "Stock Movement Audit History Report";
            res.Headers = new List<string> { "#", "Timestamp (IST)", "Product", "Type", "Quantity", "Reason", "Prev Stock", "New Stock", "Executed By" };

            var (txs, totalCount) = await _stockTransactionRepository.GetFilteredTransactionsAsync(
                req.SearchTerm, req.TransactionType, req.ProductId, null, start, end, req.EmployeeId, 1, 50000);

            var list = txs.ToList();
            res.SummaryStats = stats;
            res.TotalCount = list.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = list.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var t in paged)
            {
                res.Rows.Add(new ReportRowItem
                {
                    Id = t.Id,
                    DateString = t.Timestamp.ToIstString("yyyy-MM-dd HH:mm IST"),
                    PrimaryText = t.ProductName,
                    SecondaryText = t.Reason,
                    BadgeText = t.Type,
                    BadgeClass = t.Type == "Stock In" ? "bg-success" : (t.Type == "Stock Out" ? "bg-danger" : "bg-info text-dark"),
                    StockQty = t.Quantity,
                    UserExecutor = t.ExecutedBy
                });
            }
        }

        private async Task BuildEmployeeActivityReportAsync(ReportFilterRequest req, ReportResultData res, DateTime start, DateTime end, ReportSummaryStats stats)
        {
            res.ReportTitle = "Employee System Audit Trail Report";
            res.Headers = new List<string> { "#", "Timestamp (IST)", "User", "Module", "Action", "Target", "Status", "Details" };

            var (logs, _) = await _auditLogRepository.GetFilteredLogsAsync(req.SearchTerm, null, null, null, null, req.EmployeeId, start, end, null, null, null, 1, 50000);
            var logList = logs.ToList();

            res.SummaryStats = stats;
            res.TotalCount = logList.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = logList.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var l in paged)
            {
                res.Rows.Add(new ReportRowItem
                {
                    Id = l.Id,
                    DateString = l.Timestamp.ToIstString("yyyy-MM-dd HH:mm IST"),
                    UserExecutor = l.Username,
                    CategoryName = l.Module,
                    PrimaryText = l.Action,
                    ReferenceInfo = l.Target ?? "-",
                    SecondaryText = l.Details ?? "-",
                    BadgeText = l.Status,
                    BadgeClass = l.Status == "Success" ? "bg-success" : "bg-danger"
                });
            }
        }

        private void BuildAlertsReport(ReportFilterRequest req, ReportResultData res, List<Product> products, Dictionary<string, string> catDict, ReportSummaryStats stats)
        {
            res.ReportTitle = "Low Stock & Out of Stock Exception Report";
            res.Headers = new List<string> { "#", "Product SKU", "Product Name", "Category", "Current Stock", "Min Threshold", "Alert Status" };

            var alertProds = products.Where(p => p.CurrentStock <= p.MinimumStock).ToList();
            res.SummaryStats = stats;
            res.TotalCount = alertProds.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = alertProds.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var p in paged)
            {
                var catName = !string.IsNullOrEmpty(p.CategoryId) && catDict.TryGetValue(p.CategoryId, out var cn) ? cn : "General";
                var status = p.CurrentStock == 0 ? "Out of Stock" : "Low Stock Alert";

                res.Rows.Add(new ReportRowItem
                {
                    Id = p.Id,
                    PrimaryText = p.Code,
                    SecondaryText = p.Name,
                    CategoryName = catName,
                    StockQty = p.CurrentStock,
                    MinStockQty = p.MinimumStock,
                    BadgeText = status,
                    BadgeClass = p.CurrentStock == 0 ? "bg-danger" : "bg-warning text-dark"
                });
            }
        }

        private async Task BuildCategoryReportAsync(ReportFilterRequest req, ReportResultData res, DateTime start, DateTime end, List<Product> products, List<Category> categories, ReportSummaryStats stats)
        {
            res.ReportTitle = "Category Sales & Valuation Distribution Report";
            res.Headers = new List<string> { "#", "Category Name", "Total Products", "Total Inventory Qty", "Cost Valuation", "Retail Value" };

            res.SummaryStats = stats;
            res.TotalCount = categories.Count;
            res.TotalPages = 1;

            foreach (var c in categories)
            {
                var catProds = products.Where(p => p.CategoryId == c.Id).ToList();
                res.Rows.Add(new ReportRowItem
                {
                    Id = c.Id,
                    PrimaryText = c.Name,
                    StockQty = catProds.Count,
                    MinStockQty = catProds.Sum(p => p.CurrentStock),
                    CostValuation = catProds.Sum(p => p.CurrentStock * p.PurchasePrice),
                    SalesValuation = catProds.Sum(p => p.CurrentStock * p.SellingPrice)
                });
            }
        }

        private async Task BuildProductPerformanceReportAsync(ReportFilterRequest req, ReportResultData res, DateTime start, DateTime end, List<Product> products, Dictionary<string, string> catDict, ReportSummaryStats stats)
        {
            res.ReportTitle = "Product Sales Performance Matrix";
            res.Headers = new List<string> { "#", "SKU Code", "Product Name", "Category", "Current Stock", "Selling Price" };

            res.SummaryStats = stats;
            res.TotalCount = products.Count;
            int pages = (int)System.Math.Ceiling((double)res.TotalCount / res.PageSize);
            res.TotalPages = pages < 1 ? 1 : pages;

            var paged = products.Skip((res.Page - 1) * res.PageSize).Take(res.PageSize);

            foreach (var p in paged)
            {
                var catName = !string.IsNullOrEmpty(p.CategoryId) && catDict.TryGetValue(p.CategoryId, out var cn) ? cn : "General";
                res.Rows.Add(new ReportRowItem
                {
                    Id = p.Id,
                    PrimaryText = p.Code,
                    SecondaryText = p.Name,
                    CategoryName = catName,
                    StockQty = p.CurrentStock,
                    SellingPrice = p.SellingPrice
                });
            }
        }

        private async Task BuildExecutiveSummaryReportAsync(ReportFilterRequest req, ReportResultData res, DateTime start, DateTime end, List<Product> products, List<Category> categories, ReportSummaryStats stats)
        {
            res.ReportTitle = "Mobile Shop Executive Overview & Financial Summary";
            res.Headers = new List<string> { "#", "Metric Overview", "Value / Count" };

            res.SummaryStats = stats;
            res.TotalCount = 8;
            res.TotalPages = 1;

            res.Rows.Add(new ReportRowItem { PrimaryText = "Total Active Product Catalog", SecondaryText = products.Count.ToString() });
            res.Rows.Add(new ReportRowItem { PrimaryText = "Total Physical Inventory Units", SecondaryText = products.Sum(p => p.CurrentStock).ToString() });
            res.Rows.Add(new ReportRowItem { PrimaryText = "Total Inventory Purchase Cost Valuation", GrandTotal = stats.CurrentInventoryValue });
            res.Rows.Add(new ReportRowItem { PrimaryText = "Total Inventory Retail Value Potential", GrandTotal = stats.PotentialSalesValue });
            res.Rows.Add(new ReportRowItem { PrimaryText = "Total Estimated Gross Profit Margin", GrandTotal = stats.TotalProfitPotential });
            res.Rows.Add(new ReportRowItem { PrimaryText = "Low Stock Alert Count", SecondaryText = stats.LowStockCount.ToString() });
            res.Rows.Add(new ReportRowItem { PrimaryText = "Out of Stock Count", SecondaryText = stats.OutOfStockCount.ToString() });
        }

        #endregion

        #region Date Range Utility

        private (DateTime startUtc, DateTime endUtc, string label) ResolveDateRange(string? preset, DateTime? customStart, DateTime? customEnd)
        {
            var nowUtc = DateTime.UtcNow;

            switch (preset?.ToLower())
            {
                case "today":
                    return (nowUtc.Date, nowUtc.Date.AddDays(1).AddTicks(-1), "Today");

                case "yesterday":
                    var yest = nowUtc.Date.AddDays(-1);
                    return (yest, yest.AddDays(1).AddTicks(-1), "Yesterday");

                case "thisweek":
                    int diff = (7 + (nowUtc.DayOfWeek - DayOfWeek.Monday)) % 7;
                    var weekStart = nowUtc.Date.AddDays(-1 * diff);
                    return (weekStart, nowUtc, "This Week");

                case "thismonth":
                    var monthStart = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    return (monthStart, nowUtc, "This Month");

                case "lastmonth":
                    var lmEnd = new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1);
                    var lmStart = new DateTime(lmEnd.Year, lmEnd.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                    return (lmStart, lmEnd, "Last Month");

                case "thisyear":
                    var yrStart = new DateTime(nowUtc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    return (yrStart, nowUtc, "This Year");

                case "custom":
                    var s = customStart ?? nowUtc.AddDays(-30);
                    var e = customEnd ?? nowUtc;
                    return (s, e, $"{s:yyyy-MM-dd} to {e:yyyy-MM-dd}");

                case "alltime":
                default:
                    return (DateTime.MinValue, DateTime.MaxValue, "All Time");
            }
        }

        #endregion

        #region Export Generator Methods (Excel, PDF & CSV)

        public static List<string> GetRowValues(ReportRowItem r, string reportType, int rowNumber)
        {
            var type = (reportType ?? "").Trim().ToLower();
            var list = new List<string>();

            switch (type)
            {
                case "sales":
                    // Headers: "#", "Invoice #", "Date & Time (IST)", "Customer", "Items / IMEIs Purchased", "Gross Amount", "Discount", "GST Tax", "Refunded", "Net Amount", "Status", "Cashier / User"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.DateString) ? "-" : r.DateString);
                    list.Add(string.IsNullOrWhiteSpace(r.CustomerInfo) ? "-" : r.CustomerInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.SecondaryText) ? "-" : r.SecondaryText);
                    list.Add($"₹{r.GrandTotal:N2}");
                    list.Add($"₹{r.Discount:N2}");
                    list.Add($"₹{r.TaxAmount:N2}");
                    list.Add($"₹{r.RefundedAmount:N2}");
                    list.Add($"₹{r.NetAmount:N2}");
                    list.Add(string.IsNullOrWhiteSpace(r.BadgeText) ? "-" : r.BadgeText);
                    list.Add(string.IsNullOrWhiteSpace(r.UserExecutor) ? "-" : r.UserExecutor);
                    break;

                case "imeiinventory":
                case "device":
                    // Headers: "#", "IMEI 1", "IMEI 2", "Brand & Model", "Variant / Color", "Status", "Purchase Cost", "Selling Price", "Invoice #", "Customer Info", "Stock Date"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.SecondaryText) ? "-" : r.SecondaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.ProductInfo) ? "-" : r.ProductInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.CategoryName) ? "-" : r.CategoryName);
                    list.Add(string.IsNullOrWhiteSpace(r.BadgeText) ? "-" : r.BadgeText);
                    list.Add($"₹{r.CostPrice:N2}");
                    list.Add($"₹{r.SellingPrice:N2}");
                    list.Add(string.IsNullOrWhiteSpace(r.ReferenceInfo) ? "-" : r.ReferenceInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.CustomerInfo) ? "-" : r.CustomerInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.DateString) ? "-" : r.DateString);
                    break;

                case "tradein":
                case "exchange":
                    // Headers: "#", "Exchange #", "Date & Time (IST)", "Customer", "Old Phone Brand / Model", "Old IMEI", "Condition", "Exchange Value", "Offset Invoice", "Executed By"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.DateString) ? "-" : r.DateString);
                    list.Add(string.IsNullOrWhiteSpace(r.CustomerInfo) ? "-" : r.CustomerInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.ProductInfo) ? "-" : r.ProductInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.SecondaryText) ? "-" : r.SecondaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.BadgeText) ? "-" : r.BadgeText);
                    list.Add($"₹{r.GrandTotal:N2}");
                    list.Add(string.IsNullOrWhiteSpace(r.ReferenceInfo) ? "-" : r.ReferenceInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.UserExecutor) ? "-" : r.UserExecutor);
                    break;

                case "returns":
                case "return":
                    // Headers: "#", "Return #", "Invoice #", "Date & Time (IST)", "Customer", "Product / Device", "IMEI", "Reason", "Restock Destination", "Refund Amount", "Processed By"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.ReferenceInfo) ? "-" : r.ReferenceInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.DateString) ? "-" : r.DateString);
                    list.Add(string.IsNullOrWhiteSpace(r.CustomerInfo) ? "-" : r.CustomerInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.ProductInfo) ? "-" : r.ProductInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.SecondaryText) ? "-" : r.SecondaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.Reason) ? "-" : r.Reason);
                    list.Add(string.IsNullOrWhiteSpace(r.BadgeText) ? "-" : r.BadgeText);
                    list.Add($"₹{r.RefundedAmount:N2}");
                    list.Add(string.IsNullOrWhiteSpace(r.UserExecutor) ? "-" : r.UserExecutor);
                    break;

                case "purchasereturns":
                case "purchasereturn":
                    // Headers: "#", "Return #", "PO Number", "Date & Time (IST)", "Supplier", "Reason", "Resolution", "Total Quantity", "Return Value", "Status", "Created By"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.ReferenceInfo) ? "-" : r.ReferenceInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.DateString) ? "-" : r.DateString);
                    list.Add(string.IsNullOrWhiteSpace(r.CustomerInfo) ? "-" : r.CustomerInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.Reason) ? "-" : r.Reason);
                    list.Add(string.IsNullOrWhiteSpace(r.CategoryName) ? "-" : r.CategoryName);
                    list.Add($"{r.StockQty} Units");
                    list.Add($"₹{r.GrandTotal:N2}");
                    list.Add(string.IsNullOrWhiteSpace(r.BadgeText) ? "-" : r.BadgeText);
                    list.Add(string.IsNullOrWhiteSpace(r.UserExecutor) ? "-" : r.UserExecutor);
                    break;

                case "repairs":
                case "repair":
                    // Headers: "#", "Ticket #", "Date & Time (IST)", "Customer", "Device Model", "IMEI", "Problem Description", "Technician", "Estimated Cost", "Final Cost", "Status"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.DateString) ? "-" : r.DateString);
                    list.Add(string.IsNullOrWhiteSpace(r.CustomerInfo) ? "-" : r.CustomerInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.ProductInfo) ? "-" : r.ProductInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.SecondaryText) ? "-" : r.SecondaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.Reason) ? "-" : r.Reason);
                    list.Add(string.IsNullOrWhiteSpace(r.UserExecutor) ? "-" : r.UserExecutor);
                    list.Add($"₹{r.CostPrice:N2}");
                    list.Add($"₹{r.GrandTotal:N2}");
                    list.Add(string.IsNullOrWhiteSpace(r.BadgeText) ? "-" : r.BadgeText);
                    break;

                case "warranty":
                    // Headers: "#", "IMEI 1", "Brand & Model", "Customer Name", "Customer Phone", "Sold Date", "Warranty End Date", "Warranty Status"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.ProductInfo) ? "-" : r.ProductInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.CustomerInfo) ? "-" : r.CustomerInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.SecondaryText) ? "-" : r.SecondaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.DateString) ? "-" : r.DateString);
                    list.Add(string.IsNullOrWhiteSpace(r.ReferenceInfo) ? "-" : r.ReferenceInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.BadgeText) ? "-" : r.BadgeText);
                    break;

                case "customer":
                    // Headers: "#", "Customer Name", "Phone", "Email", "GSTIN", "Total Purchases", "Outstanding Balance", "Created Date"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.SecondaryText) ? "-" : r.SecondaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.CustomerInfo) ? "-" : r.CustomerInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.BadgeText) ? "-" : r.BadgeText);
                    list.Add($"₹{r.GrandTotal:N2}");
                    list.Add($"₹{r.SubTotal:N2}");
                    list.Add(string.IsNullOrWhiteSpace(r.DateString) ? "-" : r.DateString);
                    break;

                case "supplier":
                    // Headers: "#", "Vendor / Brand Name", "Company / Legal Name", "Date Added", "Phone / Contact", "Payables", "GSTIN"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.SecondaryText) ? "-" : r.SecondaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.DateString) ? "-" : r.DateString);
                    list.Add(string.IsNullOrWhiteSpace(r.CustomerInfo) ? "-" : r.CustomerInfo);
                    list.Add($"₹{r.GrandTotal:N2}");
                    list.Add(string.IsNullOrWhiteSpace(r.BadgeText) ? "-" : r.BadgeText);
                    break;

                case "inventory":
                    // Headers: "#", "SKU Code", "Product Name", "Category", "Current Stock", "Min Stock", "Stock Status", "Purchase Cost", "Selling Price", "Cost Valuation", "Sales Value", "Profit Potential"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.SecondaryText) ? "-" : r.SecondaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.CategoryName) ? "-" : r.CategoryName);
                    list.Add(r.StockQty.ToString());
                    list.Add(r.MinStockQty.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.BadgeText) ? "-" : r.BadgeText);
                    list.Add($"₹{r.CostPrice:N2}");
                    list.Add($"₹{r.SellingPrice:N2}");
                    list.Add($"₹{r.CostValuation:N2}");
                    list.Add($"₹{r.SalesValuation:N2}");
                    list.Add($"₹{r.ProfitPotential:N2}");
                    break;

                case "stockmovement":
                case "stockhistory":
                    // Headers: "#", "Timestamp (IST)", "Product", "Type", "Quantity", "Reason", "Prev Stock", "New Stock", "Executed By"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.DateString) ? "-" : r.DateString);
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.BadgeText) ? "-" : r.BadgeText);
                    list.Add((r.StockQty > 0 ? "+" : "") + r.StockQty.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.SecondaryText) ? "-" : r.SecondaryText);
                    list.Add(r.PreviousStock.ToString());
                    list.Add(r.NewStock.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.UserExecutor) ? "-" : r.UserExecutor);
                    break;

                case "employeeactivity":
                    // Headers: "#", "Timestamp (IST)", "User", "Module", "Action", "Target", "Status", "Details"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.DateString) ? "-" : r.DateString);
                    list.Add(string.IsNullOrWhiteSpace(r.UserExecutor) ? "-" : r.UserExecutor);
                    list.Add(string.IsNullOrWhiteSpace(r.CategoryName) ? "-" : r.CategoryName);
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.ReferenceInfo) ? "-" : r.ReferenceInfo);
                    list.Add(string.IsNullOrWhiteSpace(r.BadgeText) ? "-" : r.BadgeText);
                    list.Add(string.IsNullOrWhiteSpace(r.SecondaryText) ? "-" : r.SecondaryText);
                    break;

                case "alerts":
                    // Headers: "#", "Product SKU", "Product Name", "Category", "Current Stock", "Min Threshold", "Alert Status"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.SecondaryText) ? "-" : r.SecondaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.CategoryName) ? "-" : r.CategoryName);
                    list.Add(r.StockQty.ToString());
                    list.Add(r.MinStockQty.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.BadgeText) ? "-" : r.BadgeText);
                    break;

                case "category":
                    // Headers: "#", "Category Name", "Total Products", "Total Inventory Qty", "Cost Valuation", "Retail Value"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(r.StockQty.ToString());
                    list.Add(r.MinStockQty.ToString());
                    list.Add($"₹{r.CostValuation:N2}");
                    list.Add($"₹{r.SalesValuation:N2}");
                    break;

                case "productperformance":
                    // Headers: "#", "SKU Code", "Product Name", "Category", "Current Stock", "Selling Price"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.SecondaryText) ? "-" : r.SecondaryText);
                    list.Add(string.IsNullOrWhiteSpace(r.CategoryName) ? "-" : r.CategoryName);
                    list.Add(r.StockQty.ToString());
                    list.Add($"₹{r.SellingPrice:N2}");
                    break;

                case "executivesummary":
                    // Headers: "#", "Metric Overview", "Value / Count"
                    list.Add(rowNumber.ToString());
                    list.Add(string.IsNullOrWhiteSpace(r.PrimaryText) ? "-" : r.PrimaryText);
                    list.Add(!string.IsNullOrWhiteSpace(r.SecondaryText) ? r.SecondaryText : (r.GrandTotal > 0 ? $"₹{r.GrandTotal:N2}" : "-"));
                    break;

                default:
                    list.Add(rowNumber.ToString());
                    if (!string.IsNullOrWhiteSpace(r.PrimaryText)) list.Add(r.PrimaryText);
                    if (!string.IsNullOrWhiteSpace(r.SecondaryText)) list.Add(r.SecondaryText);
                    if (!string.IsNullOrWhiteSpace(r.DateString)) list.Add(r.DateString);
                    if (!string.IsNullOrWhiteSpace(r.CustomerInfo)) list.Add(r.CustomerInfo);
                    if (!string.IsNullOrWhiteSpace(r.ProductInfo)) list.Add(r.ProductInfo);
                    if (r.GrandTotal > 0) list.Add($"₹{r.GrandTotal:N2}");
                    if (!string.IsNullOrWhiteSpace(r.BadgeText)) list.Add(r.BadgeText);
                    if (!string.IsNullOrWhiteSpace(r.UserExecutor)) list.Add(r.UserExecutor);
                    break;
            }

            return list;
        }

        private static bool IsRightAlignedHeader(string header)
        {
            if (string.IsNullOrWhiteSpace(header)) return false;
            var h = header.Trim().ToLower();
            return h.Contains("amount") ||
                   h.Contains("price") ||
                   h.Contains("cost") ||
                   h.Contains("valuation") ||
                   h.Contains("value") ||
                   h.Contains("tax") ||
                   h.Contains("discount") ||
                   h.Contains("refund") ||
                   h.Contains("margin") ||
                   h.Contains("payables") ||
                   h.Contains("balance") ||
                   h.Contains("total purchases") ||
                   h.Contains("gross") ||
                   h.Contains("net") ||
                   h.Contains("quantity") ||
                   h.Contains("stock qty") ||
                   h.Contains("min stock") ||
                   h.Contains("units") ||
                   h.Contains("threshold");
        }

        public byte[] GenerateExcelReport(ReportResultData data)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("SIMS Report");

            // Header Banner
            worksheet.Cell(1, 1).Value = "SIMS - " + data.ReportTitle;
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 14;
            worksheet.Cell(1, 1).Style.Font.FontColor = XLColor.FromHtml("#1E3A8A");

            worksheet.Cell(2, 1).Value = $"Filters / Scope: {data.AppliedFiltersText} | Generated: {data.GeneratedAt.ToIstString("yyyy-MM-dd HH:mm IST")} | Total Records: {data.TotalCount}";
            worksheet.Cell(2, 1).Style.Font.Italic = true;
            worksheet.Cell(2, 1).Style.Font.FontSize = 9;
            worksheet.Cell(2, 1).Style.Font.FontColor = XLColor.FromHtml("#475569");

            int headerRow = 4;
            for (int c = 0; c < data.Headers.Count; c++)
            {
                var cell = worksheet.Cell(headerRow, c + 1);
                cell.Value = data.Headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontSize = 10;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E293B");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                if (IsRightAlignedHeader(data.Headers[c]))
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                else if (data.Headers[c] == "#")
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                else
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                cell.Style.Border.BottomBorder = XLBorderStyleValues.Medium;
                cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#0F172A");
            }

            int rowIdx = 5;
            int count = 1;
            foreach (var r in data.Rows)
            {
                var rowValues = GetRowValues(r, data.ReportType, count++);
                var isEven = (rowIdx % 2 == 0);
                var rowBg = isEven ? XLColor.FromHtml("#F8FAFC") : XLColor.White;

                for (int c = 0; c < data.Headers.Count; c++)
                {
                    var cell = worksheet.Cell(rowIdx, c + 1);
                    var val = c < rowValues.Count ? rowValues[c] : "-";
                    cell.Value = val;
                    cell.Style.Fill.BackgroundColor = rowBg;
                    cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#E2E8F0");
                    cell.Style.Font.FontSize = 9.5;

                    if (IsRightAlignedHeader(data.Headers[c]))
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    else if (data.Headers[c] == "#")
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    else
                        cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                }

                rowIdx++;
            }

            worksheet.Columns().AdjustToContents(5, 50);
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public byte[] GeneratePdfReport(ReportResultData data)
        {
            if (data.Headers == null || data.Headers.Count == 0)
            {
                data.Headers = new List<string> { "#", "Reference", "Date", "Party / Entity", "Details / Items", "Amount", "Status", "Executor" };
            }

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(20);
                    page.Size(PageSizes.A4.Landscape());
                    page.DefaultTextStyle(x => x.FontSize(7.5f).FontFamily("Arial"));

                    page.Header().Column(headerCol =>
                    {
                        // Company & Report Title Bar
                        headerCol.Item().Row(r =>
                        {
                            r.RelativeItem(3).Column(c =>
                            {
                                c.Item().Text("SIMS | SMART INVENTORY & POS SYSTEM")
                                    .FontSize(13).Bold().FontColor("#1E3A8A");
                                c.Item().Text("Mobile Electronics & Hardware Management • Enterprise Operations")
                                    .FontSize(7.5f).FontColor("#64748B");
                            });

                            r.RelativeItem(2).Column(c =>
                            {
                                c.Item().AlignRight().Text("OFFICIAL SYSTEM REPORT").FontSize(8).Bold().FontColor("#1E293B");
                                c.Item().AlignRight().Text($"Generated: {data.GeneratedAt.ToIstString("dd-MMM-yyyy, HH:mm IST")}")
                                    .FontSize(7.5f).FontColor("#475569");
                                c.Item().AlignRight().Text($"Total Records: {data.TotalCount:N0}")
                                    .FontSize(7.5f).Bold().FontColor("#2563EB");
                            });
                        });

                        headerCol.Item().PaddingTop(5).PaddingBottom(3).Background("#0F172A").Padding(5).Row(r =>
                        {
                            r.RelativeItem().Column(c =>
                            {
                                c.Item().Text(data.ReportTitle.ToUpper()).FontSize(10f).Bold().FontColor("#FFFFFF");
                                c.Item().Text($"Scope: {data.AppliedFiltersText}").FontSize(7f).Italic().FontColor("#94A3B8");
                            });
                        });

                        // Executive KPI Stats Banner
                        var stats = data.SummaryStats;
                        if (stats != null && (stats.GrossSales > 0 || stats.CurrentInventoryValue > 0 || stats.TotalReturnsAmount > 0 || data.TotalCount > 0))
                        {
                            decimal kpiGross = stats.GrossSales > 0 ? stats.GrossSales : (stats.CurrentInventoryValue > 0 ? stats.CurrentInventoryValue : 0);
                            decimal kpiRefunds = stats.TotalReturnsAmount;
                            decimal kpiNet = stats.NetRevenue > 0 ? stats.NetRevenue : (stats.SellableInventoryValue > 0 ? stats.SellableInventoryValue : (kpiGross - kpiRefunds));
                            int kpiCount = stats.TotalOrders > 0 ? stats.TotalOrders : (stats.TotalInventoryQty > 0 ? stats.TotalInventoryQty : data.TotalCount);

                            string label1 = stats.GrossSales > 0 ? "GROSS SALES" : "INVENTORY COST VALUE";
                            string label2 = "REFUNDS / RETURNS";
                            string label3 = stats.NetRevenue > 0 ? "NET REVENUE" : "SELLABLE VALUATION";
                            string label4 = stats.TotalOrders > 0 ? "TOTAL ORDERS" : "TOTAL STOCK UNITS";

                            headerCol.Item().PaddingTop(5).PaddingBottom(3).Row(r =>
                            {
                                r.RelativeItem().Background("#F8FAFC").Border(0.5f).BorderColor("#CBD5E1").Padding(4).Column(c =>
                                {
                                    c.Item().Text(label1).FontSize(6.5f).Bold().FontColor("#64748B");
                                    c.Item().Text($"₹{kpiGross:N2}").FontSize(9f).Bold().FontColor("#1E3A8A");
                                });

                                r.RelativeItem().PaddingLeft(4).Background("#F8FAFC").Border(0.5f).BorderColor("#CBD5E1").Padding(4).Column(c =>
                                {
                                    c.Item().Text(label2).FontSize(6.5f).Bold().FontColor("#64748B");
                                    c.Item().Text($"₹{kpiRefunds:N2}").FontSize(9f).Bold().FontColor("#DC2626");
                                });

                                r.RelativeItem().PaddingLeft(4).Background("#F8FAFC").Border(0.5f).BorderColor("#CBD5E1").Padding(4).Column(c =>
                                {
                                    c.Item().Text(label3).FontSize(6.5f).Bold().FontColor("#64748B");
                                    c.Item().Text($"₹{kpiNet:N2}").FontSize(9f).Bold().FontColor("#16A34A");
                                });

                                r.RelativeItem().PaddingLeft(4).Background("#F8FAFC").Border(0.5f).BorderColor("#CBD5E1").Padding(4).Column(c =>
                                {
                                    c.Item().Text(label4).FontSize(6.5f).Bold().FontColor("#64748B");
                                    c.Item().Text($"{kpiCount:N0}").FontSize(9f).Bold().FontColor("#0F172A");
                                });
                            });
                        }

                        headerCol.Item().PaddingTop(3).LineHorizontal(0.5f).LineColor("#CBD5E1");
                    });

                    page.Content().PaddingTop(4).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            for (int i = 0; i < data.Headers.Count; i++)
                            {
                                var h = data.Headers[i].ToLower();
                                if (h == "#")
                                    cols.ConstantColumn(22);
                                else if (h.Contains("invoice") || h.Contains("ticket") || h.Contains("return #") || h.Contains("po number") || h.Contains("exchange #") || h.Contains("sku"))
                                    cols.RelativeColumn(1.3f);
                                else if (h.Contains("imei"))
                                    cols.RelativeColumn(1.3f);
                                else if (h.Contains("date") || h.Contains("timestamp"))
                                    cols.RelativeColumn(1.3f);
                                else if (h.Contains("customer") || h.Contains("supplier") || h.Contains("vendor"))
                                    cols.RelativeColumn(1.4f);
                                else if (h.Contains("item") || h.Contains("product") || h.Contains("details") || h.Contains("description") || h.Contains("reason"))
                                    cols.RelativeColumn(2.0f);
                                else if (h.Contains("status") || h.Contains("condition") || h.Contains("type") || h.Contains("action"))
                                    cols.RelativeColumn(1.0f);
                                else if (h.Contains("user") || h.Contains("cashier") || h.Contains("technician") || h.Contains("processed") || h.Contains("created by"))
                                    cols.RelativeColumn(1.1f);
                                else if (h.Contains("amount") || h.Contains("price") || h.Contains("total") || h.Contains("cost") || h.Contains("value") || h.Contains("tax") || h.Contains("discount") || h.Contains("refund"))
                                    cols.RelativeColumn(1.1f);
                                else
                                    cols.RelativeColumn(1.0f);
                            }
                        });

                        table.Header(h =>
                        {
                            for (int c = 0; c < data.Headers.Count; c++)
                            {
                                var header = data.Headers[c];
                                bool isRight = IsRightAlignedHeader(header);
                                var cell = h.Cell()
                                    .Background("#1E293B")
                                    .BorderBottom(1.5f).BorderColor("#0F172A")
                                    .PaddingVertical(4)
                                    .PaddingHorizontal(3);

                                if (header == "#") cell = cell.AlignCenter();
                                else if (isRight) cell = cell.AlignRight();
                                else cell = cell.AlignLeft();

                                cell.Text(header)
                                    .FontSize(7f)
                                    .Bold()
                                    .FontColor("#FFFFFF");
                            }
                        });

                        int rowIdx = 1;
                        foreach (var row in data.Rows)
                        {
                            var rowValues = GetRowValues(row, data.ReportType, rowIdx);
                            var bg = rowIdx % 2 == 0 ? "#F8FAFC" : "#FFFFFF";

                            for (int colIdx = 0; colIdx < data.Headers.Count; colIdx++)
                            {
                                var val = colIdx < rowValues.Count ? (rowValues[colIdx] ?? "-") : "-";
                                var header = data.Headers[colIdx];
                                bool isRight = IsRightAlignedHeader(header);

                                var cell = table.Cell()
                                    .Background(bg)
                                    .BorderBottom(0.5f).BorderColor("#E2E8F0")
                                    .PaddingVertical(3.5f)
                                    .PaddingHorizontal(3);

                                if (header == "#") cell = cell.AlignCenter();
                                else if (isRight) cell = cell.AlignRight();
                                else cell = cell.AlignLeft();

                                var text = cell.Text(val).FontSize(7f);

                                if (header == "#")
                                    text.FontColor("#64748B");
                                else if (header.Contains("Invoice") || header.Contains("Ticket") || header.Contains("Return #") || header.Contains("PO Number"))
                                    text.Bold().FontColor("#1E3A8A");
                                else if (header.Contains("Net Amount") || header.Contains("Profit Potential"))
                                    text.Bold().FontColor("#15803D");
                                else if (header.Contains("Refunded") || header.Contains("Refund Amount") || val.Contains("Returned") || val == "Out of Stock")
                                    text.Bold().FontColor("#DC2626");
                                else if (isRight && val.StartsWith("₹"))
                                    text.FontColor("#0F172A");
                                else
                                    text.FontColor("#334155");
                            }

                            rowIdx++;
                        }
                    });

                    page.Footer().Column(f =>
                    {
                        f.Item().LineHorizontal(0.5f).LineColor("#CBD5E1");
                        f.Item().PaddingTop(3).Row(r =>
                        {
                            r.RelativeItem().Text("SIMS Mobile POS & Smart Inventory ERP • Confidential System Document")
                                .FontSize(6.5f).FontColor("#94A3B8");
                            r.RelativeItem().AlignRight().Text(x =>
                            {
                                x.Span("Page ").FontSize(7f).FontColor("#64748B");
                                x.CurrentPageNumber().FontSize(7f).Bold().FontColor("#1E293B");
                                x.Span(" of ").FontSize(7f).FontColor("#64748B");
                                x.TotalPages().FontSize(7f).Bold().FontColor("#1E293B");
                            });
                        });
                    });
                });
            }).GeneratePdf();
        }

        public byte[] GenerateCsvReport(ReportResultData data)
        {
            var sb = new StringBuilder();

            // Report Header Metadata
            sb.AppendLine($"\"SIMS ENTERPRISE REPORT\",{EscapeCsv(data.ReportTitle)}");
            sb.AppendLine($"\"Generated At\",\"{data.GeneratedAt.ToIstString("yyyy-MM-dd HH:mm IST")}\"");
            sb.AppendLine($"\"Applied Filters\",{EscapeCsv(data.AppliedFiltersText)}");
            int totalRecords = data.TotalCount > 0 ? data.TotalCount : data.Rows.Count;
            sb.AppendLine($"\"Total Records\",\"{totalRecords}\"");
            sb.AppendLine();

            // Column Headers
            sb.AppendLine(string.Join(",", data.Headers.Select(EscapeCsv)));

            // Data Rows
            int rowCount = 1;
            foreach (var row in data.Rows)
            {
                var rowValues = GetRowValues(row, data.ReportType, rowCount++);
                sb.AppendLine(string.Join(",", rowValues.Select(EscapeCsv)));
            }

            // UTF-8 with BOM for Excel compatibility
            var preamble = Encoding.UTF8.GetPreamble();
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var result = new byte[preamble.Length + bytes.Length];
            Buffer.BlockCopy(preamble, 0, result, 0, preamble.Length);
            Buffer.BlockCopy(bytes, 0, result, preamble.Length, bytes.Length);
            return result;
        }

        private static string EscapeCsv(string? val)
        {
            if (string.IsNullOrEmpty(val)) return "\"\"";
            var clean = val.Replace("\"", "\"\"");
            return $"\"{clean}\"";
        }

        public byte[] GenerateSalesExcelReport(DateTime start, DateTime end, IEnumerable<Sale> sales)
        {
            var req = new ReportFilterRequest { ReportType = "Sales", StartDate = start, EndDate = end };
            var data = BuildReportDataAsync(req).GetAwaiter().GetResult();
            return GenerateExcelReport(data);
        }

        public byte[] GenerateInventoryValuationExcelReport(IEnumerable<Product> products, Dictionary<string, string> categoryNames)
        {
            var req = new ReportFilterRequest { ReportType = "Inventory" };
            var data = BuildReportDataAsync(req).GetAwaiter().GetResult();
            return GenerateExcelReport(data);
        }

        public byte[] GenerateInventoryPdfReport(IEnumerable<Product> products, Dictionary<string, string> categoryNames)
        {
            var req = new ReportFilterRequest { ReportType = "Inventory" };
            var data = BuildReportDataAsync(req).GetAwaiter().GetResult();
            return GeneratePdfReport(data);
        }

        #endregion
    }
}
