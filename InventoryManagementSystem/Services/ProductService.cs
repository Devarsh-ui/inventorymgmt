using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Services
{
    public class ProductService : IProductService
    {
        private readonly IProductRepository _productRepository;

        public ProductService(IProductRepository productRepository)
        {
            _productRepository = productRepository;
        }

        public async Task<IEnumerable<Product>> GetAllProductsAsync()
        {
            var all = await _productRepository.GetAllAsync();
            return all.Where(p => string.IsNullOrWhiteSpace(p.SupplierId));
        }

        public async Task<Product?> GetProductByIdAsync(string id)
        {
            return await _productRepository.GetByIdAsync(id);
        }

        public async Task<Product?> GetProductByCodeAsync(string code, string? supplierId = null)
        {
            return await _productRepository.GetByCodeAsync(code, supplierId);
        }

        public async Task<Product?> GetProductByBarcodeAsync(string barcode, string? supplierId = null)
        {
            return await _productRepository.GetByBarcodeAsync(barcode, supplierId);
        }

        public async Task CreateProductAsync(Product product)
        {
            SanitizeProduct(product);
            product.CreatedDate = DateTime.UtcNow;
            product.UpdatedDate = DateTime.UtcNow;
            await _productRepository.CreateAsync(product);
        }

        public async Task UpdateProductAsync(Product product)
        {
            if (string.IsNullOrWhiteSpace(product.Id))
            {
                Console.WriteLine("[SERVICE DIAGNOSTICS] UpdateProductAsync called with empty/null product.Id — aborting.");
                return;
            }

            SanitizeProduct(product);
            product.UpdatedDate = DateTime.UtcNow;
            await _productRepository.UpdateAsync(product.Id, product);
        }

        private static void SanitizeProduct(Product product)
        {
            if (product != null && (string.IsNullOrWhiteSpace(product.CategoryId) || !MongoDB.Bson.ObjectId.TryParse(product.CategoryId, out _)))
            {
                product.CategoryId = null;
            }
        }

        public async Task<bool> UpdateStockAsync(string id, int newStock)
        {
            var existing = await _productRepository.GetByIdAsync(id);
            if (existing != null)
            {
                existing.CurrentStock = newStock;
                existing.UpdatedDate = DateTime.UtcNow;
                await _productRepository.UpdateAsync(existing.Id, existing);
                return true;
            }
            return false;
        }

        public async Task DeleteProductAsync(string id)
        {
            await _productRepository.DeleteAsync(id);
        }

        public async Task<IEnumerable<Product>> GetPagedProductsAsync(
            string? search, string? categoryId, string? sortBy, bool isDescending, int page, int pageSize,
            string? brand = null, string? modelName = null, string? stockStatus = null, string? statusFilter = null,
            decimal? minPrice = null, decimal? maxPrice = null, int? minStock = null, int? maxStock = null, string? productSource = null)
        {
            return await _productRepository.GetPagedProductsAsync(search, categoryId, sortBy, isDescending, page, pageSize, brand, modelName, stockStatus, statusFilter, minPrice, maxPrice, minStock, maxStock, productSource);
        }

        public async Task<long> GetFilteredCountAsync(
            string? search, string? categoryId,
            string? brand = null, string? modelName = null, string? stockStatus = null, string? statusFilter = null,
            decimal? minPrice = null, decimal? maxPrice = null, int? minStock = null, int? maxStock = null, string? productSource = null)
        {
            return await _productRepository.GetFilteredCountAsync(search, categoryId, brand, modelName, stockStatus, statusFilter, minPrice, maxPrice, minStock, maxStock, productSource);
        }
    }
}
