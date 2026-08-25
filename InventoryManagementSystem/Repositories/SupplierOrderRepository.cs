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
    public class SupplierOrderRepository : BaseRepository<SupplierOrder>, ISupplierOrderRepository
    {
        public SupplierOrderRepository(MongoDbContext context) : base(context, "SupplierOrders")
        {
        }

        public async Task<SupplierOrder?> GetByOrderNumberAsync(string orderNumber)
        {
            if (string.IsNullOrWhiteSpace(orderNumber)) return null;
            var filter = Builders<SupplierOrder>.Filter.Eq(so => so.OrderNumber, orderNumber.Trim());
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public async Task<IEnumerable<SupplierOrder>> GetPagedOrdersAsync(
            string? search, string? supplierId, string? status, int page, int pageSize,
            DateTime? fromDate = null, DateTime? toDate = null, decimal? minAmount = null, decimal? maxAmount = null, string? sortBy = null)
        {
            var filter = BuildFilter(search, supplierId, status, fromDate, toDate, minAmount, maxAmount);
            var query = _collection.Find(filter);

            query = sortBy switch
            {
                "date_asc" => query.SortBy(so => so.CreatedAt),
                "amount_desc" => query.SortByDescending(so => so.GrandTotal),
                "amount_asc" => query.SortBy(so => so.GrandTotal),
                "order_asc" => query.SortBy(so => so.OrderNumber),
                _ => query.SortByDescending(so => so.CreatedAt)
            };

            return await query
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();
        }

        public async Task<long> GetFilteredCountAsync(
            string? search, string? supplierId, string? status,
            DateTime? fromDate = null, DateTime? toDate = null, decimal? minAmount = null, decimal? maxAmount = null)
        {
            var filter = BuildFilter(search, supplierId, status, fromDate, toDate, minAmount, maxAmount);
            return await _collection.CountDocumentsAsync(filter);
        }

        public async Task<IEnumerable<SupplierOrder>> GetSupplierOrdersAsync(string supplierId, string? status, int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(supplierId)) return new List<SupplierOrder>();
            var filter = BuildFilter(null, supplierId, status);
            return await _collection.Find(filter)
                .SortByDescending(so => so.CreatedAt)
                .Limit(limit)
                .ToListAsync();
        }

        public async Task<string> GetNextOrderNumberAsync()
        {
            var todayPrefix = $"PO-{DateTime.UtcNow:yyyyMMdd}-";
            var filter = Builders<SupplierOrder>.Filter.Regex(so => so.OrderNumber, new BsonRegularExpression($"^{todayPrefix}"));
            var count = await _collection.CountDocumentsAsync(filter);
            
            // Loop until unique sequence number found
            int sequence = (int)count + 1;
            while (true)
            {
                var candidate = $"{todayPrefix}{sequence:D4}";
                var existing = await GetByOrderNumberAsync(candidate);
                if (existing == null) return candidate;
                sequence++;
            }
        }

        public async Task<Dictionary<string, long>> GetOrderStatusCountsAsync(string? supplierId = null)
        {
            var builder = Builders<SupplierOrder>.Filter;
            var filter = !string.IsNullOrWhiteSpace(supplierId)
                ? builder.Eq(so => so.SupplierId, supplierId)
                : builder.Empty;

            var list = await _collection.Find(filter).ToListAsync();
            var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var st in SupplierOrderStatus.AllStatuses)
            {
                result[st] = list.Count(o => o.Status.Equals(st, StringComparison.OrdinalIgnoreCase));
            }

            result["Total"] = list.Count;
            return result;
        }

        private FilterDefinition<SupplierOrder> BuildFilter(
            string? search, string? supplierId, string? status,
            DateTime? fromDate = null, DateTime? toDate = null, decimal? minAmount = null, decimal? maxAmount = null)
        {
            var builder = Builders<SupplierOrder>.Filter;
            var filters = new List<FilterDefinition<SupplierOrder>>();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                var searchFilter = builder.Or(
                    builder.Regex(so => so.OrderNumber, new BsonRegularExpression(s, "i")),
                    builder.Regex(so => so.SupplierName, new BsonRegularExpression(s, "i")),
                    builder.Regex(so => so.SupplierEmail, new BsonRegularExpression(s, "i")),
                    builder.Regex(so => so.CreatedBy, new BsonRegularExpression(s, "i")),
                    builder.Regex(so => so.Notes, new BsonRegularExpression(s, "i")),
                    builder.Regex(so => so.SupplierNotes, new BsonRegularExpression(s, "i")),
                    builder.ElemMatch(so => so.Items, item => item.ProductName.Contains(s) || item.Brand.Contains(s) || item.Model.Contains(s))
                );
                filters.Add(searchFilter);
            }

            if (!string.IsNullOrWhiteSpace(supplierId))
            {
                filters.Add(builder.Eq(so => so.SupplierId, supplierId));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                filters.Add(builder.Eq(so => so.Status, status.Trim()));
            }

            if (fromDate.HasValue)
            {
                filters.Add(builder.Gte(so => so.CreatedAt, fromDate.Value.Date));
            }

            if (toDate.HasValue)
            {
                filters.Add(builder.Lte(so => so.CreatedAt, toDate.Value.Date.AddDays(1).AddTicks(-1)));
            }

            if (minAmount.HasValue)
            {
                filters.Add(builder.Gte(so => so.GrandTotal, minAmount.Value));
            }

            if (maxAmount.HasValue)
            {
                filters.Add(builder.Lte(so => so.GrandTotal, maxAmount.Value));
            }

            return filters.Any() ? builder.And(filters) : builder.Empty;
        }
    }
}
