using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace InventoryManagementSystem.Models
{
    public class Product : ITenantEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("TenantId")]
        public string? TenantId { get; set; }

        [BsonElement("Name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("Code")]
        public string Code { get; set; } = string.Empty; // SKU code

        [BsonElement("Barcode")]
        public string Barcode { get; set; } = string.Empty;

        [BsonElement("CategoryId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        [BsonIgnoreIfDefault]
        public string? CategoryId { get; set; }

        // Mobile Shop Specific Classification
        [BsonElement("ProductType")]
        public string ProductType { get; set; } = "Accessory"; // Smartphone, Feature Phone, Tablet, Smartwatch, Accessory, Charger, Cable, Mobile Cover, Tempered Glass, Earphones, Headphones, Power Bank, Memory Card, Speaker, Other

        [BsonElement("Brand")]
        public string Brand { get; set; } = string.Empty;

        [BsonElement("ModelName")]
        public string ModelName { get; set; } = string.Empty;

        [BsonElement("ModelNumber")]
        public string ModelNumber { get; set; } = string.Empty;

        [BsonElement("Variant")]
        public string Variant { get; set; } = string.Empty; // e.g., 8GB RAM / 128GB Storage

        [BsonElement("Color")]
        public string Color { get; set; } = string.Empty;

        // Embedded Detailed Hardware Specifications Sub-Model
        [BsonElement("Specs")]
        public MobileSpecifications Specs { get; set; } = new MobileSpecifications();

        // Legacy / Convenience Hardware Specification Shortcuts
        [BsonElement("Ram")]
        public string Ram { get; set; } = string.Empty;

        [BsonElement("Storage")]
        public string Storage { get; set; } = string.Empty;

        [BsonElement("Processor")]
        public string Processor
        {
            get => !string.IsNullOrWhiteSpace(Specs?.ProcessorName) ? Specs.ProcessorName : _processor;
            set { _processor = value; if (Specs != null && string.IsNullOrWhiteSpace(Specs.ProcessorName)) Specs.ProcessorName = value; }
        }
        private string _processor = string.Empty;

        [BsonElement("DisplaySize")]
        public string DisplaySize
        {
            get => !string.IsNullOrWhiteSpace(Specs?.DisplaySize) ? Specs.DisplaySize : _displaySize;
            set { _displaySize = value; if (Specs != null && string.IsNullOrWhiteSpace(Specs.DisplaySize)) Specs.DisplaySize = value; }
        }
        private string _displaySize = string.Empty;

        [BsonElement("BatteryCapacity")]
        public string BatteryCapacity
        {
            get => (Specs != null && Specs.BatteryCapacityMah > 0) ? $"{Specs.BatteryCapacityMah} mAh" : _batteryCapacity;
            set { _batteryCapacity = value; }
        }
        private string _batteryCapacity = string.Empty;

        [BsonElement("OperatingSystem")]
        public string OperatingSystem
        {
            get => !string.IsNullOrWhiteSpace(Specs?.OperatingSystem) ? Specs.OperatingSystem : _operatingSystem;
            set { _operatingSystem = value; if (Specs != null && string.IsNullOrWhiteSpace(Specs.OperatingSystem)) Specs.OperatingSystem = value; }
        }
        private string _operatingSystem = string.Empty;

        [BsonElement("NetworkSupport")]
        public string NetworkSupport
        {
            get => (Specs != null && Specs.Network5G) ? "5G / 4G / VoLTE" : _networkSupport;
            set { _networkSupport = value; }
        }
        private string _networkSupport = string.Empty;

        [BsonElement("SimType")]
        public string SimType
        {
            get => !string.IsNullOrWhiteSpace(Specs?.SimType) ? Specs.SimType : _simType;
            set { _simType = value; if (Specs != null && string.IsNullOrWhiteSpace(Specs.SimType)) Specs.SimType = value; }
        }
        private string _simType = string.Empty;

        // Commercials & Supplier Linkage
        [BsonElement("SupplierId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        [BsonIgnoreIfDefault]
        public string? SupplierId { get; set; }

        [BsonElement("SupplierName")]
        public string SupplierName { get; set; } = string.Empty;

        [BsonElement("SupplierVendorName")]
        public string SupplierVendorName { get; set; } = string.Empty;

        [BsonElement("SupplierCompanyName")]
        public string SupplierCompanyName { get; set; } = string.Empty;

        [BsonIgnore]
        public string DisplaySupplierVendorName => !string.IsNullOrWhiteSpace(SupplierVendorName) ? SupplierVendorName : SupplierName;

        [BsonIgnore]
        public string DisplaySupplierCompanyName => !string.IsNullOrWhiteSpace(SupplierCompanyName) ? SupplierCompanyName : (!string.IsNullOrWhiteSpace(SupplierVendorName) ? SupplierVendorName : SupplierName);

        [BsonElement("SupplierPrice")]
        public decimal SupplierPrice { get; set; }

        [BsonElement("PurchasePrice")]
        public decimal PurchasePrice { get; set; }

        [BsonElement("SellingPrice")]
        public decimal SellingPrice { get; set; }

        [BsonElement("Mrp")]
        public decimal Mrp { get; set; }

        [BsonElement("GstPercentage")]
        public decimal GstPercentage { get; set; } = 18.0m;

        [BsonElement("CurrentStock")]
        public int CurrentStock { get; set; }

        [BsonElement("MinimumStock")]
        public int MinimumStock { get; set; }

        [BsonElement("WarrantyDurationMonths")]
        public int WarrantyDurationMonths { get; set; } = 12;

        [BsonElement("IsImeiRequired")]
        public bool IsImeiRequired { get; set; } = false;

        [BsonElement("Description")]
        public string Description { get; set; } = string.Empty;

        [BsonElement("ImageUrl")]
        public string ImageUrl { get; set; } = string.Empty;

        [BsonElement("ImageUrls")]
        public List<string> ImageUrls { get; set; } = new List<string>();

        [BsonElement("ImagePublicId")]
        public string ImagePublicId { get; set; } = string.Empty;

        [BsonElement("ImageOriginalFilename")]
        public string ImageOriginalFilename { get; set; } = string.Empty;

        [BsonElement("Variants")]
        public List<ProductVariant> Variants { get; set; } = new List<ProductVariant>();

        [BsonIgnore]
        public List<ProductVariant> EffectiveVariants => GetEffectiveVariants();

        public List<ProductVariant> GetEffectiveVariants()
        {
            if (Variants != null && Variants.Any()) return Variants;

            var defaultColor = new ProductColor
            {
                ColorId = "default-color",
                Name = !string.IsNullOrWhiteSpace(Color) ? Color : "Default",
                ImageUrl = ImageUrl,
                ImageUrls = ImageUrls ?? new List<string>()
            };

            var defaultVariant = new ProductVariant
            {
                VariantId = "default-variant",
                Ram = Ram,
                Storage = Storage,
                Sku = Code,
                SupplierPrice = SupplierPrice,
                PurchasePrice = PurchasePrice,
                SellingPrice = SellingPrice,
                Mrp = Mrp,
                Colors = new List<ProductColor> { defaultColor }
            };

            return new List<ProductVariant> { defaultVariant };
        }

        [BsonIgnore]
        public string DisplayVariant => !string.IsNullOrWhiteSpace(Variant)
            ? Variant
            : (!string.IsNullOrWhiteSpace(Ram) && !string.IsNullOrWhiteSpace(Storage)
                ? $"{Ram} / {Storage}"
                : (!string.IsNullOrWhiteSpace(Storage) ? Storage : (!string.IsNullOrWhiteSpace(Ram) ? Ram : "-")));

        [BsonElement("Status")]
        public string Status { get; set; } = "Active"; // Active, Inactive

        [BsonElement("LastAlertSentType")]
        public string LastAlertSentType { get; set; } = "None"; // None, LowStock, OutOfStock

        [BsonElement("CreatedDate")]
        public DateTime CreatedDate { get; set; } = DateTime.UtcNow;

        [BsonElement("UpdatedDate")]
        public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;
    }

    public class ProductColor
    {
        [BsonElement("ColorId")]
        public string ColorId { get; set; } = Guid.NewGuid().ToString("N");

        [BsonElement("Name")]
        public string Name { get; set; } = string.Empty;

        [BsonElement("Code")]
        public string Code { get; set; } = string.Empty; // Hex color code e.g. #000000

        [BsonElement("ImageUrl")]
        public string ImageUrl { get; set; } = string.Empty;

        [BsonElement("ImageUrls")]
        public List<string> ImageUrls { get; set; } = new List<string>();

        [BsonElement("InitialStock")]
        public int InitialStock { get; set; }

        [BsonElement("CurrentStock")]
        public int CurrentStock { get; set; }

        [BsonElement("PurchasePrice")]
        public decimal PurchasePrice { get; set; }

        [BsonElement("SellingPrice")]
        public decimal SellingPrice { get; set; }
    }

    public class ProductVariant
    {
        [BsonElement("VariantId")]
        public string VariantId { get; set; } = Guid.NewGuid().ToString("N");

        [BsonElement("Ram")]
        public string Ram { get; set; } = string.Empty;

        [BsonElement("Storage")]
        public string Storage { get; set; } = string.Empty;

        [BsonElement("Sku")]
        public string Sku { get; set; } = string.Empty;

        [BsonElement("SupplierPrice")]
        public decimal SupplierPrice { get; set; }

        [BsonElement("PurchasePrice")]
        public decimal PurchasePrice { get; set; }

        [BsonElement("SellingPrice")]
        public decimal SellingPrice { get; set; }

        [BsonElement("Mrp")]
        public decimal Mrp { get; set; }

        [BsonElement("InitialStock")]
        public int InitialStock { get; set; }

        [BsonElement("CurrentStock")]
        public int CurrentStock { get; set; }

        [BsonElement("Colors")]
        public List<ProductColor> Colors { get; set; } = new List<ProductColor>();

        [BsonIgnore]
        public string DisplayVariantName => !string.IsNullOrWhiteSpace(Ram) && !string.IsNullOrWhiteSpace(Storage)
            ? $"{Ram} / {Storage}"
            : (!string.IsNullOrWhiteSpace(Storage) ? Storage : (!string.IsNullOrWhiteSpace(Ram) ? Ram : "Standard"));
    }
}
