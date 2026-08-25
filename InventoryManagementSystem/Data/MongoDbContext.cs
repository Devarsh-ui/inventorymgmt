using Microsoft.Extensions.Options;
using MongoDB.Driver;
using InventoryManagementSystem.Configuration;
using InventoryManagementSystem.Models;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Data
{
    public class MongoDbContext
    {
        private readonly IMongoDatabase _database;

        public MongoDbContext(IMongoClient mongoClient, IOptions<MongoDbSettings> settings)
        {
            _database = mongoClient.GetDatabase(settings.Value.DatabaseName);
        }

        public MongoDbContext(IOptions<MongoDbSettings> settings)
        {
            var client = new MongoClient(settings.Value.ConnectionString);
            _database = client.GetDatabase(settings.Value.DatabaseName);
        }

        public IMongoCollection<T> GetCollection<T>(string name)
        {
            return _database.GetCollection<T>(name);
        }

        // Strongly-typed collections
        public IMongoCollection<User> Users => GetCollection<User>("Users");
        public IMongoCollection<Role> Roles => GetCollection<Role>("Roles");
        public IMongoCollection<AuditLog> AuditLogs => GetCollection<AuditLog>("AuditLogs");
        public IMongoCollection<Notification> Notifications => GetCollection<Notification>("Notifications");
        public IMongoCollection<Settings> Settings => GetCollection<Settings>("Settings");
        public IMongoCollection<Category> Categories => GetCollection<Category>("Categories");
        public IMongoCollection<Product> Products => GetCollection<Product>("Products");
        public IMongoCollection<StockTransaction> StockTransactions => GetCollection<StockTransaction>("StockTransactions");
        public IMongoCollection<Sale> Sales => GetCollection<Sale>("Sales");

        // Mobile Shop Collections
        public IMongoCollection<Device> Devices => GetCollection<Device>("Devices");
        public IMongoCollection<Customer> Customers => GetCollection<Customer>("Customers");
        public IMongoCollection<Supplier> Suppliers => GetCollection<Supplier>("Suppliers");
        public IMongoCollection<ReturnRecord> ReturnRecords => GetCollection<ReturnRecord>("ReturnRecords");
        public IMongoCollection<ExchangeRecord> ExchangeRecords => GetCollection<ExchangeRecord>("ExchangeRecords");
        public IMongoCollection<RepairTicket> RepairTickets => GetCollection<RepairTicket>("RepairTickets");
        public IMongoCollection<SupplierOrder> SupplierOrders => GetCollection<SupplierOrder>("SupplierOrders");
        public IMongoCollection<SupplierPurchaseReturn> SupplierPurchaseReturns => GetCollection<SupplierPurchaseReturn>("SupplierPurchaseReturns");

        /// <summary>
        /// Automatically verifies and creates database indexes on startup for fast queries.
        /// </summary>
        public async Task InitializeIndexesAsync()
        {
            try
            {
                // SupplierPurchaseReturns Indexes
                var returnNumIndex = new CreateIndexModel<SupplierPurchaseReturn>(
                    Builders<SupplierPurchaseReturn>.IndexKeys.Ascending(r => r.ReturnNumber),
                    new CreateIndexOptions { Unique = true, Sparse = true });
                var returnSupIndex = new CreateIndexModel<SupplierPurchaseReturn>(
                    Builders<SupplierPurchaseReturn>.IndexKeys.Ascending(r => r.SupplierId).Descending(r => r.CreatedAt));
                var returnStatusIndex = new CreateIndexModel<SupplierPurchaseReturn>(
                    Builders<SupplierPurchaseReturn>.IndexKeys.Ascending(r => r.Status));
                await SupplierPurchaseReturns.Indexes.CreateManyAsync(new[] { returnNumIndex, returnSupIndex, returnStatusIndex });
                // Products Indexes
                try { await Products.Indexes.DropOneAsync("Code_1"); } catch { }

                var productCodeIndex = new CreateIndexModel<Product>(
                    Builders<Product>.IndexKeys.Ascending(p => p.SupplierId).Ascending(p => p.Code));
                var productCatIndex = new CreateIndexModel<Product>(
                    Builders<Product>.IndexKeys.Ascending(p => p.CategoryId));
                var productStatusIndex = new CreateIndexModel<Product>(
                    Builders<Product>.IndexKeys.Ascending(p => p.Status));
                var productBrandIndex = new CreateIndexModel<Product>(
                    Builders<Product>.IndexKeys.Ascending(p => p.Brand));
                await Products.Indexes.CreateManyAsync(new[] { productCodeIndex, productCatIndex, productStatusIndex, productBrandIndex });

                // Devices (IMEI) Indexes
                try { await Devices.Indexes.DropOneAsync("IMEI2_1"); } catch { }
                try { await Devices.Indexes.DropOneAsync("SerialNumber_1"); } catch { }

                var deviceImei1Index = new CreateIndexModel<Device>(
                    Builders<Device>.IndexKeys.Ascending(d => d.IMEI1),
                    new CreateIndexOptions { Unique = true, Sparse = true });
                var deviceImei2Index = new CreateIndexModel<Device>(
                    Builders<Device>.IndexKeys.Ascending(d => d.IMEI2),
                    new CreateIndexOptions { Unique = true, Sparse = true });
                var deviceSerialIndex = new CreateIndexModel<Device>(
                    Builders<Device>.IndexKeys.Ascending(d => d.SerialNumber),
                    new CreateIndexOptions { Sparse = true });
                var deviceProdIndex = new CreateIndexModel<Device>(
                    Builders<Device>.IndexKeys.Ascending(d => d.ProductId).Ascending(d => d.Status));
                await Devices.Indexes.CreateManyAsync(new[] { deviceImei1Index, deviceImei2Index, deviceSerialIndex, deviceProdIndex });

                // Customers Indexes
                var customerPhoneIndex = new CreateIndexModel<Customer>(
                    Builders<Customer>.IndexKeys.Ascending(c => c.Phone),
                    new CreateIndexOptions { Unique = true, Sparse = true });
                await Customers.Indexes.CreateOneAsync(customerPhoneIndex);

                // Suppliers Indexes
                var supplierNameIndex = new CreateIndexModel<Supplier>(
                    Builders<Supplier>.IndexKeys.Ascending(s => s.CompanyName),
                    new CreateIndexOptions { Unique = true, Sparse = true });
                await Suppliers.Indexes.CreateOneAsync(supplierNameIndex);

                // Categories Indexes
                try { await Categories.Indexes.DropOneAsync("Name_1"); } catch { }

                var categoryNameIndex = new CreateIndexModel<Category>(
                    Builders<Category>.IndexKeys.Ascending(c => c.SupplierId).Ascending(c => c.Name));
                await Categories.Indexes.CreateOneAsync(categoryNameIndex);

                // StockTransactions Indexes
                var txTimestampIndex = new CreateIndexModel<StockTransaction>(
                    Builders<StockTransaction>.IndexKeys.Descending(t => t.Timestamp));
                var txProductIndex = new CreateIndexModel<StockTransaction>(
                    Builders<StockTransaction>.IndexKeys.Ascending(t => t.ProductId).Descending(t => t.Timestamp));
                await StockTransactions.Indexes.CreateManyAsync(new[] { txTimestampIndex, txProductIndex });

                // Sales Indexes
                var saleInvoiceIndex = new CreateIndexModel<Sale>(
                    Builders<Sale>.IndexKeys.Ascending(s => s.InvoiceNumber),
                    new CreateIndexOptions { Unique = true, Sparse = true });
                var saleDateIndex = new CreateIndexModel<Sale>(
                    Builders<Sale>.IndexKeys.Descending(s => s.Date));
                await Sales.Indexes.CreateManyAsync(new[] { saleInvoiceIndex, saleDateIndex });

                // RepairTickets Indexes
                var repairTicketIndex = new CreateIndexModel<RepairTicket>(
                    Builders<RepairTicket>.IndexKeys.Ascending(r => r.TicketNumber),
                    new CreateIndexOptions { Unique = true, Sparse = true });
                await RepairTickets.Indexes.CreateOneAsync(repairTicketIndex);

                // Notifications Indexes
                var notifReadIndex = new CreateIndexModel<Notification>(
                    Builders<Notification>.IndexKeys.Ascending(n => n.IsRead).Descending(n => n.Timestamp));
                await Notifications.Indexes.CreateOneAsync(notifReadIndex);

                // SupplierOrders Indexes
                var supplierOrderNumIndex = new CreateIndexModel<SupplierOrder>(
                    Builders<SupplierOrder>.IndexKeys.Ascending(so => so.OrderNumber),
                    new CreateIndexOptions { Unique = true, Sparse = true });
                var supplierOrderSupIndex = new CreateIndexModel<SupplierOrder>(
                    Builders<SupplierOrder>.IndexKeys.Ascending(so => so.SupplierId).Descending(so => so.CreatedAt));
                var supplierOrderStatusIndex = new CreateIndexModel<SupplierOrder>(
                    Builders<SupplierOrder>.IndexKeys.Ascending(so => so.Status));
                await SupplierOrders.Indexes.CreateManyAsync(new[] { supplierOrderNumIndex, supplierOrderSupIndex, supplierOrderStatusIndex });

                // Users Indexes
                var userUsernameIndex = new CreateIndexModel<User>(
                    Builders<User>.IndexKeys.Ascending(u => u.Username),
                    new CreateIndexOptions { Unique = true, Sparse = true });
                await Users.Indexes.CreateOneAsync(userUsernameIndex);
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"[MongoDB Indexing Notice] Index creation skipped or partially applied: {ex.Message}");
            }
        }
    }
}
