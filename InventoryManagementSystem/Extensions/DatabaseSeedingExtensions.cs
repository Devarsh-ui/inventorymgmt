using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using InventoryManagementSystem.Data;
using InventoryManagementSystem.Models;
using System;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Extensions
{
    public static class DatabaseSeedingExtensions
    {
        public static async Task SeedDatabaseAsync(this IApplicationBuilder app)
        {
            using var scope = app.ApplicationServices.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<MongoDbContext>();

            // 1. Seed or Resolve Primary Tenant
            var primaryTenant = await context.Tenants.Find(t => t.TenantCode == "SHOP-PRIMARY").FirstOrDefaultAsync();
            if (primaryTenant == null)
            {
                primaryTenant = new Tenant
                {
                    TenantCode = "SHOP-PRIMARY",
                    ShopName = "Primary Mobile Shop",
                    OwnerName = "Primary Admin",
                    ContactEmail = "admin@sims.com",
                    Phone = "1234567890",
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await context.Tenants.InsertOneAsync(primaryTenant);
                Console.WriteLine($"[TENANT MIGRATION] Created primary tenant with generated Id: {primaryTenant.Id}");
            }

            var primaryTenantId = primaryTenant.Id;

            // Seed Roles
            var superAdminRole = await context.Roles.Find(r => r.Name == Role.SuperAdmin).FirstOrDefaultAsync();
            if (superAdminRole == null)
            {
                await context.Roles.InsertOneAsync(new Role { Name = Role.SuperAdmin, Description = "SaaS Platform Super Administrator" });
            }

            var rolesCount = await context.Roles.CountDocumentsAsync(_ => true);
            if (rolesCount == 0)
            {
                await context.Roles.InsertManyAsync(new[]
                {
                    new Role { Name = Role.Admin, Description = "Administrator with full system privileges" },
                    new Role { Name = Role.Staff, Description = "Staff member with inventory operation privileges" },
                    new Role { Name = Role.SuperAdmin, Description = "SaaS Platform Super Administrator" }
                });
            }

            // Seed SuperAdmin Platform User
            var superAdminUser = await context.Users.Find(u => u.Username == "superadmin" || u.Role == Role.SuperAdmin).FirstOrDefaultAsync();
            if (superAdminUser == null)
            {
                superAdminUser = new User
                {
                    TenantId = null,
                    EmployeeId = null,
                    Username = "superadmin",
                    Email = "superadmin@sims.com",
                    FullName = "SaaS Platform SuperAdmin",
                    PhoneNumber = "0000000000",
                    Role = Role.SuperAdmin,
                    IsLocked = false,
                    ProfilePictureUrl = "https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?auto=format&fit=crop&q=80&w=200",
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("SuperAdmin123!")
                };
                await context.Users.InsertOneAsync(superAdminUser);
                Console.WriteLine("[SEEDING] Created Platform SuperAdmin user: superadmin / SuperAdmin123!");
            }
            else
            {
                superAdminUser.TenantId = null;
                superAdminUser.EmployeeId = null;
                superAdminUser.Role = Role.SuperAdmin;
                await context.Users.ReplaceOneAsync(u => u.Id == superAdminUser.Id, superAdminUser);
            }

            // Seed Primary Shop Admin User
            var adminUser = await context.Users.Find(u => u.Username == "admin").FirstOrDefaultAsync();
            if (adminUser == null)
            {
                adminUser = new User
                {
                    TenantId = primaryTenantId,
                    EmployeeId = "EMP-1000",
                    Username = "admin",
                    Email = "admin@sims.com",
                    FullName = "System Administrator",
                    PhoneNumber = "1234567890",
                    Role = Role.Admin,
                    IsLocked = false,
                    ProfilePictureUrl = "https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?auto=format&fit=crop&q=80&w=200",
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("AdminPassword123")
                };
                await context.Users.InsertOneAsync(adminUser);
                Console.WriteLine("[SEEDING] Created Primary Shop Admin user: admin / AdminPassword123");
            }

            // Seed Initial Settings
            var settingsCount = await context.Settings.CountDocumentsAsync(_ => true);
            if (settingsCount == 0)
            {
                var settings = new Settings
                {
                    TenantId = primaryTenantId,
                    CompanyName = "Smart Inventory Management System (SIMS)",
                    Currency = "INR",
                    GstPercentage = 18.0,
                    Theme = "dark",
                    UpdatedBy = "System",
                    LastUpdated = DateTime.UtcNow
                };
                await context.Settings.InsertOneAsync(settings);
            }

            // Seed SaaS Feature Definitions
            try
            {
                var featureCatalogService = scope.ServiceProvider.GetRequiredService<InventoryManagementSystem.Interfaces.IFeatureCatalogService>();
                await featureCatalogService.SeedDefaultFeaturesAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SEEDING NOTICE] Feature catalog seeding notice: {ex.Message}");
            }

            // Seed Default Subscription Packages
            var pkgCount = await context.SubscriptionPackages.CountDocumentsAsync(_ => true);
            if (pkgCount == 0)
            {
                var freeFeatures = new System.Collections.Generic.List<string>
                {
                    "DASHBOARD", "POS_BILLING", "CUSTOMERS", "PRODUCTS_SPECS", "CATEGORIES",
                    "STOCK_IN", "STOCK_OUT", "INVENTORY_SUMMARY", "MY_PROFILE", "SETTINGS"
                };

                var starterFeatures = new System.Collections.Generic.List<string>
                {
                    "DASHBOARD", "POS_BILLING", "CUSTOMERS", "SUPPLIERS", "PRODUCTS_SPECS", "CATEGORIES",
                    "STOCK_IN", "STOCK_OUT", "STOCK_HISTORY", "INVENTORY_SUMMARY", "INVOICE_RECORDS",
                    "REPORTS", "MY_PROFILE", "SETTINGS", "EMAIL_ALERTS", "EMAIL_ALERTS_INVOICE"
                };

                var standardFeatures = new System.Collections.Generic.List<string>
                {
                    "DASHBOARD", "IMEI_DEVICES", "POS_BILLING", "RETURNS_REFUNDS", "CUSTOMERS", "SUPPLIERS",
                    "PRODUCTS_SPECS", "CATEGORIES", "STOCK_IN", "STOCK_OUT", "STOCK_HISTORY", "PURCHASE_RETURNS",
                    "INVENTORY_SUMMARY", "INVOICE_RECORDS", "REPORTS", "ORDER_MANAGEMENT", "EMPLOYEES",
                    "MY_PROFILE", "SETTINGS", "EMAIL_ALERTS", "EMAIL_ALERTS_INVOICE", "EMAIL_ALERTS_LOW_STOCK"
                };

                var proFeatures = new System.Collections.Generic.List<string>
                {
                    "DASHBOARD", "IMEI_DEVICES", "POS_BILLING", "RETURNS_REFUNDS", "SERVICE_REPAIRS",
                    "CUSTOMERS", "SUPPLIERS", "PRODUCTS_SPECS", "CATEGORIES", "STOCK_IN", "STOCK_OUT",
                    "STOCK_HISTORY", "PURCHASE_RETURNS", "INVENTORY_SUMMARY", "INVOICE_RECORDS",
                    "REPORTS", "ORDER_MANAGEMENT", "ADMINISTRATORS", "EMPLOYEES", "MY_PROFILE",
                    "SETTINGS", "SYSTEM_LOGS", "EMAIL_ALERTS", "EMAIL_ALERTS_INVOICE",
                    "EMAIL_ALERTS_LOW_STOCK", "EMAIL_ALERTS_PURCHASE_ORDER", "EMAIL_ALERTS_REPAIR"
                };

                var entFeatures = new System.Collections.Generic.List<string>
                {
                    "DASHBOARD", "IMEI_DEVICES", "POS_BILLING", "TRADE_IN_EXCHANGE", "RETURNS_REFUNDS",
                    "SERVICE_REPAIRS", "CUSTOMERS", "SUPPLIERS", "PRODUCTS_SPECS", "CATEGORIES",
                    "STOCK_IN", "STOCK_OUT", "STOCK_HISTORY", "PURCHASE_RETURNS", "INVENTORY_SUMMARY",
                    "INVOICE_RECORDS", "REPORTS", "ORDER_MANAGEMENT", "ADMINISTRATORS", "EMPLOYEES",
                    "MY_PROFILE", "SETTINGS", "SYSTEM_LOGS", "EMAIL_ALERTS", "EMAIL_ALERTS_INVOICE",
                    "EMAIL_ALERTS_LOW_STOCK", "EMAIL_ALERTS_PURCHASE_ORDER", "EMAIL_ALERTS_REPAIR", "EMAIL_ALERTS_RETURN"
                };

                await context.SubscriptionPackages.InsertManyAsync(new[]
                {
                    new SubscriptionPackage
                    {
                        Name = "Free",
                        Description = "Zero-cost tier for independent mobile repairers & hobbyists.",
                        MonthlyPrice = 0,
                        YearlyPrice = 0,
                        TrialDays = 14,
                        BillingCycle = "Monthly",
                        MaxEmployees = 1,
                        MaxProducts = 50,
                        MaxSuppliers = 5,
                        MaxAdmins = 1,
                        EnabledModules = new System.Collections.Generic.List<string> { "Products", "POS", "Customers" },
                        EnabledFeatures = freeFeatures,
                        IsActive = true,
                        IsFeatured = false,
                        DisplayOrder = 1
                    },
                    new SubscriptionPackage
                    {
                        Name = "Starter",
                        Description = "Essential billing and inventory tools for small mobile shops.",
                        MonthlyPrice = 499,
                        YearlyPrice = 4990,
                        TrialDays = 14,
                        BillingCycle = "Monthly",
                        MaxEmployees = 3,
                        MaxProducts = 500,
                        MaxSuppliers = 20,
                        MaxAdmins = 1,
                        EnabledModules = new System.Collections.Generic.List<string> { "Products", "Inventory", "POS", "Customers", "Suppliers", "Reports" },
                        EnabledFeatures = starterFeatures,
                        IsActive = true,
                        IsFeatured = false,
                        DisplayOrder = 2
                    },
                    new SubscriptionPackage
                    {
                        Name = "Standard",
                        Description = "Advanced serialized tracking and POS for growing phone counters.",
                        MonthlyPrice = 999,
                        YearlyPrice = 9990,
                        TrialDays = 14,
                        BillingCycle = "Monthly",
                        MaxEmployees = 6,
                        MaxProducts = 2000,
                        MaxSuppliers = 50,
                        MaxAdmins = 2,
                        EnabledModules = new System.Collections.Generic.List<string> { "Products", "Inventory", "POS", "Customers", "Suppliers", "Returns", "Reports" },
                        EnabledFeatures = standardFeatures,
                        IsActive = true,
                        IsFeatured = false,
                        DisplayOrder = 3
                    },
                    new SubscriptionPackage
                    {
                        Name = "Professional",
                        Description = "Complete toolkit for high-volume stores with repair labs.",
                        MonthlyPrice = 1999,
                        YearlyPrice = 19990,
                        TrialDays = 14,
                        BillingCycle = "Monthly",
                        MaxEmployees = 15,
                        MaxProducts = 10000,
                        MaxSuppliers = 200,
                        MaxAdmins = 5,
                        EnabledModules = new System.Collections.Generic.List<string> { "Products", "Inventory", "POS", "Customers", "Suppliers", "Returns", "Repairs", "Reports", "Employees" },
                        EnabledFeatures = proFeatures,
                        IsActive = true,
                        IsFeatured = true,
                        DisplayOrder = 4
                    },
                    new SubscriptionPackage
                    {
                        Name = "Enterprise",
                        Description = "Full multi-branch scale, trade-in buyback, and priority support.",
                        MonthlyPrice = 3999,
                        YearlyPrice = 39990,
                        TrialDays = 30,
                        BillingCycle = "Monthly",
                        MaxEmployees = int.MaxValue,
                        MaxProducts = int.MaxValue,
                        MaxSuppliers = int.MaxValue,
                        MaxAdmins = int.MaxValue,
                        EnabledModules = new System.Collections.Generic.List<string> { "Products", "Inventory", "POS", "Customers", "Suppliers", "Returns", "Repairs", "TradeIn", "Reports", "AdvancedAnalytics" },
                        EnabledFeatures = entFeatures,
                        IsActive = true,
                        IsFeatured = false,
                        DisplayOrder = 5
                    }
                });
            }

            // Cleanup any orphaned supplier products/categories from previously deleted suppliers
            try
            {
                var supplierService = scope.ServiceProvider.GetRequiredService<InventoryManagementSystem.Interfaces.ISupplierService>();
                await supplierService.CleanupOrphanedSupplierDataAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SEEDING NOTICE] Orphaned supplier cleanup notice: {ex.Message}");
            }

            // Ensure Primary Tenant has Enterprise package and active license
            try
            {
                var pt = await context.Tenants.Find(t => t.TenantCode == "SHOP-PRIMARY").FirstOrDefaultAsync();
                if (pt != null)
                {
                    var entPkg = await context.SubscriptionPackages.Find(p => p.Name == "Enterprise").FirstOrDefaultAsync();
                    if (entPkg != null)
                    {
                        if (string.IsNullOrEmpty(pt.PackageId))
                        {
                            pt.PackageId = entPkg.Id;
                            await context.Tenants.ReplaceOneAsync(t => t.Id == pt.Id, pt);
                        }

                        var existingLicense = await context.TenantLicenses.Find(l => l.TenantId == pt.Id).FirstOrDefaultAsync();
                        if (existingLicense == null)
                        {
                            await context.TenantLicenses.InsertOneAsync(new TenantLicense
                            {
                                TenantId = pt.Id,
                                PackageId = entPkg.Id,
                                StartDate = DateTime.UtcNow.AddMonths(-1),
                                ExpiryDate = DateTime.UtcNow.AddYears(1),
                                Status = "Active",
                                AutoRenew = true,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SEEDING NOTICE] Primary tenant license assignment notice: {ex.Message}");
            }
        }
    }
}
