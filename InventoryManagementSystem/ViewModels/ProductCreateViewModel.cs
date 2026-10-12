using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using InventoryManagementSystem.Models;

namespace InventoryManagementSystem.ViewModels
{
    public class ProductCreateViewModel
    {
        [Required(ErrorMessage = "Product Name is required.")]
        [Display(Name = "Product Name")]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "Product Code (SKU)")]
        public string? Code { get; set; } = string.Empty;

        public string? Barcode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Category is required.")]
        [Display(Name = "Category")]
        public string CategoryId { get; set; } = string.Empty;

        public string? ProductType { get; set; } = "Smartphone";
        public string? Brand { get; set; } = string.Empty;
        public string? ModelName { get; set; } = string.Empty;
        public string? Variant { get; set; } = string.Empty;
        public string? Color { get; set; } = string.Empty;
        public string? Ram { get; set; } = string.Empty;
        public string? Storage { get; set; } = string.Empty;

        [Display(Name = "Purchase Price (₹)")]
        public decimal PurchasePrice { get; set; }

        [Display(Name = "Selling Price (₹)")]
        public decimal SellingPrice { get; set; }

        [Required(ErrorMessage = "Minimum Stock level is required.")]
        [Range(0, 1000000, ErrorMessage = "Minimum Stock must be non-negative.")]
        [Display(Name = "Minimum Stock Level")]
        public int MinimumStock { get; set; }

        [Display(Name = "Initial Stock Level")]
        public int InitialStock { get; set; }

        public string? Description { get; set; } = string.Empty;

        [Display(Name = "Product Image (Primary)")]
        public IFormFile? ProductImage { get; set; }

        [Display(Name = "Product Gallery Images (Up to 50)")]
        public List<IFormFile>? ProductImages { get; set; }

        public List<ProductVariantInputModel> Variants { get; set; } = new List<ProductVariantInputModel>();

        public MobileSpecifications Specs { get; set; } = new MobileSpecifications();

        public string Status { get; set; } = "Active";

        public IEnumerable<SelectListItem> Categories { get; set; } = new List<SelectListItem>();
    }

    public class ProductColorInputModel
    {
        public string? ColorId { get; set; }
        public string? Name { get; set; } = string.Empty;
        public string? Code { get; set; }
        public string? ImageUrl { get; set; }
        public List<string> ExistingImageUrls { get; set; } = new List<string>();
        public List<IFormFile>? ImageFiles { get; set; }
        public int InitialStock { get; set; }
        public int CurrentStock { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal SellingPrice { get; set; }
    }

    public class ProductVariantInputModel
    {
        public string? VariantId { get; set; }
        public string? Ram { get; set; } = string.Empty;
        public string? Storage { get; set; } = string.Empty;
        public string? Sku { get; set; } = string.Empty;
        public decimal SupplierPrice { get; set; }
        public decimal PurchasePrice { get; set; }
        public decimal SellingPrice { get; set; }
        public decimal Mrp { get; set; }
        public int InitialStock { get; set; }
        public int CurrentStock { get; set; }
        public List<ProductColorInputModel> Colors { get; set; } = new List<ProductColorInputModel>();
    }
}
