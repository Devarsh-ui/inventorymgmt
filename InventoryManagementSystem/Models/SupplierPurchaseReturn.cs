using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace InventoryManagementSystem.Models
{
    public class SupplierPurchaseReturn
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("ReturnNumber")]
        public string ReturnNumber { get; set; } = string.Empty; // e.g. PR-20260820-0001

        [BsonElement("SupplierId")]
        [BsonRepresentation(BsonType.ObjectId)]
        public string SupplierId { get; set; } = string.Empty;

        [BsonElement("SupplierName")]
        public string SupplierName { get; set; } = string.Empty;

        [BsonElement("SupplierEmail")]
        public string SupplierEmail { get; set; } = string.Empty;

        [BsonElement("SupplierPhone")]
        public string SupplierPhone { get; set; } = string.Empty;

        [BsonElement("SupplierAddress")]
        public string SupplierAddress { get; set; } = string.Empty;

        [BsonElement("PurchaseOrderId")]
        [BsonRepresentation(BsonType.ObjectId)]
        [BsonIgnoreIfNull]
        public string? PurchaseOrderId { get; set; }

        [BsonElement("PurchaseOrderNumber")]
        public string PurchaseOrderNumber { get; set; } = string.Empty;

        [BsonElement("OrderDate")]
        public DateTime? OrderDate { get; set; }

        [BsonElement("ReceivedDate")]
        public DateTime? ReceivedDate { get; set; }

        [BsonElement("CreatedBy")]
        public string CreatedBy { get; set; } = string.Empty; // Username/Name of Admin or Employee

        [BsonElement("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("UpdatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("Status")]
        public string Status { get; set; } = PurchaseReturnStatus.Submitted;

        [BsonElement("Reason")]
        public string Reason { get; set; } = PurchaseReturnReason.Defective;

        [BsonElement("AdditionalRemarks")]
        public string AdditionalRemarks { get; set; } = string.Empty;

        [BsonElement("ResolutionType")]
        public string ResolutionType { get; set; } = PurchaseReturnResolution.Refund;

        [BsonElement("SettlementStatus")]
        public string SettlementStatus { get; set; } = PurchaseReturnSettlement.Pending;

        [BsonElement("RejectionReason")]
        public string RejectionReason { get; set; } = string.Empty;

        [BsonElement("SupplierNotes")]
        public string SupplierNotes { get; set; } = string.Empty;

        [BsonElement("TotalQuantity")]
        public int TotalQuantity { get; set; }

        [BsonElement("TotalDeviceCount")]
        public int TotalDeviceCount { get; set; }

        [BsonElement("TotalReturnValue")]
        public decimal TotalReturnValue { get; set; }

        [BsonElement("Items")]
        public List<SupplierPurchaseReturnItem> Items { get; set; } = new List<SupplierPurchaseReturnItem>();

        [BsonElement("DeviceDetails")]
        public List<SupplierReturnDeviceDetail> DeviceDetails { get; set; } = new List<SupplierReturnDeviceDetail>();

        [BsonElement("Timeline")]
        public List<PurchaseReturnTimelineEvent> Timeline { get; set; } = new List<PurchaseReturnTimelineEvent>();

        [BsonElement("EmailSent")]
        public bool EmailSent { get; set; } = false;

        [BsonElement("EmailSentAt")]
        public DateTime? EmailSentAt { get; set; }

        [BsonElement("EmailError")]
        public string EmailError { get; set; } = string.Empty;

        [BsonElement("StockDeducted")]
        public bool StockDeducted { get; set; } = false;
    }

    public class SupplierPurchaseReturnItem
    {
        [BsonElement("ProductId")]
        public string ProductId { get; set; } = string.Empty;

        [BsonElement("ProductName")]
        public string ProductName { get; set; } = string.Empty;

        [BsonElement("Brand")]
        public string Brand { get; set; } = string.Empty;

        [BsonElement("ModelName")]
        public string ModelName { get; set; } = string.Empty;

        [BsonElement("Variant")]
        public string Variant { get; set; } = string.Empty;

        [BsonElement("Color")]
        public string Color { get; set; } = string.Empty;

        [BsonElement("Ram")]
        public string Ram { get; set; } = string.Empty;

        [BsonElement("Storage")]
        public string Storage { get; set; } = string.Empty;

        [BsonElement("CategoryName")]
        public string CategoryName { get; set; } = string.Empty;

        [BsonElement("ImageUrl")]
        public string ImageUrl { get; set; } = string.Empty;

        [BsonElement("Quantity")]
        public int Quantity { get; set; }

        [BsonElement("UnitPurchasePrice")]
        public decimal UnitPurchasePrice { get; set; }

        [BsonElement("ReturnValue")]
        public decimal ReturnValue { get; set; }

        [BsonElement("DeviceIds")]
        public List<string> DeviceIds { get; set; } = new List<string>();

        [BsonElement("Imeis")]
        public List<string> Imeis { get; set; } = new List<string>();
    }

    public class SupplierReturnDeviceDetail
    {
        [BsonElement("DeviceId")]
        public string DeviceId { get; set; } = string.Empty;

        [BsonElement("IMEI1")]
        public string IMEI1 { get; set; } = string.Empty;

        [BsonElement("IMEI2")]
        public string IMEI2 { get; set; } = string.Empty;

        [BsonElement("SerialNumber")]
        public string SerialNumber { get; set; } = string.Empty;

        [BsonElement("Brand")]
        public string Brand { get; set; } = string.Empty;

        [BsonElement("ModelName")]
        public string ModelName { get; set; } = string.Empty;

        [BsonElement("Variant")]
        public string Variant { get; set; } = string.Empty;

        [BsonElement("Color")]
        public string Color { get; set; } = string.Empty;

        [BsonElement("Ram")]
        public string Ram { get; set; } = string.Empty;

        [BsonElement("Storage")]
        public string Storage { get; set; } = string.Empty;

        [BsonElement("PurchasePrice")]
        public decimal PurchasePrice { get; set; }

        [BsonElement("Status")]
        public string Status { get; set; } = "ReturnedToSupplier";
    }

    public class PurchaseReturnTimelineEvent
    {
        [BsonElement("Status")]
        public string Status { get; set; } = string.Empty;

        [BsonElement("UpdatedBy")]
        public string UpdatedBy { get; set; } = string.Empty;

        [BsonElement("Timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [BsonElement("Remarks")]
        public string Remarks { get; set; } = string.Empty;
    }

    public static class PurchaseReturnStatus
    {
        public const string Draft = "Draft";
        public const string Submitted = "Submitted";
        public const string SupplierNotified = "Supplier Notified";
        public const string SupplierAccepted = "Supplier Accepted";
        public const string SupplierRejected = "Supplier Rejected";
        public const string PreparingShipment = "Preparing Shipment";
        public const string Shipped = "Shipped";
        public const string ReceivedBySupplier = "Received by Supplier";
        public const string Completed = "Completed";
        public const string Cancelled = "Cancelled";

        public static readonly List<string> AllStatuses = new List<string>
        {
            Draft, Submitted, SupplierNotified, SupplierAccepted, SupplierRejected,
            PreparingShipment, Shipped, ReceivedBySupplier, Completed, Cancelled
        };
    }

    public static class PurchaseReturnReason
    {
        public const string Defective = "Defective";
        public const string DamagedOnArrival = "Damaged on Arrival";
        public const string WrongProduct = "Wrong Product";
        public const string WrongVariant = "Wrong Variant";
        public const string WrongColor = "Wrong Color";
        public const string WrongStorage = "Wrong Storage";
        public const string WrongRam = "Wrong RAM";
        public const string ImeiIssue = "IMEI Issue";
        public const string WarrantyIssue = "Warranty Issue";
        public const string QualityIssue = "Quality Issue";
        public const string SupplierError = "Supplier Error";
        public const string ExcessStock = "Excess Stock";
        public const string Other = "Other";

        public static readonly List<string> AllReasons = new List<string>
        {
            Defective, DamagedOnArrival, WrongProduct, WrongVariant, WrongColor,
            WrongStorage, WrongRam, ImeiIssue, WarrantyIssue, QualityIssue,
            SupplierError, ExcessStock, Other
        };
    }

    public static class PurchaseReturnResolution
    {
        public const string Refund = "Refund";
        public const string SupplierCredit = "Supplier Credit";
        public const string Replacement = "Replacement";
        public const string Pending = "Pending Settlement";
        public const string Other = "Other";

        public static readonly List<string> AllResolutions = new List<string>
        {
            Refund, SupplierCredit, Replacement, Pending, Other
        };
    }

    public static class PurchaseReturnSettlement
    {
        public const string Pending = "Pending";
        public const string Refunded = "Refunded";
        public const string CreditReceived = "Credit Received";
        public const string ReplacementReceived = "Replacement Received";
        public const string NoFinancialAdjustment = "No Financial Adjustment";
    }
}
