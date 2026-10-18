using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using InventoryManagementSystem.Configuration;
using InventoryManagementSystem.Data;
using InventoryManagementSystem.Models;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== DIAGNOSTIC TEST: SUBSCRIPTION PACKAGES ===");
        
        var envPath = @"D:\Nightcode\inventorymgmt\.env";
        if (File.Exists(envPath))
        {
            foreach (var line in File.ReadAllLines(envPath))
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                    continue;

                var parts = trimmedLine.Split('=', 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    if (value.StartsWith("\"") && value.EndsWith("\""))
                    {
                        value = value.Substring(1, value.Length - 2);
                    }
                    Environment.SetEnvironmentVariable(key, value);
                }
            }
        }

        var connString = Environment.GetEnvironmentVariable("MongoDbSettings__ConnectionString");
        if (string.IsNullOrEmpty(connString))
            connString = Environment.GetEnvironmentVariable("MONGODB_CONNECTION_STRING");
        Console.WriteLine($"ConnectionString loaded: {(!string.IsNullOrEmpty(connString))}");

        var mongoSettings = Options.Create(new MongoDbSettings
        {
            ConnectionString = connString,
            DatabaseName = "SIMS_Db"
        });
        var context = new MongoDbContext(mongoSettings);

        var allFeaturesInDb = (await context.FeatureDefinitions.Find(_ => true).ToListAsync()).Select(f => f.FeatureKey).Distinct().ToList();
        
        var freeFeatures = new List<string>
        {
            "DASHBOARD", "POS_BILLING", "CUSTOMERS", "PRODUCTS_SPECS", "CATEGORIES",
            "STOCK_IN", "STOCK_OUT", "INVENTORY_SUMMARY", "MY_PROFILE", "SETTINGS"
        };

        var starterFeatures = new List<string>
        {
            "DASHBOARD", "POS_BILLING", "CUSTOMERS", "SUPPLIERS", "PRODUCTS_SPECS", "CATEGORIES",
            "STOCK_IN", "STOCK_OUT", "STOCK_HISTORY", "INVENTORY_SUMMARY", "INVOICE_RECORDS",
            "REPORTS", "MY_PROFILE", "SETTINGS", "EMAIL_ALERTS", "EMAIL_ALERTS_INVOICE"
        };

        var standardFeatures = new List<string>
        {
            "DASHBOARD", "IMEI_DEVICES", "POS_BILLING", "RETURNS_REFUNDS", "CUSTOMERS", "SUPPLIERS",
            "PRODUCTS_SPECS", "CATEGORIES", "STOCK_IN", "STOCK_OUT", "STOCK_HISTORY", "PURCHASE_RETURNS",
            "INVENTORY_SUMMARY", "INVOICE_RECORDS", "REPORTS", "ORDER_MANAGEMENT", "EMPLOYEES",
            "MY_PROFILE", "SETTINGS", "EMAIL_ALERTS", "EMAIL_ALERTS_INVOICE", "EMAIL_ALERTS_LOW_STOCK"
        };

        var proFeatures = new List<string>
        {
            "DASHBOARD", "IMEI_DEVICES", "POS_BILLING", "RETURNS_REFUNDS", "SERVICE_REPAIRS",
            "CUSTOMERS", "SUPPLIERS", "PRODUCTS_SPECS", "CATEGORIES", "STOCK_IN", "STOCK_OUT",
            "STOCK_HISTORY", "PURCHASE_RETURNS", "INVENTORY_SUMMARY", "INVOICE_RECORDS",
            "REPORTS", "ORDER_MANAGEMENT", "ADMINISTRATORS", "EMPLOYEES", "MY_PROFILE",
            "SETTINGS", "SYSTEM_LOGS", "EMAIL_ALERTS", "EMAIL_ALERTS_INVOICE",
            "EMAIL_ALERTS_LOW_STOCK", "EMAIL_ALERTS_PURCHASE_ORDER", "EMAIL_ALERTS_REPAIR"
        };

        var enterpriseFeatures = allFeaturesInDb.Any() ? allFeaturesInDb : new List<string>
        {
            "DASHBOARD", "IMEI_DEVICES", "POS_BILLING", "TRADE_IN_EXCHANGE", "RETURNS_REFUNDS",
            "SERVICE_REPAIRS", "CUSTOMERS", "SUPPLIERS", "PRODUCTS_SPECS", "CATEGORIES",
            "STOCK_IN", "STOCK_OUT", "STOCK_HISTORY", "PURCHASE_RETURNS", "INVENTORY_SUMMARY",
            "INVOICE_RECORDS", "REPORTS", "ORDER_MANAGEMENT", "ADMINISTRATORS", "EMPLOYEES",
            "MY_PROFILE", "SETTINGS", "SYSTEM_LOGS", "EMAIL_ALERTS", "EMAIL_ALERTS_INVOICE",
            "EMAIL_ALERTS_LOW_STOCK", "EMAIL_ALERTS_PURCHASE_ORDER", "EMAIL_ALERTS_REPAIR", "EMAIL_ALERTS_RETURN"
        };

        var standardPlanDefinitions = new List<SubscriptionPackage>
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
                EnabledModules = new List<string> { "Products", "POS", "Customers" },
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
                EnabledModules = new List<string> { "Products", "Inventory", "POS", "Customers", "Suppliers", "Reports" },
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
                EnabledModules = new List<string> { "Products", "Inventory", "POS", "Customers", "Suppliers", "Returns", "Reports" },
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
                EnabledModules = new List<string> { "Products", "Inventory", "POS", "Customers", "Suppliers", "Returns", "Repairs", "Reports", "Employees" },
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
                EnabledModules = new List<string> { "Products", "Inventory", "POS", "Customers", "Suppliers", "Returns", "Repairs", "TradeIn", "Reports", "AdvancedAnalytics" },
                EnabledFeatures = enterpriseFeatures,
                IsActive = true,
                IsFeatured = false,
                DisplayOrder = 5
            }
        };

        // If old "Basic Plan" exists, update it to "Standard"
        var existingBasic = await context.SubscriptionPackages.Find(p => p.Name == "Basic Plan" || p.Name == "Basic").FirstOrDefaultAsync();
        if (existingBasic != null)
        {
            var std = standardPlanDefinitions.First(p => p.Name == "Standard");
            existingBasic.Name = "Standard";
            existingBasic.Description = std.Description;
            existingBasic.MonthlyPrice = std.MonthlyPrice;
            existingBasic.YearlyPrice = std.YearlyPrice;
            existingBasic.TrialDays = std.TrialDays;
            existingBasic.BillingCycle = std.BillingCycle;
            existingBasic.MaxEmployees = std.MaxEmployees;
            existingBasic.MaxProducts = std.MaxProducts;
            existingBasic.MaxSuppliers = std.MaxSuppliers;
            existingBasic.MaxAdmins = std.MaxAdmins;
            existingBasic.EnabledModules = std.EnabledModules;
            existingBasic.EnabledFeatures = std.EnabledFeatures;
            existingBasic.IsActive = true;
            existingBasic.IsFeatured = false;
            existingBasic.DisplayOrder = 3;
            await context.SubscriptionPackages.ReplaceOneAsync(p => p.Id == existingBasic.Id, existingBasic);
            Console.WriteLine($"Updated existing Basic Plan (ID: {existingBasic.Id}) to Standard Plan.");
        }

        // Upsert the rest of the standard packages
        foreach (var def in standardPlanDefinitions)
        {
            var match = await context.SubscriptionPackages.Find(p => p.Name == def.Name).FirstOrDefaultAsync();
            if (match == null)
            {
                def.CreatedAt = DateTime.UtcNow;
                await context.SubscriptionPackages.InsertOneAsync(def);
                Console.WriteLine($"Inserted new package: '{def.Name}'");
            }
            else
            {
                match.Description = def.Description;
                match.MonthlyPrice = def.MonthlyPrice;
                match.YearlyPrice = def.YearlyPrice;
                match.TrialDays = def.TrialDays;
                match.BillingCycle = def.BillingCycle;
                match.MaxEmployees = def.MaxEmployees;
                match.MaxProducts = def.MaxProducts;
                match.MaxSuppliers = def.MaxSuppliers;
                match.MaxAdmins = def.MaxAdmins;
                match.EnabledModules = def.EnabledModules;
                match.EnabledFeatures = def.EnabledFeatures;
                match.IsActive = true;
                match.IsFeatured = def.IsFeatured;
                match.DisplayOrder = def.DisplayOrder;
                await context.SubscriptionPackages.ReplaceOneAsync(p => p.Id == match.Id, match);
                Console.WriteLine($"Synchronized existing package: '{match.Name}' (Order: {match.DisplayOrder})");
            }
        }

        var packages = await context.SubscriptionPackages.Find(_ => true).SortBy(p => p.DisplayOrder).ToListAsync();
        Console.WriteLine($"\n--- FINAL PACKAGES IN DB ({packages.Count}) ---");
        foreach (var p in packages)
        {
            Console.WriteLine($"Package: '{p.Name}' | Monthly: ₹{p.MonthlyPrice} | Active: {p.IsActive} | Custom: {p.IsCustom} | Order: {p.DisplayOrder}");
        }
    }
}
