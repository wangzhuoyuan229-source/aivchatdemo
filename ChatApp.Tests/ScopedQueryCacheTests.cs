using ChatApp.AI.Caching;

namespace ChatApp.Tests;

/// <summary>Coverage for the plan-3.3 scoped recall cache (TTL, capacity, isolation).</summary>
public class ScopedQueryCacheTests
{
    [Fact]
    public void EntriesExpireAfterTtlAndAreNeverReturnedAgain()
    {
        var cache = new ScopedQueryCache<string>(TimeSpan.FromMilliseconds(60));

        cache.Set("a", "q", "v1");
        Assert.True(cache.TryGet("a", "q", out var value));
        Assert.Equal("v1", value);

        Thread.Sleep(120);
        Assert.False(cache.TryGet("a", "q", out _));
    }

    [Fact]
    public void ScopesAreIsolatedAndInvalidateIndependently()
    {
        var cache = new ScopedQueryCache<string>();

        cache.Set("mem:1", "q1", "r1");
        cache.Set("mem:2", "q1", "r2");

        cache.InvalidateScope("mem:1");

        Assert.False(cache.TryGet("mem:1", "q1", out _));
        Assert.True(cache.TryGet("mem:2", "q1", out var value));
        Assert.Equal("r2", value);
    }

    [Fact]
    public void FreshReadsRefreshTheTtl()
    {
        var cache = new ScopedQueryCache<string>(TimeSpan.FromMilliseconds(120));

        cache.Set("a", "q", "v");
        Thread.Sleep(70);
        Assert.True(cache.TryGet("a", "q", out _));
        Thread.Sleep(70);
        Assert.True(cache.TryGet("a", "q", out _));
    }

    [Fact]
    public void ScopeCapacityEvictsOldestScope()
    {
        var cache = new ScopedQueryCache<string>(maxPerScope: 8, maxScopes: 2);

        cache.Set("s1", "q", "1");
        cache.Set("s2", "q", "2");
        cache.Set("s3", "q", "3");

        Assert.Equal(2, cache.EntryCount);
        Assert.False(cache.TryGet("s1", "q", out _));
        Assert.True(cache.TryGet("s2", "q", out _));
        Assert.True(cache.TryGet("s3", "q", out _));
    }

    [Fact]
    public void PerScopeOverflowKeepsOnlyTheNewestQuery()
    {
        var cache = new ScopedQueryCache<string>(maxPerScope: 2);

        cache.Set("a", "q1", "1");
        cache.Set("a", "q2", "2");
        cache.Set("a", "q3", "3");

        Assert.Equal(1, cache.EntryCount);
        Assert.False(cache.TryGet("a", "q1", out _));
        Assert.False(cache.TryGet("a", "q2", out _));
        Assert.True(cache.TryGet("a", "q3", out _));
    }

    [Fact]
    public void ClearDropsEveryScope()
    {
        var cache = new ScopedQueryCache<string>();
        cache.Set("a", "q", "v");
        cache.Set("b", "q", "v");

        cache.Clear();

        Assert.Equal(0, cache.EntryCount);
        Assert.False(cache.TryGet("a", "q", out _));
    }
}