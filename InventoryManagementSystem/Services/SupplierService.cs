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

        public SupplierService(
            ISupplierRepository supplierRepository,
            IAuditLogService auditLogService,
            IAccountValidationService accountValidationService,
            IProductRepository productRepository,
            ICategoryRepository categoryRepository,
            IDeviceRepository deviceRepository,
            IImageService imageService)
        {
            _supplierRepository = supplierRepository;
            _auditLogService = auditLogService;
            _accountValidationService = accountValidationService;
            _productRepository = productRepository;
            _categoryRepository = categoryRepository;
            _deviceRepository = deviceRepository;
            _imageService = imageService;
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
            if (string.IsNullOrWhiteSpace(supplier.CompanyName)) return (false, "Company Name is required.", null);

            // Contact Phone & Email Validation
            if (!string.IsNullOrWhiteSpace(supplier.Phone) && !ValidationHelper.IsValidPhone(supplier.Phone))
            {
                return (false, "Invalid Contact Number format. Phone number must be 10 numeric digits.", null);
            }
            if (!string.IsNullOrWhiteSpace(supplier.Email) && !ValidationHelper.IsValidEmail(supplier.Email))
            {
                return (false, "Invalid Email address format. Example: supplier@domain.com", null);
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

            // Company Name Uniqueness Check among Suppliers
            var existing = await _supplierRepository.GetByNameAsync(supplier.CompanyName);

            if (string.IsNullOrEmpty(supplier.Id))
            {
                if (existing != null) return (false, $"Supplier '{supplier.CompanyName}' already exists.", existing);

                if (string.IsNullOrWhiteSpace(supplier.Status)) supplier.Status = "Active";

                if (!string.IsNullOrWhiteSpace(supplier.PasswordHash) && !supplier.PasswordHash.StartsWith("$2"))
                {
                    supplier.PasswordHash = BCrypt.Net.BCrypt.HashPassword(supplier.PasswordHash);
                }

                supplier.CreatedDate = DateTime.UtcNow;
                supplier.UpdatedDate = DateTime.UtcNow;
                await _supplierRepository.CreateAsync(supplier);

                await _auditLogService.LogActivityAsync(
                    "SUPPLIER_CREATED",
                    executedBy,
                    supplier.CompanyName,
                    $"Added new supplier '{supplier.CompanyName}' ({supplier.Email})");

                return (true, "Supplier account added successfully.", supplier);
            }
            else
            {
                if (existing != null && existing.Id != supplier.Id)
                {
                    return (false, $"Another supplier with name '{supplier.CompanyName}' already exists.", null);
                }

                var currentRecord = await _supplierRepository.GetByIdAsync(supplier.Id);
                if (currentRecord != null)
                {
                    // Handle password update logic
                    if (!string.IsNullOrWhiteSpace(supplier.PasswordHash) && !supplier.PasswordHash.StartsWith("$2"))
                    {
                        supplier.PasswordHash = BCrypt.Net.BCrypt.HashPassword(supplier.PasswordHash);
                    }
                    else
                    {
                        supplier.PasswordHash = currentRecord.PasswordHash;
                    }
                }

                if (string.IsNullOrWhiteSpace(supplier.Status)) supplier.Status = currentRecord?.Status ?? "Active";

                supplier.UpdatedDate = DateTime.UtcNow;
                await _supplierRepository.UpdateAsync(supplier.Id, supplier);

                await _auditLogService.LogActivityAsync(
                    "SUPPLIER_UPDATED",
                    executedBy,
                    supplier.CompanyName,
                    $"Updated supplier account '{supplier.CompanyName}'");

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
                supplier.CompanyName,
                $"Deleted supplier '{supplier.CompanyName}' along with {supplierProducts.Count} product(s) and {supplierCategories.Count} category/categories.");

            return (true, $"Supplier '{supplier.CompanyName}' and associated catalog products deleted successfully.");
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
