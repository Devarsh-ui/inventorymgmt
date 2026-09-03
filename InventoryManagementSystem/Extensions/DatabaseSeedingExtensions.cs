using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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

            // Seed Roles
            var rolesCount = await context.Roles.CountDocumentsAsync(_ => true);
            if (rolesCount == 0)
            {
                await context.Roles.InsertManyAsync(new[]
                {
                    new Role { Name = Role.Admin, Description = "Administrator with full system privileges" },
                    new Role { Name = Role.Staff, Description = "Staff member with inventory operation privileges" }
                });
            }

            // Seed Admin User
            var adminCount = await context.Users.CountDocumentsAsync(u => u.Role == Role.Admin);
            if (adminCount == 0)
            {
                var adminUser = new User
                {
                    EmployeeId = "EMP-1000",
                    Username = "admin",
                    Email = "admin@sims.com",
                    FullName = "System Administrator",
                    PhoneNumber = "1234567890",
                    Role = Role.Admin,
                    IsLocked = false,
                    ProfilePictureUrl = "https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?auto=format&fit=crop&q=80&w=200", // Standard premium mockup avatar
                    CreatedDate = DateTime.UtcNow,
                    UpdatedDate = DateTime.UtcNow,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("AdminPassword123")
                };

                await context.Users.InsertOneAsync(adminUser);
            }

            // Seed Initial Settings
            var settingsCount = await context.Settings.CountDocumentsAsync(_ => true);
            if (settingsCount == 0)
            {
                var settings = new Settings
                {
                    CompanyName = "Smart Inventory Management System (SIMS)",
                    Currency = "INR",
                    GstPercentage = 18.0,
                    Theme = "dark",
                    UpdatedBy = "System",
                    LastUpdated = DateTime.UtcNow
                };
                await context.Settings.InsertOneAsync(settings);
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
        }
    }
}
