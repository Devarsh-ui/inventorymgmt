using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace InventoryManagementSystem.Models
{
    public class SupplierOrder
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("OrderNumber")]
        public string OrderNumber { get; set; } = string.Empty; // e.g. PO-20260819-0001

        [BsonElement("SupplierId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string SupplierId { get; set; } = string.Empty;

        [BsonElement("SupplierName")]
        public string SupplierName { get; set; } = string.Empty;

        [BsonElement("SupplierEmail")]
        public string SupplierEmail { get; set; } = string.Empty;

        [BsonElement("SupplierPhone")]
        public string SupplierPhone { get; set; } = string.Empty;

        [BsonElement("CreatedBy")]
        public string CreatedBy { get; set; } = string.Empty; // Admin username

        [BsonElement("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("UpdatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("Status")]
        public string Status { get; set; } = SupplierOrderStatus.Pending;

        [BsonElement("Items")]
        public List<SupplierOrderItem> Items { get; set; } = new List<SupplierOrderItem>();

        [BsonElement("TotalQuantity")]
        public int TotalQuantity { get; set; }

        [BsonElement("Subtotal")]
        public decimal Subtotal { get; set; }

        [BsonElement("Tax")]
        public decimal Tax { get; set; }

        [BsonElement("Discount")]
        public decimal Discount { get; set; }

        [BsonElement("GrandTotal")]
        public decimal GrandTotal { get; set; }

        [BsonElement("ExpectedDeliveryDate")]
        public DateTime? ExpectedDeliveryDate { get; set; }

        [BsonElement("Notes")]
        public string Notes { get; set; } = string.Empty;

        [BsonElement("SupplierNotes")]
        public string SupplierNotes { get; set; } = string.Empty;

        [BsonElement("EmailSent")]
        public bool EmailSent { get; set; } = false;

        [BsonElement("EmailSentAt")]
        public DateTime? EmailSentAt { get; set; }

        [BsonElement("EmailError")]
        public string EmailError { get; set; } = string.Empty;

        [BsonElement("SupplierStockDeducted")]
        public bool SupplierStockDeducted { get; set; } = false;
    }

    public class SupplierOrderItem
    {
        [BsonElement("ProductId")]
        public string ProductId { get; set; } = string.Empty;

        [BsonElement("ProductName")]
        public string ProductName { get; set; } = string.Empty;

        [BsonElement("Brand")]
        public string Brand { get; set; } = string.Empty;

        [BsonElement("Model")]
        public string Model { get; set; } = string.Empty;

        [BsonElement("Variant")]
        public string Variant { get; set; } = string.Empty;

        [BsonElement("Color")]
        public string Color { get; set; } = string.Empty;

        [BsonElement("Ram")]
        public string Ram { get; set; } = string.Empty;

        [BsonElement("Storage")]
        public string Storage { get; set; } = string.Empty;

        [BsonElement("ImageUrl")]
        public string ImageUrl { get; set; } = string.Empty;

        [BsonElement("Quantity")]
        public int Quantity { get; set; }

        [BsonElement("UnitPrice")]
        public decimal UnitPrice { get; set; }

        [BsonElement("Subtotal")]
        public decimal Subtotal { get; set; }

        [BsonElement("AvailableStock")]
        public int AvailableStock { get; set; }
    }

    public static class SupplierOrderStatus
    {
        public const string Pending = "Pending";
        public const string Accepted = "Accepted";
        public const string Rejected = "Rejected";
        public const string Processing = "Processing";
        public const string Shipped = "Shipped";
        public const string Delivered = "Delivered";
        public const string Completed = "Completed";
        public const string Cancelled = "Cancelled";

        public static readonly List<string> AllStatuses = new List<string>
        {
            Pending, Accepted, Rejected, Processing, Shipped, Delivered, Completed, Cancelled
        };
    }
}
