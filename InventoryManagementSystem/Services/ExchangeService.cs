using InventoryManagementSystem.Helpers;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using MongoDB.Bson;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Services
{
    public class ExchangeService : IExchangeService
    {
        private readonly IExchangeRepository _exchangeRepository;
        private readonly IDeviceRepository _deviceRepository;
        private readonly IProductRepository _productRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IAuditLogService _auditLogService;

        public ExchangeService(
            IExchangeRepository exchangeRepository,
            IDeviceRepository deviceRepository,
            IProductRepository productRepository,
            ICustomerRepository customerRepository,
            IAuditLogService auditLogService)
        {
            _exchangeRepository = exchangeRepository;
            _deviceRepository = deviceRepository;
            _productRepository = productRepository;
            _customerRepository = customerRepository;
            _auditLogService = auditLogService;
        }

        public async Task<ExchangeRecord?> GetExchangeByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            return await _exchangeRepository.GetByIdAsync(id);
        }

        public async Task<IEnumerable<ExchangeRecord>> GetPagedExchangesAsync(string? search, int page, int pageSize)
        {
            return await _exchangeRepository.GetPagedExchangesAsync(search, page, pageSize);
        }

        public async Task<IEnumerable<ExchangeRecord>> GetFilteredExchangesAsync(string? search, string? brand, string? color, string? condition, string? destinationStatus, int page, int pageSize)
        {
            return await _exchangeRepository.GetFilteredExchangesAsync(search, brand, color, condition, destinationStatus, page, pageSize);
        }

        public async Task<long> GetFilteredCountAsync(string? search)
        {
            return await _exchangeRepository.GetFilteredCountAsync(search);
        }

        public async Task<long> GetFilteredCountExAsync(string? search, string? brand, string? color, string? condition, string? destinationStatus)
        {
            return await _exchangeRepository.GetFilteredCountExAsync(search, brand, color, condition, destinationStatus);
        }

        public async Task<(bool Success, string Message, ExchangeRecord? Record)> ProcessExchangeAsync(ExchangeRecord req, string executedBy)
        {
            try
            {
                if (req == null) return (false, "Exchange data missing.", null);
                if (string.IsNullOrWhiteSpace(req.OldBrand) || string.IsNullOrWhiteSpace(req.OldModel))
                {
                    return (false, "Old phone Brand and Model are required.", null);
                }
                if (string.IsNullOrWhiteSpace(req.OldColor))
                {
                    return (false, "Phone Color is required.", null);
                }
                if (string.IsNullOrWhiteSpace(req.OldImei1))
                {
                    return (false, "Primary IMEI (IMEI 1) is required.", null);
                }
                if (req.FinalExchangeValue <= 0)
                {
                    return (false, "Final exchange valuation must be greater than zero.", null);
                }
                if (string.IsNullOrWhiteSpace(req.CustomerName) || string.IsNullOrWhiteSpace(req.CustomerPhone))
                {
                    return (false, "Customer Name and Contact Phone are required.", null);
                }

                // Field validations
                var imei1Clean = req.OldImei1.Trim();
                if (!ValidationHelper.IsValidImei(imei1Clean))
                {
                    return (false, $"Invalid Primary IMEI 1 format '{imei1Clean}'. IMEI must be a 14 to 16 digit number.", null);
                }

                string? imei2Clean = !string.IsNullOrWhiteSpace(req.OldImei2) ? req.OldImei2.Trim() : null;
                if (imei2Clean != null && !ValidationHelper.IsValidImei(imei2Clean))
                {
                    return (false, $"Invalid Secondary IMEI 2 format '{imei2Clean}'. IMEI must be a 14 to 16 digit number.", null);
                }

                if (!ValidationHelper.IsValidPhone(req.CustomerPhone))
                {
                    return (false, "Invalid Customer Contact Phone format. Contact number must be 10 numeric digits.", null);
                }

                if (!string.IsNullOrWhiteSpace(req.CustomerEmail) && !ValidationHelper.IsValidEmail(req.CustomerEmail))
                {
                    return (false, "Invalid Customer Email format. Example: user@domain.com", null);
                }

                if (req.BatteryHealthPercentage.HasValue && (req.BatteryHealthPercentage < 0 || req.BatteryHealthPercentage > 100))
                {
                    return (false, "Battery Health percentage must be between 0% and 100%.", null);
                }

                // System-wide IMEI Uniqueness Checks
                if (await _deviceRepository.IsImeiExistsAsync(imei1Clean))
                {
                    return (false, $"This IMEI 1 '{imei1Clean}' already exists in inventory/devices database. Duplicate IMEI cannot be accepted.", null);
                }
                if (imei2Clean != null && await _deviceRepository.IsImeiExistsAsync(imei2Clean))
                {
                    return (false, $"This IMEI 2 '{imei2Clean}' already exists in inventory/devices database. Duplicate IMEI cannot be accepted.", null);
                }

                var existingExch1 = await _exchangeRepository.GetByImeiAsync(imei1Clean);
                if (existingExch1 != null)
                {
                    var isActiveInDeviceRepo = await _deviceRepository.IsImeiExistsAsync(imei1Clean);
                    if (isActiveInDeviceRepo)
                    {
                        return (false, $"Phone with IMEI '{imei1Clean}' is currently active in inventory under Exchange #{existingExch1.ExchangeNumber}.", null);
                    }
                }

                // Security Lock Check
                if (req.AppleActivationLock == "Enabled" || req.GoogleFRPAccountLock == "Enabled")
                {
                    req.HasAccountLock = true;
                }

                if (req.HasAccountLock && req.InventoryDestinationStatus == "InStock")
                {
                    // Device with active lock cannot automatically enter sellable inventory
                    req.InventoryDestinationStatus = "Pending";
                }

                // Generate Exchange Number
                req.ExchangeNumber = $"EXCH-{DateTime.UtcNow:yyyyMMdd}-{Random.Shared.Next(1000, 9999)}";
                req.Date = DateTime.UtcNow;
                req.ExecutedBy = executedBy;
                req.InspectorName = string.IsNullOrWhiteSpace(req.InspectorName) ? executedBy : req.InspectorName;

                // Customer Lookup & Auto-creation
                var custPhoneClean = req.CustomerPhone.Trim();
                var existingCust = await _customerRepository.GetByPhoneAsync(custPhoneClean);
                if (existingCust != null)
                {
                    req.CustomerId = existingCust.Id;
                }
                else
                {
                    var newCust = new Customer
                    {
                        Name = req.CustomerName.Trim(),
                        Phone = custPhoneClean,
                        Email = req.CustomerEmail?.Trim() ?? string.Empty,
                        Address = req.CustomerAddress?.Trim() ?? string.Empty,
                        CreatedDate = DateTime.UtcNow,
                        UpdatedDate = DateTime.UtcNow
                    };
                    await _customerRepository.CreateAsync(newCust);
                    req.CustomerId = newCust.Id;
                }

                // 1. Create a physical Device record for the old exchanged-in phone
                var oldDevice = new Device
                {
                    Brand = req.OldBrand.Trim(),
                    ModelName = req.OldModel.Trim(),
                    Variant = req.OldStorage?.Trim() ?? string.Empty,
                    Color = req.OldColor.Trim(), // COLOR PERSISTENCE
                    Ram = req.OldRam?.Trim() ?? string.Empty,
                    Storage = req.OldStorage?.Trim() ?? string.Empty,
                    IMEI1 = imei1Clean,
                    IMEI2 = imei2Clean,
                    SerialNumber = string.IsNullOrWhiteSpace(req.SerialNumber) ? null : req.SerialNumber.Trim(),
                    Condition = req.Condition,
                    BatteryHealthPercentage = req.BatteryHealthPercentage,
                    PurchasePrice = req.FinalExchangeValue, // ACQUISITION COST
                    SellingPrice = System.Math.Round(req.FinalExchangeValue * 1.25m, 2), // Suggested market retail
                    Status = req.InventoryDestinationStatus, // InStock, UnderRepair, Damaged, Pending
                    Source = "Trade-In",
                    ExchangeNumber = req.ExchangeNumber,
                    CustomerId = req.CustomerId,
                    CustomerName = req.CustomerName.Trim(),
                    CustomerPhone = custPhoneClean,
                    Notes = $"Trade-in Exchanged Phone. Condition: {req.Condition}. Accessories: {string.Join(", ", req.AccessoriesReceived ?? new List<string>())}. Remarks: {req.Remarks}",
                    DevicePhotos = req.DevicePhotos ?? new List<string>(),
                    CreatedBy = executedBy,
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow
                };

                // 2. Dedicated Product Catalog Creation / Linking for Products & Specs
                var brandClean = req.OldBrand.Trim();
                var modelClean = req.OldModel.Trim();
                var variantClean = req.OldStorage?.Trim() ?? string.Empty;
                var colorClean = req.OldColor.Trim();

                var targetProductName = $"Pre-Owned {brandClean} {modelClean}";
                if (!string.IsNullOrWhiteSpace(variantClean)) targetProductName += $" ({variantClean})";
                if (!string.IsNullOrWhiteSpace(colorClean)) targetProductName += $" - {colorClean}";

                var products = await _productRepository.GetAllAsync();
                Product? targetProduct = null;

                foreach (var p in products)
                {
                    if (p.Name.Equals(targetProductName, StringComparison.OrdinalIgnoreCase) ||
                       (p.Brand.Equals(brandClean, StringComparison.OrdinalIgnoreCase) && 
                        p.ModelName.Equals(modelClean, StringComparison.OrdinalIgnoreCase) &&
                        p.Variant.Equals(variantClean, StringComparison.OrdinalIgnoreCase)))
                    {
                        targetProduct = p;
                        break;
                    }
                }

                if (targetProduct == null)
                {
                    var codeBrand = brandClean.ToUpper().Replace(" ", "");
                    var codeModel = modelClean.ToUpper().Replace(" ", "");
                    var randSuffix = Random.Shared.Next(100, 999);
                    var productCode = $"EXCH-{codeBrand}-{codeModel}-{randSuffix}";

                    targetProduct = new Product
                    {
                        Name = targetProductName,
                        Code = productCode,
                        ProductType = "Smartphone",
                        Brand = brandClean,
                        ModelName = modelClean,
                        Variant = variantClean,
                        Color = colorClean,
                        PurchasePrice = req.FinalExchangeValue,
                        SellingPrice = System.Math.Round(req.FinalExchangeValue * 1.25m, 2),
                        CurrentStock = 0,
                        MinimumStock = 1,
                        Status = "Active",
                        Description = $"Traded-in pre-owned smartphone ({req.Condition} condition). Primary IMEI: {imei1Clean}."
                    };
                    await _productRepository.CreateAsync(targetProduct);
                }

                if (targetProduct != null && ObjectId.TryParse(targetProduct.Id, out _))
                {
                    oldDevice.ProductId = targetProduct.Id;
                    oldDevice.ProductCode = targetProduct.Code;
                    oldDevice.ProductName = targetProduct.Name;
                    req.ProductId = targetProduct.Id;

                    if (req.InventoryDestinationStatus == "InStock")
                    {
                        targetProduct.CurrentStock += 1;
                        targetProduct.UpdatedDate = DateTime.UtcNow;
                        await _productRepository.UpdateAsync(targetProduct.Id, targetProduct);
                    }
                }
                else
                {
                    oldDevice.ProductId = null;
                    oldDevice.ProductName = targetProductName;
                    oldDevice.ProductCode = "EXCH-USED";
                }

                await _deviceRepository.CreateAsync(oldDevice);
                req.DeviceId = oldDevice.Id;

                await _exchangeRepository.CreateAsync(req);

                // Audit Log
                await _auditLogService.LogActivityAsync(
                    "TRADE_IN_CREATED",
                    executedBy,
                    req.ExchangeNumber,
                    $"Processed Trade-In #{req.ExchangeNumber} for '{req.OldBrand} {req.OldModel}' (Color: {req.OldColor}, IMEI: {imei1Clean}, Valuation: ₹{req.FinalExchangeValue:N2}, Status: {req.InventoryDestinationStatus}, Customer: {req.CustomerName})");

                string successNotice = $"Trade-In #{req.ExchangeNumber} accepted successfully! Device added to inventory with status '{req.InventoryDestinationStatus}' (Valuation: ₹{req.FinalExchangeValue:N2}).";
                if (req.HasAccountLock)
                {
                    successNotice += " ⚠️ Warning: Device has active security account lock. Status set to 'Pending' until unlocked.";
                }

                return (true, successNotice, req);
            }
            catch (Exception ex)
            {
                return (false, "Error processing trade-in exchange: " + ex.Message, null);
            }
        }

        public async Task<(bool Success, string Message, ExchangeRecord? Record)> UpdateExchangeAsync(ExchangeRecord req, string updatedBy)
        {
            try
            {
                if (req == null || string.IsNullOrWhiteSpace(req.Id)) return (false, "Invalid exchange record ID.", null);

                var existing = await _exchangeRepository.GetByIdAsync(req.Id);
                if (existing == null) return (false, "Trade-in exchange record not found.", null);

                if (string.IsNullOrWhiteSpace(req.OldBrand) || string.IsNullOrWhiteSpace(req.OldModel)) return (false, "Old phone Brand and Model are required.", null);
                if (string.IsNullOrWhiteSpace(req.OldColor)) return (false, "Phone Color is required.", null);
                if (string.IsNullOrWhiteSpace(req.OldImei1)) return (false, "Primary IMEI 1 is required.", null);
                if (req.FinalExchangeValue <= 0) return (false, "Final exchange valuation must be greater than zero.", null);
                if (string.IsNullOrWhiteSpace(req.CustomerName) || string.IsNullOrWhiteSpace(req.CustomerPhone)) return (false, "Customer Name and Contact Phone are required.", null);

                var imei1Clean = req.OldImei1.Trim();
                if (!ValidationHelper.IsValidImei(imei1Clean)) return (false, "Invalid Primary IMEI 1 format. IMEI must be 14 to 16 digits.", null);
                if (!ValidationHelper.IsValidPhone(req.CustomerPhone)) return (false, "Invalid Customer Contact Phone format.", null);

                req.ExchangeNumber = existing.ExchangeNumber;
                req.Date = existing.Date;
                req.ExecutedBy = existing.ExecutedBy;
                req.DeviceId = existing.DeviceId;
                req.ProductId = existing.ProductId;

                if (req.DevicePhotos == null || req.DevicePhotos.Count == 0)
                {
                    req.DevicePhotos = existing.DevicePhotos ?? new List<string>();
                }

                await _exchangeRepository.UpdateAsync(existing.Id, req);

                if (!string.IsNullOrWhiteSpace(existing.DeviceId))
                {
                    var dev = await _deviceRepository.GetByIdAsync(existing.DeviceId);
                    if (dev != null)
                    {
                        dev.Brand = req.OldBrand.Trim();
                        dev.ModelName = req.OldModel.Trim();
                        dev.Color = req.OldColor.Trim();
                        dev.IMEI1 = imei1Clean;
                        dev.IMEI2 = req.OldImei2?.Trim();
                        dev.PurchasePrice = req.FinalExchangeValue;
                        dev.CustomerName = req.CustomerName.Trim();
                        dev.CustomerPhone = req.CustomerPhone.Trim();
                        dev.Condition = req.Condition;
                        dev.DevicePhotos = req.DevicePhotos ?? new List<string>();
                        dev.UpdatedDate = DateTime.UtcNow;
                        await _deviceRepository.UpdateAsync(dev.Id, dev);
                    }
                }

                await _auditLogService.LogActivityAsync("TRADE_IN_UPDATED", updatedBy, req.ExchangeNumber, $"Updated Trade-In #{req.ExchangeNumber} record details.");
                return (true, $"Trade-In #{req.ExchangeNumber} record updated successfully.", req);
            }
            catch (Exception ex)
            {
                return (false, "Error updating exchange record: " + ex.Message, null);
            }
        }

        public async Task<(bool Success, string Message)> DeleteExchangeAsync(string id, string deletedBy)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id)) return (false, "Invalid exchange record ID.");

                var existing = await _exchangeRepository.GetByIdAsync(id);
                if (existing == null) return (false, "Exchange record not found.");

                // 1. Hard-delete all physical Device records matching ExchangeNumber or IMEIs from Devices collection
                await _deviceRepository.DeleteDevicesByExchangeAsync(existing.ExchangeNumber, existing.OldImei1, existing.OldImei2);
                if (!string.IsNullOrWhiteSpace(existing.DeviceId))
                {
                    await _deviceRepository.DeleteAsync(existing.DeviceId);
                }

                // 3. Decrement Product Stock if applicable
                if (!string.IsNullOrWhiteSpace(existing.ProductId))
                {
                    var prod = await _productRepository.GetByIdAsync(existing.ProductId);
                    if (prod != null && prod.CurrentStock > 0)
                    {
                        prod.CurrentStock -= 1;
                        prod.UpdatedDate = DateTime.UtcNow;
                        await _productRepository.UpdateAsync(prod.Id, prod);
                    }
                }

                await _exchangeRepository.DeleteAsync(id);
                await _auditLogService.LogActivityAsync("TRADE_IN_DELETED", deletedBy, existing.ExchangeNumber, $"Deleted Trade-In #{existing.ExchangeNumber} record.");
                return (true, $"Trade-In #{existing.ExchangeNumber} record deleted successfully.");
            }
            catch (Exception ex)
            {
                return (false, "Error deleting exchange record: " + ex.Message);
            }
        }
    }
}
