using MongoDB.Driver;
using InventoryManagementSystem.Data;
using InventoryManagementSystem.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Repositories
{
    public class BaseRepository<T> : IBaseRepository<T> where T : class
    {
        protected readonly MongoDbContext _context;
        protected readonly IMongoCollection<T> _collection;

        public BaseRepository(MongoDbContext context, string collectionName)
        {
            _context = context;
            _collection = _context.GetCollection<T>(collectionName);
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync()
        {
            return await _collection.Find(_ => true).ToListAsync();
        }

        public virtual async Task<T?> GetByIdAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            FilterDefinition<T> filter;
            if (MongoDB.Bson.ObjectId.TryParse(id, out var objectId))
            {
                filter = Builders<T>.Filter.Eq("_id", objectId);
            }
            else
            {
                filter = Builders<T>.Filter.Eq("_id", id);
            }
            return await _collection.Find(filter).FirstOrDefaultAsync();
        }

        public virtual async Task CreateAsync(T entity)
        {
            await _collection.InsertOneAsync(entity);
        }

        public virtual async Task UpdateAsync(string id, T entity)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            FilterDefinition<T> filter;
            if (MongoDB.Bson.ObjectId.TryParse(id, out var objectId))
            {
                filter = Builders<T>.Filter.Eq("_id", objectId);
            }
            else
            {
                filter = Builders<T>.Filter.Eq("_id", id);
            }
            var result = await _collection.ReplaceOneAsync(filter, entity);
            Console.WriteLine($"[REPOSITORY DIAGNOSTICS] UpdateAsync matched: {result.MatchedCount}, modified: {result.ModifiedCount}");
        }

        public virtual async Task DeleteAsync(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return;
            FilterDefinition<T> filter;
            if (MongoDB.Bson.ObjectId.TryParse(id, out var objectId))
            {
                filter = Builders<T>.Filter.Eq("_id", objectId);
            }
            else
            {
                filter = Builders<T>.Filter.Eq("_id", id);
            }
            await _collection.DeleteOneAsync(filter);
        }

        public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
        {
            return await _collection.Find(predicate).ToListAsync();
        }

        public virtual async Task<long> CountAsync(Expression<Func<T, bool>>? predicate = null)
        {
            if (predicate == null)
            {
                return await _collection.CountDocumentsAsync(_ => true);
            }
            return await _collection.CountDocumentsAsync(predicate);
        }
    }
}
