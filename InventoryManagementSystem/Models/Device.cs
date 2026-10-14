using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace InventoryManagementSystem.Models
{
    public class Device : ITenantEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("TenantId")]
        public string? TenantId { get; set; }

        [BsonElement("ProductId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        [BsonIgnoreIfDefault]
        public string? ProductId { get; set; }

        [BsonElement("ProductCode")]
        public string ProductCode { get; set; } = string.Empty;

        [BsonElement("ProductName")]
        public string ProductName { get; set; } = string.Empty;

        [BsonElement("Brand")]
        public string Brand { get; set; } = string.Empty;

        [BsonElement("ModelName")]
        public string ModelName { get; set; } = string.Empty;

        [BsonElement("VariantId")]
        [BsonIgnoreIfNull]
        [BsonIgnoreIfDefault]
        public string? VariantId { get; set; }

        [BsonElement("ColorId")]
        [BsonIgnoreIfNull]
        [BsonIgnoreIfDefault]
        public string? ColorId { get; set; }

        [BsonElement("Variant")]
        public string Variant { get; set; } = string.Empty;

        [BsonElement("Color")]
        public string Color { get; set; } = string.Empty;

        // Physical Identification
        [BsonElement("IMEI1")]
        public string IMEI1 { get; set; } = string.Empty;

        [BsonElement("IMEI2")]
        [BsonIgnoreIfNull]
        public string? IMEI2 { get; set; }

        [BsonElement("SerialNumber")]
        [BsonIgnoreIfNull]
        public string? SerialNumber { get; set; }

        // Commercial Details
        [BsonElement("PurchasePrice")]
        public decimal PurchasePrice { get; set; }

        [BsonElement("SellingPrice")]
        public decimal SellingPrice { get; set; }

        // Device Status: InStock, Reserved, Sold, Returned, Exchanged, Damaged, UnderRepair
        [BsonElement("Status")]
        public string Status { get; set; } = "InStock";

        // Supplier Tracking
        [BsonElement("SupplierId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        [BsonIgnoreIfDefault]
        public string? SupplierId { get; set; }

        [BsonElement("SupplierName")]
        public string SupplierName { get; set; } = string.Empty;

        [BsonElement("SupplierInvoiceNumber")]
        public string SupplierInvoiceNumber { get; set; } = string.Empty;

        [BsonElement("PurchaseDate")]
        public DateTime PurchaseDate { get; set; } = DateTime.UtcNow;

        // Customer & Sale Tracking
        [BsonElement("SoldDate")]
        public DateTime? SoldDate { get; set; }

        [BsonElement("InvoiceNumber")]
        public string InvoiceNumber { get; set; } = string.Empty;

        [BsonElement("CustomerId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        [BsonIgnoreIfDefault]
        public string? CustomerId { get; set; }

        [BsonElement("CustomerName")]
        public string CustomerName { get; set; } = string.Empty;

        [BsonElement("CustomerPhone")]
        public string CustomerPhone { get; set; } = string.Empty;

        // Warranty Tracking
        [BsonElement("WarrantyStartDate")]
        public DateTime? WarrantyStartDate { get; set; }

        [BsonElement("WarrantyEndDate")]
        public DateTime? WarrantyEndDate { get; set; }

        // Trade-In & Origin Tracking
        [BsonElement("Source")]
        public string Source { get; set; } = "Stock In"; // Stock In, Trade-In, Return

        [BsonElement("ExchangeNumber")]
        public string ExchangeNumber { get; set; } = string.Empty;

        [BsonElement("Condition")]
        public string Condition { get; set; } = "Good";

        [BsonElement("BatteryHealthPercentage")]
        public int? BatteryHealthPercentage { get; set; }

        [BsonElement("Ram")]
        public string Ram { get; set; } = string.Empty;

        [BsonElement("Storage")]
        public string Storage { get; set; } = string.Empty;

        [BsonIgnore]
        public string DisplayVariant => !string.IsNullOrWhiteSpace(Variant)
            ? Variant
            : (!string.IsNullOrWhiteSpace(Ram) && !string.IsNullOrWhiteSpace(Storage)
                ? $"{Ram} / {Storage}"
                : (!string.IsNullOrWhiteSpace(Storage) ? Storage : (!string.IsNullOrWhiteSpace(Ram) ? Ram : "-")));

        [BsonElement("Processor")]
        public string Processor { get; set; } = string.Empty;

        [BsonElement("DisplaySpecs")]
        public string DisplaySpecs { get; set; } = string.Empty;

        [BsonElement("CameraSpecs")]
        public string CameraSpecs { get; set; } = string.Empty;

        [BsonElement("BatteryCapacity")]
        public string BatteryCapacity { get; set; } = string.Empty;

        [BsonElement("Notes")]
        public string Notes { get; set; } = string.Empty;

        [BsonElement("DevicePhotos")]
        public List<string> DevicePhotos { get; set; } = new List<string>();

        [BsonElement("CreatedBy")]
        public string CreatedBy { get; set; } = string.Empty;

        [BsonElement("CreatedDate")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [BsonElement("UpdatedDate")]
        public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
    }
}
