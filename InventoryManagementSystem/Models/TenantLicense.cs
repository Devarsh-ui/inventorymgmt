using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace InventoryManagementSystem.Models
{
    [BsonIgnoreExtraElements]
    public class TenantLicense
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = string.Empty;

        [BsonElement("TenantId")]
        public string TenantId { get; set; } = string.Empty;

        [BsonElement("PackageId")]
        public string PackageId { get; set; } = string.Empty;

        [BsonElement("StartDate")]
        public DateTime StartDate { get; set; } = DateTime.UtcNow;

        [BsonElement("ExpiryDate")]
        public DateTime ExpiryDate { get; set; } = DateTime.UtcNow.AddDays(30);

        [BsonElement("Status")]
        public string Status { get; set; } = "Active"; // Trial, Active, ExpiringSoon, Expired, Suspended

        [BsonElement("AutoRenew")]
        public bool AutoRenew { get; set; } = true;

        [BsonElement("LastPaymentId")]
        public string LastPaymentId { get; set; } = string.Empty;

        [BsonElement("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("UpdatedAt")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [BsonElement("ExpirationEmailSent")]
        public bool ExpirationEmailSent { get; set; } = false;

        [BsonElement("SuspensionEmailSent")]
        public bool SuspensionEmailSent { get; set; } = false;
    }
}
