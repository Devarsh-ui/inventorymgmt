using System;

namespace InventoryManagementSystem.Models
{
    public class PublicCtaViewModel
    {
        public string? Eyebrow { get; set; } = "JOIN 1,200+ MOBILE MERCHANTS";
        public string Title { get; set; } = "Everything you need";
        public string? TitleHighlight { get; set; } = "in one place";
        public string Description { get; set; } = "Join over 1,200 smartphone retailers, repair workshops, and mobile device distributors managing IMEI stock and GST billing on SIMS.";
        public string PrimaryButtonText { get; set; } = "Start Free Trial";
        public string PrimaryButtonUrl { get; set; } = "/Landing/Register";
        public string? SecondaryButtonText { get; set; } = "Explore Platform";
        public string? SecondaryButtonUrl { get; set; } = "/Landing/Features";
        public string? GuaranteeText { get; set; } = "14-Day Full Feature Trial • No Credit Card Required • Instant Setup";
        public bool IsMinimalStrip { get; set; } = false;
        public bool IncludeFaq { get; set; } = false;
        public string? FaqHeading { get; set; } = "Everything you need to know about SIMS";
        public string? SupportPhone { get; set; }
        public string? SupportEmail { get; set; }
    }
}
