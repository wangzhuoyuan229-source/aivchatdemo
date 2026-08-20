namespace ChatApp.AI.Caching;

/// <summary>
/// Small thread-safe, TTL + capacity-limited per-scope query cache used to dedupe
/// repeated recall calls (memory / knowledge retrieval) within a session so the
/// same question does not re-run embedding or vector search. Scopes isolate data
/// by role/conversation; write operations call <see cref="InvalidateScope"/> to
/// keep results fresh.
/// </summary>
public sealed class ScopedQueryCache<T>
{
    private readonly TimeSpan _ttl;
    private readonly int _maxPerScope;
    private readonly int _maxScopes;
    private readonly Dictionary<string, Dictionary<string, Entry>> _scopes = new();
    private readonly object _lock = new();

    public ScopedQueryCache(TimeSpan? ttl = null, int maxPerScope = 8, int maxScopes = 32)
    {
        _ttl = ttl ?? TimeSpan.FromSeconds(60);
        _maxPerScope = maxPerScope;
        _maxScopes = maxScopes;
    }

    public bool TryGet(string scope, string key, out T value)
    {
        lock (_lock)
        {
            if (_scopes.TryGetValue(scope, out var queries) &&
                queries.TryGetValue(key, out var entry) &&
                DateTime.UtcNow - entry.At < _ttl)
            {
                entry.At = DateTime.UtcNow;
                value = entry.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    public void Set(string scope, string key, T value)
    {
        lock (_lock)
        {
            if (!_scopes.TryGetValue(scope, out var queries))
            {
                if (_scopes.Count >= _maxScopes)
                    _scopes.Remove(_scopes.MinBy(p => p.Value.Values.Select(e => e.At).DefaultIfEmpty(DateTime.MinValue).Max()).Key);
                queries = new Dictionary<string, Entry>();
                _scopes[scope] = queries;
            }
            if (queries.Count >= _maxPerScope)
                queries.Clear();
            queries[key] = new Entry(DateTime.UtcNow, value);
        }
    }

    public void InvalidateScope(string scope)
    {
        lock (_lock) _scopes.Remove(scope);
    }

    public void Clear()
    {
        lock (_lock) _scopes.Clear();
    }

    public int EntryCount
    {
        get
        {
            lock (_lock) return _scopes.Values.Sum(q => q.Count);
        }
    }

    private sealed class Entry(DateTime at, T value)
    {
        public DateTime At { get; set; } = at;
        public T Value { get; } = value;
    }
}