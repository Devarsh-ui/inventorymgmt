using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;

namespace InventoryManagementSystem.Models
{
    [BsonIgnoreExtraElements]
    public class ExchangeRecord : ITenantEntity
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("TenantId")]
        public string? TenantId { get; set; }

        [BsonElement("ExchangeNumber")]
        public string ExchangeNumber { get; set; } = string.Empty;

        // Old Exchanged Phone Info
        [BsonElement("OldBrand")]
        public string OldBrand { get; set; } = string.Empty;

        [BsonElement("OldModel")]
        public string OldModel { get; set; } = string.Empty;

        [BsonElement("OldColor")]
        public string OldColor { get; set; } = string.Empty;

        [BsonElement("OldStorage")]
        public string OldStorage { get; set; } = string.Empty;

        [BsonElement("OldRam")]
        public string OldRam { get; set; } = string.Empty;

        [BsonElement("OldImei1")]
        public string OldImei1 { get; set; } = string.Empty;

        [BsonElement("OldImei2")]
        [BsonIgnoreIfNull]
        public string? OldImei2 { get; set; }

        [BsonElement("SerialNumber")]
        [BsonIgnoreIfNull]
        public string? SerialNumber { get; set; }

        [BsonElement("DeviceType")]
        public string DeviceType { get; set; } = "Smartphone"; // Smartphone, Feature Phone, Tablet, Smartwatch

        [BsonElement("SimType")]
        public string SimType { get; set; } = "Dual SIM";

        // Condition & Component Inspection
        [BsonElement("Condition")]
        public string Condition { get; set; } = "Good"; // Flawless, Good, Fair, Damaged, Non-Working

        [BsonElement("ScreenCondition")]
        public string ScreenCondition { get; set; } = "Good"; // Excellent, Good, Scratched, Cracked, Display Issue

        [BsonElement("BodyCondition")]
        public string BodyCondition { get; set; } = "Good"; // Excellent, Good, Scratches, Dents, Heavy Damage

        [BsonElement("CameraCondition")]
        public string CameraCondition { get; set; } = "Working"; // Working, Minor Issue, Faulty

        [BsonElement("SpeakerCondition")]
        public string SpeakerCondition { get; set; } = "Working";

        [BsonElement("MicrophoneCondition")]
        public string MicrophoneCondition { get; set; } = "Working";

        [BsonElement("ChargingPortCondition")]
        public string ChargingPortCondition { get; set; } = "Working"; // Working, Loose, Damaged

        [BsonElement("ButtonsCondition")]
        public string ButtonsCondition { get; set; } = "Working";

        [BsonElement("FaceIdFingerprintCondition")]
        public string FaceIdFingerprintCondition { get; set; } = "Working"; // Working, Not Working, Not Available

        [BsonElement("NetworkSimCondition")]
        public string NetworkSimCondition { get; set; } = "Working";

        [BsonElement("WifiBluetoothCondition")]
        public string WifiBluetoothCondition { get; set; } = "Working";

        [BsonElement("BatteryHealthPercentage")]
        public int? BatteryHealthPercentage { get; set; }

        [BsonElement("BatteryCondition")]
        public string BatteryCondition { get; set; } = "Good"; // Excellent, Good, Weak, Needs Replacement

        // Security & Account Lock Status
        [BsonElement("IsDeviceUnlocked")]
        public bool IsDeviceUnlocked { get; set; } = true;

        [BsonElement("AppleActivationLock")]
        public string AppleActivationLock { get; set; } = "Not Checked"; // Clear, Enabled, Not Checked

        [BsonElement("GoogleFRPAccountLock")]
        public string GoogleFRPAccountLock { get; set; } = "Not Checked"; // Clear, Enabled, Not Checked

        [BsonElement("HasAccountLock")]
        public bool HasAccountLock { get; set; } = false;

        // Accessories Received
        [BsonElement("AccessoriesReceived")]
        public List<string> AccessoriesReceived { get; set; } = new List<string>();

        [BsonElement("OtherAccessoriesNotes")]
        public string OtherAccessoriesNotes { get; set; } = string.Empty;

        // Ownership & Purchase Proof
        [BsonElement("OriginalPurchaseDate")]
        public DateTime? OriginalPurchaseDate { get; set; }

        [BsonElement("OriginalPurchaseInvoice")]
        public string OriginalPurchaseInvoice { get; set; } = string.Empty;

        [BsonElement("OriginalSupplierStore")]
        public string OriginalSupplierStore { get; set; } = string.Empty;

        [BsonElement("WarrantyStatus")]
        public string WarrantyStatus { get; set; } = "Out of Warranty";

        [BsonElement("WarrantyExpiryDate")]
        public DateTime? WarrantyExpiryDate { get; set; }

        [BsonElement("HasPurchaseProof")]
        public bool HasPurchaseProof { get; set; } = false;

        // Valuation & Costing
        [BsonElement("EstimatedValue")]
        public decimal EstimatedValue { get; set; }

        [BsonElement("FinalExchangeValue")]
        public decimal FinalExchangeValue { get; set; }

        [BsonElement("Remarks")]
        public string Remarks { get; set; } = string.Empty;

        // Inventory Routing & Link
        [BsonElement("InventoryDestinationStatus")]
        public string InventoryDestinationStatus { get; set; } = "InStock"; // InStock, UnderRepair, Damaged, Pending

        [BsonElement("DeviceId")]
        [BsonIgnoreIfNull]
        public string? DeviceId { get; set; }

        [BsonElement("ProductId")]
        [BsonIgnoreIfNull]
        public string? ProductId { get; set; }

        // Purchased New Device Info (if part of POS invoice checkout)
        [BsonElement("NewDeviceId")]
        public string NewDeviceId { get; set; } = string.Empty;

        [BsonElement("NewImei")]
        public string NewImei { get; set; } = string.Empty;

        [BsonElement("NewProductId")]
        public string NewProductId { get; set; } = string.Empty;

        [BsonElement("NewProductName")]
        public string NewProductName { get; set; } = string.Empty;

        [BsonElement("InvoiceNumber")]
        public string InvoiceNumber { get; set; } = string.Empty;

        // Customer & Customer ID
        [BsonElement("CustomerId")]
        public string CustomerId { get; set; } = string.Empty;

        [BsonElement("CustomerName")]
        public string CustomerName { get; set; } = string.Empty;

        [BsonElement("CustomerPhone")]
        public string CustomerPhone { get; set; } = string.Empty;

        [BsonElement("CustomerEmail")]
        public string CustomerEmail { get; set; } = string.Empty;

        [BsonElement("CustomerAddress")]
        public string CustomerAddress { get; set; } = string.Empty;

        // Inspection Audit
        [BsonElement("InspectionStatus")]
        public string InspectionStatus { get; set; } = "Passed"; // Pending, Passed, Failed

        [BsonElement("InspectorName")]
        public string InspectorName { get; set; } = string.Empty;

        [BsonElement("ExecutedBy")]
        public string ExecutedBy { get; set; } = string.Empty;

        // Device Photos (Multiple attachments / live camera captures)
        [BsonElement("DevicePhotos")]
        [BsonIgnoreIfNull]
        public List<string> DevicePhotos { get; set; } = new List<string>();

        [BsonElement("Date")]
        public DateTime Date { get; set; } = DateTime.UtcNow;
    }
}
