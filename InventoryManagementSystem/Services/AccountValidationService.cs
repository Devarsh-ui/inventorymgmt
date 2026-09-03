using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using InventoryManagementSystem.Data;
using InventoryManagementSystem.Interfaces;
using InventoryManagementSystem.Models;

namespace InventoryManagementSystem.Services
{
    public class AccountValidationService : IAccountValidationService
    {
        private readonly MongoDbContext _context;

        public AccountValidationService(MongoDbContext context)
        {
            _context = context;
        }

        public async Task<bool> IsEmailAlreadyRegisteredAsync(string email, string? excludeUserId = null, string? excludeSupplierId = null)
        {
            if (string.IsNullOrWhiteSpace(email)) return false;
            var normalizedEmail = email.Trim();

            // Case-insensitive regex filter for MongoDB
            var regex = new BsonRegularExpression($"^{Regex.Escape(normalizedEmail)}$", "i");

            // 1. Check Users collection (Admin / Staff)
            var userFilterBuilder = Builders<User>.Filter;
            var userFilter = userFilterBuilder.Regex(u => u.Email, regex);

            if (!string.IsNullOrWhiteSpace(excludeUserId))
            {
                userFilter = userFilterBuilder.And(userFilter, userFilterBuilder.Ne(u => u.Id, excludeUserId));
            }

            var existingUser = await _context.Users.Find(userFilter).FirstOrDefaultAsync();
            if (existingUser != null) return true;

            // 2. Check Suppliers collection
            var supplierFilterBuilder = Builders<Supplier>.Filter;
            var supplierFilter = supplierFilterBuilder.Regex(s => s.Email, regex);

            if (!string.IsNullOrWhiteSpace(excludeSupplierId))
            {
                supplierFilter = supplierFilterBuilder.And(supplierFilter, supplierFilterBuilder.Ne(s => s.Id, excludeSupplierId));
            }

            var existingSupplier = await _context.Suppliers.Find(supplierFilter).FirstOrDefaultAsync();
            return existingSupplier != null;
        }

        public async Task<bool> IsUsernameAlreadyRegisteredAsync(string username, string? excludeUserId = null, string? excludeSupplierId = null)
        {
            if (string.IsNullOrWhiteSpace(username)) return false;
            var normalized = username.Trim();

            var regex = new BsonRegularExpression($"^{Regex.Escape(normalized)}$", "i");

            // Check Users collection (Username or Email)
            var userFilterBuilder = Builders<User>.Filter;
            var userFilter = userFilterBuilder.Or(
                userFilterBuilder.Regex(u => u.Username, regex),
                userFilterBuilder.Regex(u => u.Email, regex)
            );

            if (!string.IsNullOrWhiteSpace(excludeUserId))
            {
                userFilter = userFilterBuilder.And(userFilter, userFilterBuilder.Ne(u => u.Id, excludeUserId));
            }

            var existingUser = await _context.Users.Find(userFilter).FirstOrDefaultAsync();
            return existingUser != null;
        }
    }
}
