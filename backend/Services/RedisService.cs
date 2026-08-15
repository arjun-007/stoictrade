using StackExchange.Redis;
using System.Text.Json;

namespace StoicTrade.Api.Services
{
    public class RedisService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly IDatabase _db;

        public RedisService(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
            if (!connectionString.Contains("abortConnect"))
            {
                connectionString += ",abortConnect=false";
            }
            _redis = ConnectionMultiplexer.Connect(connectionString);
            _db = _redis.GetDatabase();
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Value, DateTime Expiry)> _fallbackCache 
            = new System.Collections.Concurrent.ConcurrentDictionary<string, (string Value, DateTime Expiry)>();

        public async Task<bool> AcquireLockAsync(string key, TimeSpan expiry)
        {
            try { 
                return await _db.StringSetAsync(key, "LOCKED", expiry, When.NotExists); 
            } catch { 
                var now = DateTime.UtcNow;
                if (_fallbackCache.TryGetValue(key, out var existing) && existing.Expiry > now) return false;
                _fallbackCache[key] = ("LOCKED", now.Add(expiry));
                return true;
            }
        }

        public async Task SetValueAsync(string key, string value, TimeSpan expiry)
        {
            try { 
                await _db.StringSetAsync(key, value, expiry); 
            } catch { 
                _fallbackCache[key] = (value, DateTime.UtcNow.Add(expiry));
            }
        }

        public async Task<string?> GetValueAsync(string key)
        {
            try { 
                return await _db.StringGetAsync(key); 
            } catch { 
                if (_fallbackCache.TryGetValue(key, out var item) && item.Expiry > DateTime.UtcNow) return item.Value;
                return null;
            }
        }

        public async Task DeleteKeyAsync(string key)
        {
            try { 
                await _db.KeyDeleteAsync(key); 
            } catch { 
                _fallbackCache.TryRemove(key, out _);
            }
        }

        public async Task<bool> IsLockedAsync(string key)
        {
            try { 
                var val = await _db.StringGetAsync(key);
                return val == "LOCKED";
            } catch { 
                if (_fallbackCache.TryGetValue(key, out var item) && item.Expiry > DateTime.UtcNow) return item.Value == "LOCKED";
                return false; 
            }
        }
    }
}
