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

        private bool IsRedisAvailable => _redis != null && _redis.IsConnected;

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Value, DateTime Expiry)> _fallbackCache 
            = new System.Collections.Concurrent.ConcurrentDictionary<string, (string Value, DateTime Expiry)>();

        public async Task<bool> AcquireLockAsync(string key, TimeSpan expiry)
        {
            if (IsRedisAvailable)
            {
                try { 
                    return await _db.StringSetAsync(key, "LOCKED", expiry, When.NotExists); 
                } catch { }
            }
            
            var now = DateTime.UtcNow;
            if (_fallbackCache.TryGetValue(key, out var existing) && existing.Expiry > now) return false;
            _fallbackCache[key] = ("LOCKED", now.Add(expiry));
            return true;
        }

        public async Task SetValueAsync(string key, string value, TimeSpan expiry)
        {
            if (IsRedisAvailable)
            {
                try { 
                    await _db.StringSetAsync(key, value, expiry);
                    return;
                } catch { }
            }
            _fallbackCache[key] = (value, DateTime.UtcNow.Add(expiry));
        }

        public async Task<string?> GetValueAsync(string key)
        {
            if (IsRedisAvailable)
            {
                try { 
                    return await _db.StringGetAsync(key); 
                } catch { }
            }
            
            if (_fallbackCache.TryGetValue(key, out var item) && item.Expiry > DateTime.UtcNow) return item.Value;
            return null;
        }

        public async Task DeleteKeyAsync(string key)
        {
            if (IsRedisAvailable)
            {
                try { 
                    await _db.KeyDeleteAsync(key);
                    return;
                } catch { }
            }
            _fallbackCache.TryRemove(key, out _);
        }

        public async Task<bool> IsLockedAsync(string key)
        {
            if (IsRedisAvailable)
            {
                try { 
                    var val = await _db.StringGetAsync(key);
                    return val == "LOCKED";
                } catch { }
            }
            
            if (_fallbackCache.TryGetValue(key, out var item) && item.Expiry > DateTime.UtcNow) return item.Value == "LOCKED";
            return false; 
        }

        public async Task<IEnumerable<string>> GetKeysByPrefixAsync(string prefix)
        {
            await Task.CompletedTask;
            var keys = new List<string>();
            if (IsRedisAvailable)
            {
                try
                {
                    // For StackExchange.Redis we need IServer to get keys
                    var endpoints = _redis.GetEndPoints();
                    var server = _redis.GetServer(endpoints[0]);
                    foreach (var key in server.Keys(pattern: prefix + "*"))
                    {
                        var keyStr = key.ToString();
                        if (keyStr != null) keys.Add(keyStr);
                    }
                    return keys;
                }
                catch { }
            }

            // Fallback
            var now = DateTime.UtcNow;
            foreach (var kvp in _fallbackCache)
            {
                if (kvp.Key.StartsWith(prefix) && kvp.Value.Expiry > now)
                {
                    keys.Add(kvp.Key);
                }
            }
            return keys;
        }
    }
}
