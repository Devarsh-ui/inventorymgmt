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

            var categories = (await _categoryRepository.GetAllAsync()).ToList();
            var categoryDict = categories.ToDictionary(c => c.Id, c => c.Name);
            var products = (await _productRepository.GetAllAsync()).ToList();
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
                    CustomerInfo = r.SupplierName,
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
            res.Headers = new List<string> { "#", "Company Name", "Contact Person", "Phone", "Email", "GSTIN", "Payables", "Created Date" };

            var suppliers = await _supplierRepository.GetAllAsync();
            var supList = suppliers.ToList();

            if (!string.IsNullOrWhiteSpace(req.SearchTerm))
            {
                var s = req.SearchTerm.Trim().ToLower();
                supList = supList.Where(sup =>
                    (sup.CompanyName != null && sup.CompanyName.ToLower().Contains(s)) ||
                    (sup.ContactPerson != null && sup.ContactPerson.ToLower().Contains(s)) ||
                    (sup.Phone != null && sup.Phone.ToLower().Contains(s))
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
                    PrimaryText = sup.CompanyName,
                    SecondaryText = sup.ContactPerson,
                    CustomerInfo = sup.Phone,
                    BadgeText = sup.Gstin ?? "-",
                    GrandTotal = sup.OutstandingPayable,
                    DateString = sup.CreatedDate.ToIstString("yyyy-MM-dd")
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
                    SecondaryText = l.Details,
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

        #region Export Generator Methods (Excel & PDF)

        public byte[] GenerateExcelReport(ReportResultData data)
        {
            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add("Mobile Shop Report");

            worksheet.Cell(1, 1).Value = data.ReportTitle;
            worksheet.Cell(1, 1).Style.Font.Bold = true;
            worksheet.Cell(1, 1).Style.Font.FontSize = 14;

            worksheet.Cell(2, 1).Value = $"Filters: {data.AppliedFiltersText} | Generated: {data.GeneratedAt.ToIstString("yyyy-MM-dd HH:mm IST")}";
            worksheet.Cell(2, 1).Style.Font.Italic = true;

            int rowIdx = 4;
            for (int c = 0; c < data.Headers.Count; c++)
            {
                var cell = worksheet.Cell(rowIdx, c + 1);
                cell.Value = data.Headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1E293B");
                cell.Style.Font.FontColor = XLColor.White;
            }

            rowIdx++;
            int count = 1;
            foreach (var r in data.Rows)
            {
                worksheet.Cell(rowIdx, 1).Value = count++;
                int col = 2;
                if (!string.IsNullOrEmpty(r.PrimaryText)) worksheet.Cell(rowIdx, col++).Value = r.PrimaryText;
                if (!string.IsNullOrEmpty(r.SecondaryText)) worksheet.Cell(rowIdx, col++).Value = r.SecondaryText;
                if (!string.IsNullOrEmpty(r.DateString)) worksheet.Cell(rowIdx, col++).Value = r.DateString;
                if (!string.IsNullOrEmpty(r.CustomerInfo)) worksheet.Cell(rowIdx, col++).Value = r.CustomerInfo;
                if (!string.IsNullOrEmpty(r.ProductInfo)) worksheet.Cell(rowIdx, col++).Value = r.ProductInfo;
                if (r.GrandTotal > 0) worksheet.Cell(rowIdx, col++).Value = (double)r.GrandTotal;
                if (!string.IsNullOrEmpty(r.BadgeText)) worksheet.Cell(rowIdx, col++).Value = r.BadgeText;
                if (!string.IsNullOrEmpty(r.UserExecutor)) worksheet.Cell(rowIdx, col++).Value = r.UserExecutor;

                rowIdx++;
            }

            worksheet.Columns().AdjustToContents();
            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        public byte[] GeneratePdfReport(ReportResultData data)
        {
            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(30);
                    page.Size(PageSizes.A4.Landscape());
                    page.DefaultTextStyle(x => x.FontSize(9).FontFamily("Arial"));

                    page.Header().Column(col =>
                    {
                        col.Item().Text(data.ReportTitle.ToUpper()).FontSize(14).Bold().FontColor(Colors.Blue.Darken4);
                        col.Item().Text($"Filters: {data.AppliedFiltersText} | Generated: {data.GeneratedAt.ToIstString("yyyy-MM-dd HH:mm IST")}").FontSize(8).Italic().FontColor(Colors.Grey.Darken1);
                        col.Item().PaddingTop(5).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                    });

                    page.Content().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(25);
                            for (int i = 1; i < data.Headers.Count; i++) cols.RelativeColumn();
                        });

                        table.Header(h =>
                        {
                            foreach (var header in data.Headers)
                            {
                                h.Cell().Background(Colors.Blue.Darken3).Padding(4).Text(header).FontColor(Colors.White).Bold();
                            }
                        });

                        int idx = 1;
                        foreach (var row in data.Rows)
                        {
                            var bg = idx % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White;
                            table.Cell().Background(bg).Padding(3).Text(idx.ToString());

                            if (!string.IsNullOrEmpty(row.PrimaryText)) table.Cell().Background(bg).Padding(3).Text(row.PrimaryText);
                            if (!string.IsNullOrEmpty(row.SecondaryText)) table.Cell().Background(bg).Padding(3).Text(row.SecondaryText);
                            if (!string.IsNullOrEmpty(row.DateString)) table.Cell().Background(bg).Padding(3).Text(row.DateString);
                            if (!string.IsNullOrEmpty(row.CustomerInfo)) table.Cell().Background(bg).Padding(3).Text(row.CustomerInfo);
                            if (!string.IsNullOrEmpty(row.ProductInfo)) table.Cell().Background(bg).Padding(3).Text(row.ProductInfo);
                            if (row.GrandTotal > 0) table.Cell().Background(bg).Padding(3).Text($"₹{row.GrandTotal:N2}").AlignRight().Bold();
                            if (!string.IsNullOrEmpty(row.BadgeText)) table.Cell().Background(bg).Padding(3).Text(row.BadgeText);
                            if (!string.IsNullOrEmpty(row.UserExecutor)) table.Cell().Background(bg).Padding(3).Text(row.UserExecutor);

                            idx++;
                        }
                    });

                    page.Footer().AlignCenter().Text(x =>
                    {
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
                });
            }).GeneratePdf();
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
