using InventoryManagementSystem.Helpers;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Services
{
    public class SupplierService : ISupplierService
    {
        private readonly ISupplierRepository _supplierRepository;
        private readonly IAuditLogService _auditLogService;
        private readonly IAccountValidationService _accountValidationService;
        private readonly IProductRepository _productRepository;
        private readonly ICategoryRepository _categoryRepository;
        private readonly IDeviceRepository _deviceRepository;
        private readonly IImageService _imageService;
        private readonly IPasswordPolicyService _passwordPolicyService;

        public SupplierService(
            ISupplierRepository supplierRepository,
            IAuditLogService auditLogService,
            IAccountValidationService accountValidationService,
            IProductRepository productRepository,
            ICategoryRepository categoryRepository,
            IDeviceRepository deviceRepository,
            IImageService imageService,
            IPasswordPolicyService passwordPolicyService)
        {
            _supplierRepository = supplierRepository;
            _auditLogService = auditLogService;
            _accountValidationService = accountValidationService;
            _productRepository = productRepository;
            _categoryRepository = categoryRepository;
            _deviceRepository = deviceRepository;
            _imageService = imageService;
            _passwordPolicyService = passwordPolicyService;
        }

        public async Task<IEnumerable<Supplier>> GetAllSuppliersAsync()
        {
            return await _supplierRepository.GetAllAsync();
        }

        public async Task<Supplier?> GetSupplierByIdAsync(string id)
        {
            return await _supplierRepository.GetByIdAsync(id);
        }

        public async Task<IEnumerable<Supplier>> GetPagedSuppliersAsync(string? search, string? terms, string? payableStatus, int page, int pageSize)
        {
            return await _supplierRepository.GetPagedSuppliersAsync(search, terms, payableStatus, page, pageSize);
        }

        public async Task<long> GetFilteredCountAsync(string? search, string? terms, string? payableStatus)
        {
            return await _supplierRepository.GetFilteredCountAsync(search, terms, payableStatus);
        }

        public async Task<(bool Success, string Message, Supplier? Supplier)> SaveSupplierAsync(Supplier supplier, string executedBy)
        {
            if (supplier == null) return (false, "Supplier data is missing.", null);
            if (string.IsNullOrWhiteSpace(supplier.CompanyName)) return (false, "Company / Legal Name is required.", null);
            if (string.IsNullOrWhiteSpace(supplier.VendorName)) return (false, "Vendor / Brand Name is required.", null);

            supplier.CompanyName = supplier.CompanyName.Trim();
            supplier.VendorName = supplier.VendorName.Trim();

            // Contact Phone & Email Validation
            if (!string.IsNullOrWhiteSpace(supplier.Phone) && !ValidationHelper.IsValidPhone(supplier.Phone))
            {
                return (false, "Invalid Contact Number format. Phone number must be 10 numeric digits.", null);
            }
            if (!string.IsNullOrWhiteSpace(supplier.Email) && !ValidationHelper.IsValidEmail(supplier.Email))
            {
                return (false, "Invalid Email address format. Example: supplier@domain.com", null);
            }

            // Optional GSTIN Validation (max 15 alphanumeric characters, no emojis or special symbols)
            if (!string.IsNullOrWhiteSpace(supplier.Gstin))
            {
                var cleanGstin = supplier.Gstin.Trim().ToUpperInvariant();
                if (cleanGstin.Length > 15 || !ValidationHelper.IsValidGstin(cleanGstin))
                {
                    return (false, "Invalid Vendor GSTIN format. Must be a valid 15-character alphanumeric GSTIN (e.g. 27AAAAA0000A1Z5) without emojis or special characters.", null);
                }
                supplier.Gstin = cleanGstin;
            }
            else
            {
                supplier.Gstin = string.Empty;
            }

            // Global Email Uniqueness Check across Admin, Staff, and Suppliers
            if (!string.IsNullOrWhiteSpace(supplier.Email))
            {
                bool isDuplicate = await _accountValidationService.IsEmailAlreadyRegisteredAsync(supplier.Email, excludeSupplierId: supplier.Id);
                if (isDuplicate)
                {
                    return (false, "This email address is already registered with another account.", null);
                }
            }

            // Uniqueness Check among Suppliers
            var existingByCompany = await _supplierRepository.GetByNameAsync(supplier.CompanyName);
            var existingByVendor = await _supplierRepository.GetByNameAsync(supplier.VendorName);

            if (string.IsNullOrEmpty(supplier.Id))
            {
                if (existingByCompany != null) return (false, $"Supplier with Company / Legal Name '{supplier.CompanyName}' already exists.", existingByCompany);
                if (existingByVendor != null) return (false, $"Supplier with Vendor / Brand Name '{supplier.VendorName}' already exists.", existingByVendor);

                if (string.IsNullOrWhiteSpace(supplier.Status)) supplier.Status = "Active";

                if (string.IsNullOrWhiteSpace(supplier.PasswordHash))
                {
                    return (false, "Portal account password is required.", null);
                }

                if (!supplier.PasswordHash.StartsWith("$2"))
                {
                    var policyResult = _passwordPolicyService.Validate(supplier.PasswordHash);
                    if (!policyResult.IsValid)
                    {
                        return (false, policyResult.GetCombinedErrorMessage(), null);
                    }
                    supplier.PasswordHash = BCrypt.Net.BCrypt.HashPassword(supplier.PasswordHash.Trim());
                }

                supplier.CreatedDate = DateTime.UtcNow;
                supplier.UpdatedDate = DateTime.UtcNow;
                await _supplierRepository.CreateAsync(supplier);

                await _auditLogService.LogActivityAsync(
                    "SUPPLIER_CREATED",
                    executedBy,
                    $"{supplier.DisplayVendorName} ({supplier.DisplayCompanyName})",
                    $"Added new supplier '{supplier.DisplayVendorName}' ({supplier.DisplayCompanyName}) [{supplier.Email}]");

                return (true, "Supplier account added successfully.", supplier);
            }
            else
            {
                if (existingByCompany != null && existingByCompany.Id != supplier.Id)
                {
                    return (false, $"Another supplier with Company / Legal Name '{supplier.CompanyName}' already exists.", null);
                }
                if (existingByVendor != null && existingByVendor.Id != supplier.Id)
                {
                    return (false, $"Another supplier with Vendor / Brand Name '{supplier.VendorName}' already exists.", null);
                }

                var currentRecord = await _supplierRepository.GetByIdAsync(supplier.Id);
                bool passwordChanged = false;
                if (currentRecord != null)
                {
                    // Handle password update logic: leave blank on edit to keep existing password
                    if (!string.IsNullOrWhiteSpace(supplier.PasswordHash) && !supplier.PasswordHash.StartsWith("$2"))
                    {
                        var policyResult = _passwordPolicyService.Validate(supplier.PasswordHash, currentPasswordHash: currentRecord.PasswordHash);
                        if (!policyResult.IsValid)
                        {
                            return (false, policyResult.GetCombinedErrorMessage(), null);
                        }
                        supplier.PasswordHash = BCrypt.Net.BCrypt.HashPassword(supplier.PasswordHash.Trim());
                        passwordChanged = true;
                    }
                    else
                    {
                        supplier.PasswordHash = currentRecord.PasswordHash;
                    }
                }

                if (string.IsNullOrWhiteSpace(supplier.Status)) supplier.Status = currentRecord?.Status ?? "Active";

                supplier.UpdatedDate = DateTime.UtcNow;
                await _supplierRepository.UpdateAsync(supplier.Id, supplier);

                if (passwordChanged)
                {
                    await _auditLogService.LogExAsync(
                        "SUPPLIER_PASSWORD_CHANGED",
                        "Supplier Management",
                        supplier.DisplayVendorName,
                        $"Password updated for supplier '{supplier.DisplayVendorName}' ({supplier.DisplayCompanyName}).",
                        "Success",
                        "Information");
                }

                await _auditLogService.LogActivityAsync(
                    "SUPPLIER_UPDATED",
                    executedBy,
                    $"{supplier.DisplayVendorName} ({supplier.DisplayCompanyName})",
                    $"Updated supplier account '{supplier.DisplayVendorName}' ({supplier.DisplayCompanyName})");

                return (true, "Supplier profile updated successfully.", supplier);
            }
        }

        public async Task<(bool Success, string Message)> DeleteSupplierAsync(string id, string executedBy)
        {
            if (string.IsNullOrWhiteSpace(id)) return (false, "Supplier ID is required.");
            var supplier = await _supplierRepository.GetByIdAsync(id);
            if (supplier == null) return (false, "Supplier record not found.");

            // 1. Cascade delete all products created by this supplier
            var supplierProducts = (await _productRepository.FindAsync(p => p.SupplierId == id)).ToList();
            foreach (var prod in supplierProducts)
            {
                if (!string.IsNullOrEmpty(prod.ImagePublicId))
                {
                    await _imageService.DeleteImageAsync(prod.ImagePublicId);
                }
                await _deviceRepository.DeleteByProductIdAsync(prod.Id);
                await _productRepository.DeleteAsync(prod.Id);
            }

            // 2. Cascade delete all categories created by this supplier
            var supplierCategories = (await _categoryRepository.FindAsync(c => c.SupplierId == id)).ToList();
            foreach (var cat in supplierCategories)
            {
                await _categoryRepository.DeleteAsync(cat.Id);
            }

            // 3. Delete the supplier record
            await _supplierRepository.DeleteAsync(id);
            await _auditLogService.LogActivityAsync(
                "Supplier Deleted",
                executedBy,
                $"{supplier.DisplayVendorName} ({supplier.DisplayCompanyName})",
                $"Deleted supplier '{supplier.DisplayVendorName}' ({supplier.DisplayCompanyName}) along with {supplierProducts.Count} product(s) and {supplierCategories.Count} category/categories.");

            return (true, $"Supplier '{supplier.DisplayVendorName}' and associated catalog products deleted successfully.");
        }

        public async Task CleanupOrphanedSupplierDataAsync()
        {
            try
            {
                var activeSuppliers = (await _supplierRepository.GetAllAsync()).ToList();
                var activeSupplierIds = activeSuppliers.Select(s => s.Id).ToHashSet();

                // 1. Clean up products with a SupplierId that does not match any existing active supplier
                var allProducts = (await _productRepository.GetAllAsync()).ToList();
                var orphanedProducts = allProducts.Where(p => !string.IsNullOrWhiteSpace(p.SupplierId) && !activeSupplierIds.Contains(p.SupplierId)).ToList();
                foreach (var p in orphanedProducts)
                {
                    if (!string.IsNullOrEmpty(p.ImagePublicId))
                    {
                        await _imageService.DeleteImageAsync(p.ImagePublicId);
                    }
                    await _deviceRepository.DeleteByProductIdAsync(p.Id);
                    await _productRepository.DeleteAsync(p.Id);
                }

                // 2. Clean up categories with a SupplierId that does not match any existing active supplier
                var allCategories = (await _categoryRepository.GetAllAsync()).ToList();
                var orphanedCategories = allCategories.Where(c => !string.IsNullOrWhiteSpace(c.SupplierId) && !activeSupplierIds.Contains(c.SupplierId)).ToList();
                foreach (var c in orphanedCategories)
                {
                    await _categoryRepository.DeleteAsync(c.Id);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SUPPLIER SERVICE] CleanupOrphanedSupplierDataAsync notice: {ex.Message}");
            }
        }

        public async Task<Supplier?> AuthenticateSupplierAsync(string emailOrUsername, string password)
        {
            if (string.IsNullOrWhiteSpace(emailOrUsername) || string.IsNullOrWhiteSpace(password)) return null;

            var input = emailOrUsername.Trim();
            var all = await _supplierRepository.GetAllAsync();
            var supplier = all.FirstOrDefault(s =>
                (!string.IsNullOrEmpty(s.Email) && string.Equals(s.Email.Trim(), input, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(s.VendorName) && string.Equals(s.VendorName.Trim(), input, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(s.CompanyName) && string.Equals(s.CompanyName.Trim(), input, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(s.Phone) && string.Equals(s.Phone.Trim(), input, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(s.ContactPerson) && string.Equals(s.ContactPerson.Trim(), input, StringComparison.OrdinalIgnoreCase)));

            if (supplier == null)
            {
                return null;
            }

            if (string.Equals(supplier.Status, "Inactive", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(supplier.PasswordHash))
            {
                return null;
            }

            bool isValid = false;
            try
            {
                if (supplier.PasswordHash.StartsWith("$2"))
                {
                    isValid = BCrypt.Net.BCrypt.Verify(password, supplier.PasswordHash);
                }
                else if (supplier.PasswordHash == password)
                {
                    isValid = true;
                    // Auto-upgrade plain-text password to BCrypt hash
                    supplier.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
                    await _supplierRepository.UpdateAsync(supplier.Id, supplier);
                }
            }
            catch
            {
                if (supplier.PasswordHash == password)
                {
                    isValid = true;
                    supplier.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
                    await _supplierRepository.UpdateAsync(supplier.Id, supplier);
                }
            }

            if (!isValid) return null;

            supplier.LastLogin = DateTime.UtcNow;
            if (string.IsNullOrWhiteSpace(supplier.Status)) supplier.Status = "Active";
            await _supplierRepository.UpdateAsync(supplier.Id, supplier);
            return supplier;
        }
    }
}
