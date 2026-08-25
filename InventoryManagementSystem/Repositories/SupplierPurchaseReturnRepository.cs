using MongoDB.Bson;
using MongoDB.Driver;
using InventoryManagementSystem.Data;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Repositories
{
    public class SupplierPurchaseReturnRepository : BaseRepository<SupplierPurchaseReturn>, ISupplierPurchaseReturnRepository
    {
        public SupplierPurchaseReturnRepository(MongoDbContext context) : base(context, "SupplierPurchaseReturns")
        {
        }

        public async Task<SupplierPurchaseReturn?> GetByReturnNumberAsync(string returnNumber)
        {
            if (string.IsNullOrWhiteSpace(returnNumber)) return null;
            var filter = Builders<SupplierPurchaseReturn>.Filter.Eq(r => r.ReturnNumber, returnNumber.Trim());
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<SupplierPurchaseReturn>> GetPagedReturnsAsync(string? search, string? supplierId, string? status, string? reason, int page, int pageSize)
        {
            var filter = BuildFilter(search, supplierId, status, reason);
            return await _collection.Find(filter)
                .SortByDescending(r => r.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();
        }

        public async Task<long> GetFilteredCountAsync(string? search, string? supplierId, string? status, string? reason)
        {
            var filter = BuildFilter(search, supplierId, status, reason);
            return await _collection.CountDocumentsAsync(filter);
        }

        public async Task<IEnumerable<SupplierPurchaseReturn>> GetSupplierReturnsAsync(string supplierId, string? status, int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(supplierId)) return new List<SupplierPurchaseReturn>();
            var filter = BuildFilter(null, supplierId, status, null);
            return await _collection.Find(filter)
                .SortByDescending(r => r.CreatedAt)
                .Limit(limit)
                .ToListAsync();
        }

        public async Task<IEnumerable<SupplierPurchaseReturn>> GetReturnsForOrderAsync(string purchaseOrderId)
        {
            if (string.IsNullOrWhiteSpace(purchaseOrderId)) return new List<SupplierPurchaseReturn>();
            var filter = Builders<SupplierPurchaseReturn>.Filter.Eq(r => r.PurchaseOrderId, purchaseOrderId);
            return await _collection.Find(filter).ToListAsync();
        }

        public async Task<Dictionary<string, long>> GetReturnStatusCountsAsync(string? supplierId = null)
        {
            var builder = Builders<SupplierPurchaseReturn>.Filter;
            var filter = !string.IsNullOrWhiteSpace(supplierId)
                ? builder.Eq(r => r.SupplierId, supplierId)
                : builder.Empty;

            var list = await _collection.Find(filter).ToListAsync();
            var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var st in PurchaseReturnStatus.AllStatuses)
            {
                result[st] = list.Count(r => r.Status.Equals(st, StringComparison.OrdinalIgnoreCase));
            }

            result["Total"] = list.Count;
            return result;
        }

        private FilterDefinition<SupplierPurchaseReturn> BuildFilter(string? search, string? supplierId, string? status, string? reason)
        {
            var builder = Builders<SupplierPurchaseReturn>.Filter;
            var filters = new List<FilterDefinition<SupplierPurchaseReturn>>();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                var searchFilter = builder.Or(
                    builder.Regex(r => r.ReturnNumber, new BsonRegularExpression(s, "i")),
                    builder.Regex(r => r.PurchaseOrderNumber, new BsonRegularExpression(s, "i")),
                    builder.Regex(r => r.SupplierName, new BsonRegularExpression(s, "i")),
                    builder.Regex(r => r.CreatedBy, new BsonRegularExpression(s, "i")),
                    builder.ElemMatch(r => r.Items, item => item.ProductName.Contains(s) || item.Brand.Contains(s) || item.ModelName.Contains(s)),
                    builder.ElemMatch(r => r.DeviceDetails, d => d.IMEI1.Contains(s) || d.SerialNumber.Contains(s))
                );
                filters.Add(searchFilter);
            }

            if (!string.IsNullOrWhiteSpace(supplierId))
            {
                filters.Add(builder.Eq(r => r.SupplierId, supplierId));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                filters.Add(builder.Eq(r => r.Status, status.Trim()));
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                filters.Add(builder.Eq(r => r.Reason, reason.Trim()));
            }

            return filters.Any() ? builder.And(filters) : builder.Empty;
        }
    }
}
