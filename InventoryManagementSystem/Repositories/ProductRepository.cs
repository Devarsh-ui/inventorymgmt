using MongoDB.Driver;
using InventoryManagementSystem.Data;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Repositories
{
    public class ProductRepository : BaseRepository<Product>, IProductRepository
    {
        public ProductRepository(MongoDbContext context) : base(context, "Products")
        {
        }

        public async Task<Product?> GetByCodeAsync(string code, string? supplierId = null)
        {
            var filter = Builders<Product>.Filter.Eq(p => p.Code, code);
            if (string.IsNullOrEmpty(supplierId))
            {
                var shopFilter = Builders<Product>.Filter.Or(
                    Builders<Product>.Filter.Eq(p => p.SupplierId, null),
                    Builders<Product>.Filter.Exists(p => p.SupplierId, false)
                );
                filter = Builders<Product>.Filter.And(filter, shopFilter);
            }
            else
            {
                filter = Builders<Product>.Filter.And(filter, Builders<Product>.Filter.Eq(p => p.SupplierId, supplierId));
            }
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<Product?> GetByBarcodeAsync(string barcode, string? supplierId = null)
        {
            var filter = Builders<Product>.Filter.Eq(p => p.Barcode, barcode);
            if (string.IsNullOrEmpty(supplierId))
            {
                var shopFilter = Builders<Product>.Filter.Or(
                    Builders<Product>.Filter.Eq(p => p.SupplierId, null),
                    Builders<Product>.Filter.Exists(p => p.SupplierId, false)
                );
                filter = Builders<Product>.Filter.And(filter, shopFilter);
            }
            else
            {
                filter = Builders<Product>.Filter.And(filter, Builders<Product>.Filter.Eq(p => p.SupplierId, supplierId));
            }
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<Product>> GetPagedProductsAsync(
            string? search, string? categoryId, string? sortBy, bool isDescending, int page, int pageSize,
            string? brand = null, string? modelName = null, string? stockStatus = null, string? statusFilter = null,
            decimal? minPrice = null, decimal? maxPrice = null, int? minStock = null, int? maxStock = null, string? productSource = null)
        {
            var filter = BuildFilter(search, categoryId, brand, modelName, stockStatus, statusFilter, minPrice, maxPrice, minStock, maxStock, productSource);
            var query = _collection.Find(filter);

            query = sortBy switch
            {
                "Name" => isDescending ? query.SortByDescending(p => p.Name) : query.SortBy(p => p.Name),
                "Code" => isDescending ? query.SortByDescending(p => p.Code) : query.SortBy(p => p.Code),
                "SellingPrice" => isDescending ? query.SortByDescending(p => p.SellingPrice) : query.SortBy(p => p.SellingPrice),
                "CurrentStock" => isDescending ? query.SortByDescending(p => p.CurrentStock) : query.SortBy(p => p.CurrentStock),
                "Brand" => isDescending ? query.SortByDescending(p => p.Brand) : query.SortBy(p => p.Brand),
                "ModelName" => isDescending ? query.SortByDescending(p => p.ModelName) : query.SortBy(p => p.ModelName),
                _ => isDescending ? query.SortByDescending(p => p.CreatedDate) : query.SortBy(p => p.CreatedDate)
            };

            return await query.Skip((page - 1) * pageSize).Limit(pageSize).ToListAsync();
        }

        public async Task<long> GetFilteredCountAsync(
            string? search, string? categoryId,
            string? brand = null, string? modelName = null, string? stockStatus = null, string? statusFilter = null,
            decimal? minPrice = null, decimal? maxPrice = null, int? minStock = null, int? maxStock = null, string? productSource = null)
        {
            var filter = BuildFilter(search, categoryId, brand, modelName, stockStatus, statusFilter, minPrice, maxPrice, minStock, maxStock, productSource);
            return await _collection.CountDocumentsAsync(filter);
        }

        public async Task<(int TotalProducts, int CurrentStockSum, int LowStockCount, int OutOfStockCount)> GetStockMetricsAsync()
        {
            var shopFilter = Builders<Product>.Filter.Or(
                Builders<Product>.Filter.Eq(p => p.SupplierId, null),
                Builders<Product>.Filter.Exists(p => p.SupplierId, false)
            );
            var products = await _collection.Find(shopFilter).ToListAsync();
            int totalProducts = products.Count;
            int currentStockSum = 0;
            int lowStockCount = 0;
            int outOfStockCount = 0;

            foreach (var p in products)
            {
                if (p == null) continue;
                currentStockSum += p.CurrentStock;
                if (p.Status == "Active" || string.IsNullOrEmpty(p.Status))
                {
                    if (p.CurrentStock == 0) outOfStockCount++;
                    else if (p.CurrentStock <= p.MinimumStock) lowStockCount++;
                }
            }

            return (totalProducts, currentStockSum, lowStockCount, outOfStockCount);
        }

        private FilterDefinition<Product> BuildFilter(
            string? search, string? categoryId,
            string? brand = null, string? modelName = null, string? stockStatus = null, string? statusFilter = null,
            decimal? minPrice = null, decimal? maxPrice = null, int? minStock = null, int? maxStock = null, string? productSource = null)
        {
            var filter = Builders<Product>.Filter.Empty;

            if (!string.IsNullOrWhiteSpace(productSource))
            {
                if (productSource.Equals("Shop", StringComparison.OrdinalIgnoreCase) || productSource.Equals("Store", StringComparison.OrdinalIgnoreCase) || productSource.Equals("Main", StringComparison.OrdinalIgnoreCase))
                {
                    var shopFilter = Builders<Product>.Filter.Or(
                        Builders<Product>.Filter.Eq(p => p.SupplierId, null),
                        Builders<Product>.Filter.Exists(p => p.SupplierId, false)
                    );
                    filter = Builders<Product>.Filter.And(filter, shopFilter);
                }
                else if (productSource.Equals("Supplier", StringComparison.OrdinalIgnoreCase))
                {
                    var supplierFilter = Builders<Product>.Filter.And(
                        Builders<Product>.Filter.Ne(p => p.SupplierId, null),
                        Builders<Product>.Filter.Exists(p => p.SupplierId, true)
                    );
                    filter = Builders<Product>.Filter.And(filter, supplierFilter);
                }
                else if (productSource.Equals("TradeIn", StringComparison.OrdinalIgnoreCase) || productSource.Equals("Exchange", StringComparison.OrdinalIgnoreCase))
                {
                    var tradeInFilter = Builders<Product>.Filter.Or(
                        Builders<Product>.Filter.Regex(p => p.Code, new global::MongoDB.Bson.BsonRegularExpression("^EXCH-", "i")),
                        Builders<Product>.Filter.Regex(p => p.Name, new global::MongoDB.Bson.BsonRegularExpression("Pre-Owned|Trade-In|Exchange", "i"))
                    );
                    filter = Builders<Product>.Filter.And(filter, tradeInFilter);
                }
                else if (productSource.Equals("New", StringComparison.OrdinalIgnoreCase) || productSource.Equals("Standard", StringComparison.OrdinalIgnoreCase))
                {
                    var newProductFilter = Builders<Product>.Filter.And(
                        Builders<Product>.Filter.Not(Builders<Product>.Filter.Regex(p => p.Code, new global::MongoDB.Bson.BsonRegularExpression("^EXCH-", "i"))),
                        Builders<Product>.Filter.Not(Builders<Product>.Filter.Regex(p => p.Name, new global::MongoDB.Bson.BsonRegularExpression("Pre-Owned|Trade-In", "i")))
                    );
                    filter = Builders<Product>.Filter.And(filter, newProductFilter);
                }
            }
            else
            {
                // Default catalog filter: show shop products (not external supplier catalog proposals)
                var shopFilter = Builders<Product>.Filter.Or(
                    Builders<Product>.Filter.Eq(p => p.SupplierId, null),
                    Builders<Product>.Filter.Exists(p => p.SupplierId, false)
                );
                filter = Builders<Product>.Filter.And(filter, shopFilter);
            }

            if (!string.IsNullOrEmpty(categoryId))
            {
                filter = Builders<Product>.Filter.And(filter, Builders<Product>.Filter.Eq(p => p.CategoryId, categoryId));
            }

            if (!string.IsNullOrWhiteSpace(brand))
            {
                filter = Builders<Product>.Filter.And(filter, Builders<Product>.Filter.Regex(p => p.Brand, new global::MongoDB.Bson.BsonRegularExpression(brand.Trim(), "i")));
            }

            if (!string.IsNullOrWhiteSpace(modelName))
            {
                filter = Builders<Product>.Filter.And(filter, Builders<Product>.Filter.Regex(p => p.ModelName, new global::MongoDB.Bson.BsonRegularExpression(modelName.Trim(), "i")));
            }

            if (!string.IsNullOrWhiteSpace(statusFilter))
            {
                filter = Builders<Product>.Filter.And(filter, Builders<Product>.Filter.Eq(p => p.Status, statusFilter.Trim()));
            }

            if (stockStatus == "OutOfStock")
            {
                filter = Builders<Product>.Filter.And(filter, Builders<Product>.Filter.Lte(p => p.CurrentStock, 0));
            }
            else if (stockStatus == "InStock")
            {
                filter = Builders<Product>.Filter.And(filter, Builders<Product>.Filter.Gt(p => p.CurrentStock, 0));
            }

            if (minPrice.HasValue)
            {
                filter = Builders<Product>.Filter.And(filter, Builders<Product>.Filter.Gte(p => p.SellingPrice, minPrice.Value));
            }

            if (maxPrice.HasValue)
            {
                filter = Builders<Product>.Filter.And(filter, Builders<Product>.Filter.Lte(p => p.SellingPrice, maxPrice.Value));
            }

            if (minStock.HasValue)
            {
                filter = Builders<Product>.Filter.And(filter, Builders<Product>.Filter.Gte(p => p.CurrentStock, minStock.Value));
            }

            if (maxStock.HasValue)
            {
                filter = Builders<Product>.Filter.And(filter, Builders<Product>.Filter.Lte(p => p.CurrentStock, maxStock.Value));
            }

            if (!string.IsNullOrEmpty(search))
            {
                var s = search.Trim();
                var searchFilter = Builders<Product>.Filter.Or(
                    Builders<Product>.Filter.Regex(p => p.Name, new global::MongoDB.Bson.BsonRegularExpression(s, "i")),
                    Builders<Product>.Filter.Regex(p => p.Code, new global::MongoDB.Bson.BsonRegularExpression(s, "i")),
                    Builders<Product>.Filter.Regex(p => p.Barcode, new global::MongoDB.Bson.BsonRegularExpression(s, "i")),
                    Builders<Product>.Filter.Regex(p => p.Brand, new global::MongoDB.Bson.BsonRegularExpression(s, "i")),
                    Builders<Product>.Filter.Regex(p => p.ModelName, new global::MongoDB.Bson.BsonRegularExpression(s, "i")),
                    Builders<Product>.Filter.Regex(p => p.Variant, new global::MongoDB.Bson.BsonRegularExpression(s, "i"))
                );
                filter = Builders<Product>.Filter.And(filter, searchFilter);
            }

            return filter;
        }
    }
}
