using InventoryManagementSystem.Data;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Services
{
    public class FeatureCatalogService : IFeatureCatalogService
    {
        private readonly MongoDbContext _context;
        private readonly ILicenseService _licenseService;

        public FeatureCatalogService(MongoDbContext context, ILicenseService licenseService)
        {
            _context = context;
            _licenseService = licenseService;
        }

        public async Task<List<FeatureDefinition>> GetAllFeaturesAsync()
        {
            var features = await _context.FeatureDefinitions.Find(_ => true).SortBy(f => f.DisplayOrder).ToListAsync();
            if (!features.Any())
            {
                await SeedDefaultFeaturesAsync();
                features = await _context.FeatureDefinitions.Find(_ => true).SortBy(f => f.DisplayOrder).ToListAsync();
            }
            return features;
        }

        public async Task<List<FeatureDefinition>> GetActiveFeaturesAsync()
        {
            var all = await GetAllFeaturesAsync();
            return all.Where(f => f.IsActive).OrderBy(f => f.DisplayOrder).ToList();
        }

        public async Task<Dictionary<string, List<FeatureDefinition>>> GetGroupedFeaturesAsync()
        {
            var active = await GetActiveFeaturesAsync();
            var grouped = new Dictionary<string, List<FeatureDefinition>>();

            var categories = new[]
            {
                "MAIN / DASHBOARD",
                "MOBILE SHOP",
                "DIRECTORY",
                "INVENTORY & STOCK",
                "RECORDS & REPORTS",
                "SYSTEM / MANAGEMENT"
            };

            foreach (var cat in categories)
            {
                grouped[cat] = active.Where(f => f.Category == cat).OrderBy(f => f.DisplayOrder).ToList();
            }

            // Catch any custom or newly added categories
            var remaining = active.Where(f => !categories.Contains(f.Category)).GroupBy(f => f.Category);
            foreach (var group in remaining)
            {
                grouped[group.Key] = group.OrderBy(f => f.DisplayOrder).ToList();
            }

            return grouped;
        }

        public FeatureDefinition? GetFeatureByControllerAndAction(string controllerName, string actionName = "Index")
        {
            var active = GetActiveFeaturesAsync().GetAwaiter().GetResult();

            // Direct match by controller and action name
            var match = active.FirstOrDefault(f =>
                string.Equals(f.ControllerName, controllerName, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(f.ActionName, actionName, StringComparison.OrdinalIgnoreCase));

            if (match != null) return match;

            // Controller fallback match
            return active.FirstOrDefault(f => string.Equals(f.ControllerName, controllerName, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<List<string>> GetEnabledFeatureKeysForTenantAsync(string tenantId)
        {
            if (string.IsNullOrEmpty(tenantId))
            {
                // SuperAdmin has access to all features
                var allActive = await GetActiveFeaturesAsync();
                return allActive.Select(f => f.FeatureKey).ToList();
            }

            var package = await _licenseService.GetTenantPackageAsync(tenantId);
            if (package == null)
            {
                // Fallback to all features if no package assigned
                var allActive = await GetActiveFeaturesAsync();
                return allActive.Select(f => f.FeatureKey).ToList();
            }

            var enabled = new List<string>();
            if (package.EnabledFeatures != null && package.EnabledFeatures.Any())
            {
                enabled.AddRange(package.EnabledFeatures.Where(f => !string.IsNullOrEmpty(f)));
            }

            // Backward compatibility for legacy EnabledModules containing controller names (e.g., "Sales", "Supplier")
            if (package.EnabledModules != null && package.EnabledModules.Any())
            {
                var activeFeatures = await GetActiveFeaturesAsync();
                foreach (var mod in package.EnabledModules.Where(m => !string.IsNullOrEmpty(m)))
                {
                    var matches = activeFeatures.Where(f =>
                        string.Equals(f.ControllerName, mod, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.FeatureKey, mod, StringComparison.OrdinalIgnoreCase));
                    foreach (var m in matches)
                    {
                        if (!enabled.Contains(m.FeatureKey))
                        {
                            enabled.Add(m.FeatureKey);
                        }
                    }
                }
            }

            return enabled.Distinct().ToList();
        }

        public async Task<bool> IsFeatureEnabledForTenantAsync(string tenantId, string controllerName, string actionName = "Index")
        {
            if (string.IsNullOrEmpty(tenantId)) return true; // SuperAdmin bypass

            // Exempt common essential controllers (Home, Account, Notifications, SystemLog for own profile)
            if (string.Equals(controllerName, "Home", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(controllerName, "Account", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(controllerName, "Notifications", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var feature = GetFeatureByControllerAndAction(controllerName, actionName);
            if (feature == null) return true; // Unregistered features allowed by default

            var enabledKeys = await GetEnabledFeatureKeysForTenantAsync(tenantId);
            return enabledKeys.Contains(feature.FeatureKey, StringComparer.OrdinalIgnoreCase);
        }

        public async Task SeedDefaultFeaturesAsync()
        {
            var count = await _context.FeatureDefinitions.CountDocumentsAsync(_ => true);
            if (count > 0) return;

            var defaultFeatures = new List<FeatureDefinition>
            {
                // MAIN / DASHBOARD
                new FeatureDefinition { FeatureKey = "DASHBOARD", FeatureName = "Dashboard", Description = "Overview statistics, recent transactions, sales summaries, and key performance indicators.", Category = "MAIN / DASHBOARD", ControllerName = "Home", ActionName = "Index", Route = "/Home/Index", Icon = "bi-grid-1x2", DisplayOrder = 1 },

                // MOBILE SHOP
                new FeatureDefinition { FeatureKey = "IMEI_DEVICES", FeatureName = "IMEI & Devices", Description = "Smartphone IMEI barcode tracking, device serial numbers, condition status, and stock lifecycle.", Category = "MOBILE SHOP", ControllerName = "Imei", ActionName = "Index", Route = "/Imei/Index", Icon = "bi-qr-code-scan", DisplayOrder = 2 },
                new FeatureDefinition { FeatureKey = "POS_BILLING", FeatureName = "POS Billing", Description = "Point-of-Sale invoice checkout, customer billing, payment methods, GST calculations, and digital receipts.", Category = "MOBILE SHOP", ControllerName = "Sales", ActionName = "Create", Route = "/Sales/Create", Icon = "bi-receipt", DisplayOrder = 3 },
                new FeatureDefinition { FeatureKey = "TRADE_IN_EXCHANGE", FeatureName = "Trade-In Exchange", Description = "Used mobile device trade-in valuations, customer device exchange agreements, and secondary stock entry.", Category = "MOBILE SHOP", ControllerName = "Exchange", ActionName = "Index", Route = "/Exchange/Index", Icon = "bi-arrow-left-right", DisplayOrder = 4 },
                new FeatureDefinition { FeatureKey = "RETURNS_REFUNDS", FeatureName = "Returns & Refunds", Description = "Customer sales return management, defective unit claims, replacement issuing, and refund auditing.", Category = "MOBILE SHOP", ControllerName = "Return", ActionName = "Index", Route = "/Return/Index", Icon = "bi-arrow-counterclockwise", DisplayOrder = 5 },
                new FeatureDefinition { FeatureKey = "SERVICE_REPAIRS", FeatureName = "Service & Repairs", Description = "Device repair ticketing, technician job sheets, repair status tracking, spare parts billing, and customer updates.", Category = "MOBILE SHOP", ControllerName = "Repair", ActionName = "Index", Route = "/Repair/Index", Icon = "bi-tools", DisplayOrder = 6 },

                // DIRECTORY
                new FeatureDefinition { FeatureKey = "CUSTOMERS", FeatureName = "Customers", Description = "Customer profiles, contact directory, order history, credit balance tracking, and loyalty records.", Category = "DIRECTORY", ControllerName = "Customer", ActionName = "Index", Route = "/Customer/Index", Icon = "bi-person-heart", DisplayOrder = 7 },
                new FeatureDefinition { FeatureKey = "SUPPLIERS", FeatureName = "Suppliers", Description = "Vendor directory, procurement stock-in sources, payment terms, and outstanding payables tracking.", Category = "DIRECTORY", ControllerName = "Supplier", ActionName = "Index", Route = "/Supplier/Index", Icon = "bi-truck", DisplayOrder = 8 },

                // INVENTORY & STOCK
                new FeatureDefinition { FeatureKey = "PRODUCTS_SPECS", FeatureName = "Products & Specs", Description = "Mobile phone and accessory catalog, hardware technical specifications, selling prices, and min stock thresholds.", Category = "INVENTORY & STOCK", ControllerName = "Product", ActionName = "Index", Route = "/Product/Index", Icon = "bi-box-seam", DisplayOrder = 9 },
                new FeatureDefinition { FeatureKey = "CATEGORIES", FeatureName = "Categories", Description = "Product categorizations, brands, model groupings, and inventory classification tags.", Category = "INVENTORY & STOCK", ControllerName = "Category", ActionName = "Index", Route = "/Category/Index", Icon = "bi-tag", DisplayOrder = 10 },
                new FeatureDefinition { FeatureKey = "STOCK_IN", FeatureName = "Stock In", Description = "Purchased stock intake, purchase invoice uploading, supplier delivery logging, and stock count additions.", Category = "INVENTORY & STOCK", ControllerName = "Stock", ActionName = "StockIn", Route = "/Stock/StockIn", Icon = "bi-arrow-down-circle", DisplayOrder = 11 },
                new FeatureDefinition { FeatureKey = "STOCK_OUT", FeatureName = "Stock Out", Description = "Manual inventory deductions, damaged stock write-offs, store transfers, and internal stock usage.", Category = "INVENTORY & STOCK", ControllerName = "Stock", ActionName = "StockOut", Route = "/Stock/StockOut", Icon = "bi-arrow-up-circle", DisplayOrder = 12 },
                new FeatureDefinition { FeatureKey = "STOCK_HISTORY", FeatureName = "Stock History", Description = "Audit trail of every stock movement, IMEI assignment, intake, deduction, and adjustment log.", Category = "INVENTORY & STOCK", ControllerName = "Stock", ActionName = "History", Route = "/Stock/History", Icon = "bi-clock-history", DisplayOrder = 13 },
                new FeatureDefinition { FeatureKey = "PURCHASE_RETURNS", FeatureName = "Purchase Returns", Description = "Returning defective or unsold inventory back to wholesale suppliers with credit note records.", Category = "INVENTORY & STOCK", ControllerName = "PurchaseReturn", ActionName = "Index", Route = "/PurchaseReturn/Index", Icon = "bi-box-arrow-left", DisplayOrder = 14 },
                new FeatureDefinition { FeatureKey = "INVENTORY_SUMMARY", FeatureName = "Inventory Summary", Description = "Total inventory valuation, stock level health breakdown, stock value by category, and turnover metrics.", Category = "INVENTORY & STOCK", ControllerName = "Stock", ActionName = "InventorySummary", Route = "/Stock/InventorySummary", Icon = "bi-file-earmark-text", DisplayOrder = 15 },

                // RECORDS & REPORTS
                new FeatureDefinition { FeatureKey = "INVOICE_RECORDS", FeatureName = "Invoice Records", Description = "Historical POS invoice lookup, PDF reprinting, email resending, and invoice cancellation history.", Category = "RECORDS & REPORTS", ControllerName = "Sales", ActionName = "Index", Route = "/Sales/Index", Icon = "bi-journal-text", DisplayOrder = 16 },
                new FeatureDefinition { FeatureKey = "REPORTS", FeatureName = "Reports", Description = "Comprehensive business analytics, daily revenue, profit margins, top-selling models, and GST tax reports.", Category = "RECORDS & REPORTS", ControllerName = "Report", ActionName = "Index", Route = "/Report/Index", Icon = "bi-bar-chart-line", DisplayOrder = 17 },
                new FeatureDefinition { FeatureKey = "ORDER_MANAGEMENT", FeatureName = "Order Management", Description = "Purchase order generation to suppliers, stock order status tracking, and arrival confirmations.", Category = "RECORDS & REPORTS", ControllerName = "SupplierOrder", ActionName = "Index", Route = "/SupplierOrder/Index", Icon = "bi-cart-check", DisplayOrder = 18 },

                // SYSTEM / MANAGEMENT
                new FeatureDefinition { FeatureKey = "ADMINISTRATORS", FeatureName = "Administrators", Description = "Shop administrator credential management, shop owner settings, and master access overrides.", Category = "SYSTEM / MANAGEMENT", ControllerName = "Admin", ActionName = "Index", Route = "/Admin/Index", Icon = "bi-shield-lock", DisplayOrder = 19 },
                new FeatureDefinition { FeatureKey = "EMPLOYEES", FeatureName = "Employees", Description = "Staff employee accounts, credential resets, activity tracking, and module permission assignments.", Category = "SYSTEM / MANAGEMENT", ControllerName = "User", ActionName = "Index", Route = "/User/Index", Icon = "bi-person-badge", DisplayOrder = 20 },
                new FeatureDefinition { FeatureKey = "MY_PROFILE", FeatureName = "My Profile", Description = "User personal profile details, account avatar, email preferences, and password change.", Category = "SYSTEM / MANAGEMENT", ControllerName = "Account", ActionName = "Profile", Route = "/Account/Profile", Icon = "bi-person-circle", DisplayOrder = 21 },
                new FeatureDefinition { FeatureKey = "SETTINGS", FeatureName = "Settings", Description = "Shop business profile details, GSTIN number, receipt footer headers, and invoice template options.", Category = "SYSTEM / MANAGEMENT", ControllerName = "Settings", ActionName = "Index", Route = "/Settings/Index", Icon = "bi-sliders", DisplayOrder = 22 },
                new FeatureDefinition { FeatureKey = "SYSTEM_LOGS", FeatureName = "System Logs", Description = "Audit logs of all user actions, security login attempts, credential changes, and system errors.", Category = "SYSTEM / MANAGEMENT", ControllerName = "SystemLog", ActionName = "Index", Route = "/SystemLog/Index", Icon = "bi-terminal", DisplayOrder = 23 },
                new FeatureDefinition { FeatureKey = "EMAIL_ALERTS", FeatureName = "Email Alerts", Description = "Automated email notification triggers, Brevo SMTP settings, low stock alerts, and receipt emails.", Category = "SYSTEM / MANAGEMENT", ControllerName = "InventoryAlert", ActionName = "Index", Route = "/InventoryAlert/Index", Icon = "bi-bell", DisplayOrder = 24 },

                // EMAIL ALERTS SUB-FEATURES
                new FeatureDefinition { FeatureKey = "EMAIL_ALERTS_INVOICE", FeatureName = "Invoice Emails", Description = "Automatically email digital PDF invoices to customers upon POS checkout completion.", Category = "SYSTEM / MANAGEMENT", ControllerName = "InventoryAlert", ActionName = "Index", Route = "/InventoryAlert/Index", Icon = "bi-envelope-check", DisplayOrder = 25, ParentKey = "EMAIL_ALERTS" },
                new FeatureDefinition { FeatureKey = "EMAIL_ALERTS_LOW_STOCK", FeatureName = "Low Stock Emails", Description = "Send automated daily low-stock warnings to shop managers when inventory drops below safety threshold.", Category = "SYSTEM / MANAGEMENT", ControllerName = "InventoryAlert", ActionName = "Index", Route = "/InventoryAlert/Index", Icon = "bi-envelope-exclamation", DisplayOrder = 26, ParentKey = "EMAIL_ALERTS" },
                new FeatureDefinition { FeatureKey = "EMAIL_ALERTS_PURCHASE_ORDER", FeatureName = "Purchase Order Emails", Description = "Automatically dispatch purchase orders and stock requests directly to suppliers via email.", Category = "SYSTEM / MANAGEMENT", ControllerName = "InventoryAlert", ActionName = "Index", Route = "/InventoryAlert/Index", Icon = "bi-envelope-paper", DisplayOrder = 27, ParentKey = "EMAIL_ALERTS" },
                new FeatureDefinition { FeatureKey = "EMAIL_ALERTS_REPAIR", FeatureName = "Repair Ticket Emails", Description = "Notify customers via email when repair ticket status changes to Completed or Ready for Collection.", Category = "SYSTEM / MANAGEMENT", ControllerName = "InventoryAlert", ActionName = "Index", Route = "/InventoryAlert/Index", Icon = "bi-envelope-heart", DisplayOrder = 28, ParentKey = "EMAIL_ALERTS" },
                new FeatureDefinition { FeatureKey = "EMAIL_ALERTS_RETURN", FeatureName = "Return Confirmation Emails", Description = "Send email receipts to customers when sales returns or device exchanges are processed.", Category = "SYSTEM / MANAGEMENT", ControllerName = "InventoryAlert", ActionName = "Index", Route = "/InventoryAlert/Index", Icon = "bi-envelope-dash", DisplayOrder = 29, ParentKey = "EMAIL_ALERTS" }
            };

            await _context.FeatureDefinitions.InsertManyAsync(defaultFeatures);
        }
    }
}
