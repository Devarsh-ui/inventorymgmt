using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using InventoryManagementSystem.Extensions;
using InventoryManagementSystem.Data;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Services
{
    public class SalesService : ISalesService
    {
        private readonly ISaleRepository _saleRepository;
        private readonly IProductRepository _productRepository;
        private readonly IDeviceRepository _deviceRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IStockService _stockService;
        private readonly IAuditLogService _auditLogService;
        private readonly MongoDbContext _context;

        public SalesService(
            ISaleRepository saleRepository,
            IProductRepository productRepository,
            IDeviceRepository deviceRepository,
            ICustomerRepository customerRepository,
            IStockService stockService,
            IAuditLogService auditLogService,
            MongoDbContext context)
        {
            _saleRepository = saleRepository;
            _productRepository = productRepository;
            _deviceRepository = deviceRepository;
            _customerRepository = customerRepository;
            _stockService = stockService;
            _auditLogService = auditLogService;
            _context = context;

            QuestPDF.Settings.License = LicenseType.Community;
        }

        public async Task<Sale?> CreateSaleAsync(Sale sale)
        {
            if (sale == null || sale.Items == null || !sale.Items.Any())
            {
                throw new InvalidOperationException("Sale cannot be empty.");
            }

            // 1. Validate customer & auto-create/update customer record if provided
            if (!string.IsNullOrWhiteSpace(sale.CustomerPhone))
            {
                if (!InventoryManagementSystem.Helpers.ValidationHelper.IsValidPhone(sale.CustomerPhone))
                {
                    throw new InvalidOperationException("Invalid Customer Contact Number format. Phone number must be 10 numeric digits.");
                }

                var existingCust = await _customerRepository.GetByPhoneAsync(sale.CustomerPhone.Trim());
                if (existingCust != null)
                {
                    sale.CustomerId = existingCust.Id;
                    if (string.IsNullOrWhiteSpace(sale.CustomerName)) sale.CustomerName = existingCust.Name;
                    await _customerRepository.UpdatePurchasesAsync(existingCust.Id, sale.GrandTotal);
                }
                else if (!string.IsNullOrWhiteSpace(sale.CustomerName))
                {
                    var newCust = new Customer
                    {
                        Name = sale.CustomerName.Trim(),
                        Phone = sale.CustomerPhone.Trim(),
                        TotalPurchases = sale.GrandTotal,
                        CreatedDate = DateTime.UtcNow,
                        UpdatedDate = DateTime.UtcNow
                    };
                    await _customerRepository.CreateAsync(newCust);
                    sale.CustomerId = newCust.Id;
                }
            }

            // 2. Validate items & IMEIs
            foreach (var item in sale.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.IMEI1) && !InventoryManagementSystem.Helpers.ValidationHelper.IsValidImei(item.IMEI1))
                {
                    throw new InvalidOperationException($"Invalid IMEI 1 format '{item.IMEI1}' for product '{item.ProductName}'. Must be 14 to 16 digits.");
                }
                if (!string.IsNullOrWhiteSpace(item.IMEI2) && !InventoryManagementSystem.Helpers.ValidationHelper.IsValidImei(item.IMEI2))
                {
                    throw new InvalidOperationException($"Invalid IMEI 2 format '{item.IMEI2}' for product '{item.ProductName}'. Must be 14 to 16 digits.");
                }

                var product = await _productRepository.GetByIdAsync(item.ProductId);
                if (product == null || product.CurrentStock < item.Quantity)
                {
                    throw new InvalidOperationException($"Insufficient stock for product '{item.ProductName}' (Available: {product?.CurrentStock ?? 0})");
                }

                // If physical device / IMEI selected, validate availability & cost
                Device? matchedDevice = null;
                if (!string.IsNullOrWhiteSpace(item.DeviceId))
                {
                    matchedDevice = await _deviceRepository.GetByIdAsync(item.DeviceId);
                }
                else if (!string.IsNullOrWhiteSpace(item.IMEI1))
                {
                    matchedDevice = await _deviceRepository.GetByImeiAsync(item.IMEI1.Trim());
                }

                if (matchedDevice != null)
                {
                    if (matchedDevice.Status != "InStock")
                    {
                        throw new InvalidOperationException($"Selected device IMEI '{matchedDevice.IMEI1}' is in status '{matchedDevice.Status}' and cannot be sold.");
                    }

                    item.DeviceId = matchedDevice.Id;
                    item.IMEI1 = matchedDevice.IMEI1;
                    item.IMEI2 = matchedDevice.IMEI2;
                    item.SerialNumber = matchedDevice.SerialNumber;
                    item.Brand = matchedDevice.Brand;
                    item.ModelName = matchedDevice.ModelName;
                    item.Variant = matchedDevice.Variant;
                    item.Color = matchedDevice.Color; // PRESERVE COLOR
                    item.CostPrice = matchedDevice.PurchasePrice; // ACQUISITION COST / TRADE-IN VALUATION

                    // Calculate warranty end date
                    int durMonths = product.WarrantyDurationMonths > 0 ? product.WarrantyDurationMonths : 12;
                    item.WarrantyEndDate = DateTime.UtcNow.AddMonths(durMonths);
                }
            }

            sale.InvoiceNumber = await GenerateInvoiceNumberAsync();
            sale.Date = DateTime.UtcNow;

            // Save Sale record
            await _saleRepository.CreateAsync(sale);

            // 3. Mark Devices as 'Sold', Auto-Create POS Billing IMEIs, & deduct product stock
            foreach (var item in sale.Items)
            {
                var prod = await _productRepository.GetByIdAsync(item.ProductId);

                if (!string.IsNullOrWhiteSpace(item.DeviceId))
                {
                    await _deviceRepository.UpdateStatusAsync(
                        item.DeviceId,
                        "Sold",
                        sale.InvoiceNumber,
                        sale.CustomerId,
                        sale.CustomerName,
                        sale.CustomerPhone);

                    var soldDev = await _deviceRepository.GetByIdAsync(item.DeviceId);
                    if (soldDev != null && soldDev.Source == "Trade-In")
                    {
                        await _auditLogService.LogActivityAsync(
                            "TRADE_IN_SOLD",
                            sale.CreatedBy,
                            soldDev.ExchangeNumber,
                            $"Sold Traded-In Mobile '{soldDev.Brand} {soldDev.ModelName}' (IMEI: {soldDev.IMEI1}, Color: {soldDev.Color}) under Invoice #{sale.InvoiceNumber} for ₹{item.SellingPrice:N2} (Acquisition Cost: ₹{soldDev.PurchasePrice:N2}, Gross Profit: ₹{(item.SellingPrice - soldDev.PurchasePrice):N2})");
                    }
                }
                else if (!string.IsNullOrWhiteSpace(item.IMEI1))
                {
                    var cleanImei1 = item.IMEI1.Trim();
                    var existingDev = await _deviceRepository.GetByImeiAsync(cleanImei1);
                    if (existingDev != null)
                    {
                        await _deviceRepository.UpdateStatusAsync(
                            existingDev.Id,
                            "Sold",
                            sale.InvoiceNumber,
                            sale.CustomerId,
                            sale.CustomerName,
                            sale.CustomerPhone);
                        item.DeviceId = existingDev.Id;
                    }
                    else
                    {
                        // Auto-create new physical device record for this IMEI sold via POS
                        var brandName = !string.IsNullOrWhiteSpace(item.Brand) ? item.Brand : (prod?.Brand ?? "Mobile");
                        var modelName = !string.IsNullOrWhiteSpace(item.ModelName) ? item.ModelName : (prod?.ModelName ?? item.ProductName);
                        var posDevice = new Device
                        {
                            Brand = brandName.Trim(),
                            ModelName = modelName.Trim(),
                            Variant = item.Variant?.Trim() ?? prod?.Variant ?? string.Empty,
                            Color = item.Color?.Trim() ?? prod?.Color ?? string.Empty,
                            IMEI1 = cleanImei1,
                            IMEI2 = string.IsNullOrWhiteSpace(item.IMEI2) ? null : item.IMEI2.Trim(),
                            SerialNumber = string.IsNullOrWhiteSpace(item.SerialNumber) ? null : item.SerialNumber.Trim(),
                            ProductId = item.ProductId,
                            ProductCode = item.ProductCode ?? prod?.Code,
                            ProductName = item.ProductName ?? prod?.Name,
                            PurchasePrice = item.CostPrice > 0 ? item.CostPrice : (prod?.PurchasePrice ?? 0),
                            SellingPrice = item.SellingPrice > 0 ? item.SellingPrice : (prod?.SellingPrice ?? 0),
                            Status = "Sold",
                            Source = "POS Billing",
                            InvoiceNumber = sale.InvoiceNumber,
                            CustomerId = sale.CustomerId,
                            CustomerName = sale.CustomerName,
                            CustomerPhone = sale.CustomerPhone,
                            Notes = $"Sold via POS Billing Invoice #{sale.InvoiceNumber}",
                            CreatedBy = sale.CreatedBy,
                            CreatedDate = DateTime.UtcNow,
                            UpdatedDate = DateTime.UtcNow
                        };
                        await _deviceRepository.CreateAsync(posDevice);
                        item.DeviceId = posDevice.Id;
                    }
                }

                await _stockService.StockOutAsync(item.ProductId, item.Quantity, $"Mobile Invoice Sale: {sale.InvoiceNumber}", sale.CreatedBy);
            }

            return sale;
        }

        public async Task<string> GenerateInvoiceNumberAsync()
        {
            var istDate = DateTimeExtensions.ToIst(DateTime.UtcNow);
            var dateStr = istDate.ToString("yyyyMMdd");
            var seq = await _saleRepository.GetNextInvoiceSequenceAsync();

            string invoiceNumber;
            while (true)
            {
                var suffix = seq.ToString("D4");
                invoiceNumber = $"INV-{dateStr}-{suffix}";

                var existing = await _saleRepository.GetByInvoiceNumberAsync(invoiceNumber);
                if (existing == null)
                {
                    break;
                }
                seq++;
            }

            return invoiceNumber;
        }

        public async Task<IEnumerable<Sale>> GetAllSalesAsync()
        {
            return await _saleRepository.GetAllAsync();
        }

        public async Task<Sale?> GetSaleByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            var sale = await _saleRepository.GetByIdAsync(id);
            if (sale == null)
            {
                sale = await _saleRepository.GetByInvoiceNumberAsync(id.Trim());
            }
            return sale;
        }

        public async Task<Sale?> GetSaleByInvoiceNumberAsync(string invoiceNumber)
        {
            return await _saleRepository.GetByInvoiceNumberAsync(invoiceNumber);
        }

        public async Task<IEnumerable<Sale>> GetPagedSalesAsync(int page, int pageSize)
        {
            return await _saleRepository.GetPagedSalesAsync(page, pageSize);
        }

        public async Task<long> GetTotalSalesCountAsync()
        {
            return await _saleRepository.GetTotalSalesCountAsync();
        }

        public async Task<(IEnumerable<Sale> Items, long TotalCount)> GetFilteredSalesAsync(
            string? searchTerm,
            string? customerName,
            DateTime? startDate,
            DateTime? endDate,
            string? cashier,
            int page,
            int pageSize,
            string? paymentStatus = null,
            string? paymentMethod = null,
            decimal? minAmount = null,
            decimal? maxAmount = null,
            string? sortBy = null,
            bool isDescending = true)
        {
            return await _saleRepository.GetFilteredSalesAsync(
                searchTerm, customerName, startDate, endDate, cashier, page, pageSize,
                paymentStatus, paymentMethod, minAmount, maxAmount, sortBy, isDescending);
        }

        public async Task<Sale?> UpdateSaleAsync(
            string saleId,
            string customerName,
            string customerPhone,
            string paymentStatus,
            decimal discount,
            decimal amountPaid,
            List<SaleItem> newItems,
            string updatedBy)
        {
            var existingSale = await _saleRepository.GetByIdAsync(saleId);
            if (existingSale == null) return null;

            // Revert previous stock deductions & device statuses
            foreach (var item in existingSale.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.DeviceId))
                {
                    await _deviceRepository.UpdateStatusAsync(item.DeviceId, "InStock");
                }
                await _stockService.StockInAsync(item.ProductId, item.Quantity, $"Invoice #{existingSale.InvoiceNumber} Edit Reversal", updatedBy);
            }

            // Apply new stock deductions & device statuses
            foreach (var item in newItems)
            {
                var product = await _productRepository.GetByIdAsync(item.ProductId);
                if (product == null || product.CurrentStock < item.Quantity)
                {
                    throw new InvalidOperationException($"Insufficient stock for product '{item.ProductName}' (Available: {product?.CurrentStock ?? 0})");
                }
            }

            foreach (var item in newItems)
            {
                if (!string.IsNullOrWhiteSpace(item.DeviceId))
                {
                    await _deviceRepository.UpdateStatusAsync(item.DeviceId, "Sold", existingSale.InvoiceNumber, existingSale.CustomerId, customerName, customerPhone);
                }
                await _stockService.StockOutAsync(item.ProductId, item.Quantity, $"Invoice #{existingSale.InvoiceNumber} Updated Sale", updatedBy);
            }

            decimal subTotal = newItems.Sum(i => i.Quantity * i.SellingPrice);
            decimal totalDiscount = discount + existingSale.ExchangeDiscount;
            decimal gstAmount = System.Math.Round((subTotal - totalDiscount) * (existingSale.GstPercentage / 100m), 2);
            decimal grandTotal = System.Math.Max(0m, (subTotal - totalDiscount) + gstAmount);
            decimal dueAmount = System.Math.Max(0m, grandTotal - amountPaid);

            existingSale.CustomerName = customerName ?? string.Empty;
            existingSale.CustomerPhone = customerPhone ?? string.Empty;
            existingSale.PaymentStatus = paymentStatus ?? "Paid";
            existingSale.Discount = discount;
            existingSale.SubTotal = subTotal;
            existingSale.GstAmount = gstAmount;
            existingSale.GrandTotal = grandTotal;
            existingSale.AmountPaid = amountPaid;
            existingSale.DueAmount = dueAmount;
            existingSale.Items = newItems;

            await _saleRepository.UpdateAsync(existingSale.Id, existingSale);
            return existingSale;
        }

        public async Task<bool> DeleteSaleAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            var sale = await _saleRepository.GetByIdAsync(id);
            if (sale == null) return false;

            // Restock products & set devices back to InStock or delete if created by POS
            foreach (var item in sale.Items)
            {
                if (!string.IsNullOrWhiteSpace(item.DeviceId))
                {
                    var dev = await _deviceRepository.GetByIdAsync(item.DeviceId);
                    if (dev != null)
                    {
                        if (dev.Source == "POS Billing")
                        {
                            await _deviceRepository.DeleteAsync(dev.Id);
                        }
                        else
                        {
                            await _deviceRepository.UpdateStatusAsync(dev.Id, "InStock");
                        }
                    }
                }
                else if (!string.IsNullOrWhiteSpace(item.IMEI1))
                {
                    var dev = await _deviceRepository.GetByImeiAsync(item.IMEI1.Trim());
                    if (dev != null)
                    {
                        if (dev.Source == "POS Billing")
                        {
                            await _deviceRepository.DeleteAsync(dev.Id);
                        }
                        else
                        {
                            await _deviceRepository.UpdateStatusAsync(dev.Id, "InStock");
                        }
                    }
                }

                await _stockService.StockInAsync(item.ProductId, item.Quantity, $"Invoice #{sale.InvoiceNumber} Cancellation/Deletion", sale.CreatedBy);
            }

            await _saleRepository.DeleteAsync(id);
            return true;
        }

        public async Task<long> DeleteSalesAsync(IEnumerable<string> ids)
        {
            if (ids == null || !ids.Any()) return 0;
            long count = 0;
            foreach (var id in ids)
            {
                if (await DeleteSaleAsync(id))
                {
                    count++;
                }
            }
            return count;
        }

        public byte[] GenerateInvoicePdf(Sale sale)
        {
            var settings = _context.GetCollection<Settings>("Settings").Find(FilterDefinition<Settings>.Empty).FirstOrDefault() ?? new Settings();

            var companyName = !string.IsNullOrWhiteSpace(settings.CompanyName) ? settings.CompanyName : "MOBILE SHOP INVENTORY SYSTEM";
            var companyGstin = !string.IsNullOrWhiteSpace(sale.CompanyGstin) ? sale.CompanyGstin : (!string.IsNullOrWhiteSpace(settings.GstinNumber) ? settings.GstinNumber : "27AAAAA0000A1Z5");
            var companyPhone = !string.IsNullOrWhiteSpace(settings.CompanyPhone) ? settings.CompanyPhone : "+91 98765 43210";
            var companyEmail = !string.IsNullOrWhiteSpace(settings.CompanyEmail) ? settings.CompanyEmail : "support@mobileshop.com";
            var companyAddress = !string.IsNullOrWhiteSpace(settings.Address) ? settings.Address : "123 Mobile Market Hub, Mumbai, India";
            var formattedDate = sale.Date.ToIstString("yyyy-MM-dd HH:mm IST");

            return Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Margin(40);
                    page.Size(PageSizes.A4);
                    page.DefaultTextStyle(x => x.FontSize(9.5f).FontFamily("Arial"));

                    // Header Section
                    page.Header().Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text(companyName.ToUpper()).FontSize(15).Bold().FontColor(Colors.Blue.Darken3);
                            col.Item().Text(companyAddress).FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                            col.Item().Text($"Phone: {companyPhone} · Email: {companyEmail}").FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                            col.Item().Text($"Tax GSTIN: {companyGstin}").FontSize(8.5f).Bold().FontColor(Colors.Grey.Darken3);
                        });

                        row.ConstantItem(200).Column(col =>
                        {
                            col.Item().Text("TAX INVOICE").FontSize(18).Bold().AlignRight().FontColor(Colors.Blue.Darken4);
                            col.Item().Text($"Invoice #: {sale.InvoiceNumber}").AlignRight().Bold().FontSize(10.5f);
                            col.Item().Text($"Date: {formattedDate}").AlignRight().FontSize(8.5f).FontColor(Colors.Grey.Darken2);
                        });
                    });

                    // Customer Details
                    page.Content().Column(col =>
                    {
                        col.Item().PaddingTop(12).PaddingBottom(8).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);

                        col.Item().Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Billed To:").Bold().FontColor(Colors.Grey.Darken3);
                                c.Item().Text(string.IsNullOrWhiteSpace(sale.CustomerName) ? "Walk-in Customer" : sale.CustomerName).FontSize(10.5f).Bold();
                                if (!string.IsNullOrWhiteSpace(sale.CustomerPhone))
                                {
                                    c.Item().Text($"Phone: {sale.CustomerPhone}").FontSize(9);
                                }
                            });

                            row.ConstantItem(200).Column(c =>
                            {
                                c.Item().Text($"Payment Method: {sale.PaymentMethod ?? "Cash"}").Bold().FontColor(Colors.Grey.Darken3);
                                var statusColor = sale.PaymentStatus == "Paid" ? Colors.Green.Darken2 : (sale.PaymentStatus == "Partial" ? Colors.Orange.Darken2 : Colors.Red.Darken2);
                                c.Item().Text($"Status: {(sale.PaymentStatus ?? "Paid").ToUpper()}").FontSize(10.5f).Bold().FontColor(statusColor);
                                c.Item().Text($"Employee: {sale.CreatedBy}").FontSize(9);
                            });
                        });

                        // Main Items Table
                        col.Item().PaddingTop(12).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.ConstantColumn(30);   // #
                                columns.RelativeColumn(3f);   // Item Description & IMEIs
                                columns.ConstantColumn(70);   // Price
                                columns.ConstantColumn(40);   // Qty
                                columns.ConstantColumn(80);   // Total
                            });

                            // Table Header
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("#").FontColor(Colors.White).Bold();
                                header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Item Description & Serial / IMEI").FontColor(Colors.White).Bold();
                                header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Price").AlignRight().FontColor(Colors.White).Bold();
                                header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Qty").AlignCenter().FontColor(Colors.White).Bold();
                                header.Cell().Background(Colors.Blue.Darken3).Padding(4).Text("Total").AlignRight().FontColor(Colors.White).Bold();
                            });

                            // Table Rows
                            int index = 1;
                            foreach (var item in sale.Items)
                            {
                                var background = index % 2 == 0 ? Colors.Grey.Lighten4 : Colors.White;

                                table.Cell().Background(background).Padding(4).Text(index.ToString());

                                table.Cell().Background(background).Padding(4).Column(c =>
                                {
                                    c.Item().Text(item.ProductName).Bold();
                                    if (!string.IsNullOrWhiteSpace(item.Brand) || !string.IsNullOrWhiteSpace(item.Variant))
                                    {
                                        c.Item().Text($"{item.Brand} {item.ModelName} ({item.Variant} {item.Color})").FontSize(8).FontColor(Colors.Grey.Darken2);
                                    }
                                    if (!string.IsNullOrWhiteSpace(item.IMEI1))
                                    {
                                        c.Item().Text($"IMEI 1: {item.IMEI1}" + (!string.IsNullOrWhiteSpace(item.IMEI2) ? $" | IMEI 2: {item.IMEI2}" : "")).FontSize(8).Bold().FontColor(Colors.Blue.Darken3);
                                    }
                                    if (item.WarrantyEndDate.HasValue)
                                    {
                                        c.Item().Text($"Warranty Valid Until: {item.WarrantyEndDate.Value:yyyy-MM-dd}").FontSize(7.5f).Italic().FontColor(Colors.Green.Darken3);
                                    }
                                });

                                table.Cell().Background(background).Padding(4).Text($"₹{item.SellingPrice:N2}").AlignRight();
                                table.Cell().Background(background).Padding(4).Text(item.Quantity.ToString()).AlignCenter();
                                table.Cell().Background(background).Padding(4).Text($"₹{item.Total:N2}").AlignRight().Bold();

                                index++;
                            }
                        });

                        // Calculations Block
                        col.Item().AlignRight().PaddingTop(12).Row(row =>
                        {
                            row.ConstantItem(260).Table(summaryTable =>
                            {
                                summaryTable.ColumnsDefinition(cols =>
                                {
                                    cols.RelativeColumn();
                                    cols.ConstantColumn(110);
                                });

                                summaryTable.Cell().Padding(2).Text("Sub-Total:").AlignRight();
                                summaryTable.Cell().Padding(2).Text($"₹{sale.SubTotal:N2}").AlignRight();

                                summaryTable.Cell().Padding(2).Text($"GST ({sale.GstPercentage}%):").AlignRight();
                                summaryTable.Cell().Padding(2).Text($"₹{sale.GstAmount:N2}").AlignRight();

                                if (sale.Discount > 0)
                                {
                                    summaryTable.Cell().Padding(2).Text("Promo Discount:").AlignRight().FontColor(Colors.Red.Medium);
                                    summaryTable.Cell().Padding(2).Text($"-₹{sale.Discount:N2}").AlignRight().FontColor(Colors.Red.Medium);
                                }

                                if (sale.ExchangeDiscount > 0)
                                {
                                    summaryTable.Cell().Padding(2).Text("Mobile Trade-In Credit:").AlignRight().FontColor(Colors.Green.Darken2);
                                    summaryTable.Cell().Padding(2).Text($"-₹{sale.ExchangeDiscount:N2}").AlignRight().FontColor(Colors.Green.Darken2);
                                }

                                summaryTable.Cell().BorderTop(1).BorderColor(Colors.Grey.Lighten1).Padding(3).Text("Grand Total:").AlignRight().Bold();
                                summaryTable.Cell().BorderTop(1).BorderColor(Colors.Grey.Lighten1).Padding(3).Text($"₹{sale.GrandTotal:N2}").AlignRight().Bold().FontSize(11).FontColor(Colors.Blue.Darken3);

                                summaryTable.Cell().Padding(2).Text("Amount Paid:").AlignRight().FontColor(Colors.Green.Darken2);
                                summaryTable.Cell().Padding(2).Text($"₹{sale.AmountPaid:N2}").AlignRight().Bold().FontColor(Colors.Green.Darken2);

                                if (sale.DueAmount > 0)
                                {
                                    summaryTable.Cell().Padding(2).Text("Due Amount:").AlignRight().FontColor(Colors.Red.Medium);
                                    summaryTable.Cell().Padding(2).Text($"₹{sale.DueAmount:N2}").AlignRight().Bold().FontColor(Colors.Red.Medium);
                                }
                            });
                        });

                        col.Item().PaddingTop(20).AlignCenter().Text("Thank you for shopping with us!").FontSize(9.5f).Italic().FontColor(Colors.Grey.Darken1);
                        col.Item().AlignCenter().Text($"For warranty & support inquiries: {companyEmail}").FontSize(8).FontColor(Colors.Grey.Medium);
                    });

                    // Page Footer
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
    }
}
