using System;
using System.Threading.Tasks;
using FluentAssertions;
using ReStore.Application.Common;
using ReStore.Application.Interfaces;
using ReStore.Infrastructure.Caching;
using ReStore.Infrastructure.Messaging;
using Xunit;

namespace ReStore.Test;

/// <summary>
/// The invariant from decision D9: Redis and RabbitMQ may be missing, misconfigured or
/// down, and no call may throw. These tests are the guarantee behind "adding them
/// cannot break the app".
/// </summary>
public class InfrastructureSeamTests
{
    private static readonly IAppLogger Logger = new NoopAppLogger();

    [Fact]
    public async Task NullCache_never_throws_and_always_misses()
    {
        ICache cache = new NullCache();

        (await cache.GetAsync<int>("anything")).Should().Be(0);
        (await cache.SetAsync("anything", 1)).Should().BeFalse();
        (await cache.RemoveAsync("anything")).Should().BeFalse();
        (await cache.IsAvailableAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task NullMessageBus_never_throws_and_never_claims_delivery()
    {
        IMessageBus bus = new NullMessageBus();

        (await bus.PublishAsync(new { Id = 1 })).Should().BeFalse();
        (await bus.IsAvailableAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task RedisCache_is_inert_when_disabled()
    {
        ICache cache = new RedisCacheService(new RedisSettings { Enabled = false }, Logger);

        (await cache.GetAsync<int>("key")).Should().Be(0);
        (await cache.SetAsync("key", 1)).Should().BeFalse();
        (await cache.IsAvailableAsync()).Should().BeFalse();
    }

    /// <summary>
    /// Enabled and pointed at a closed port: the adapter must report a miss quickly and
    /// must not surface the connection failure. This is the failure mode a real outage
    /// produces.
    /// </summary>
    [Fact]
    public async Task RedisCache_degrades_to_a_miss_when_the_server_is_unreachable()
    {
        var settings = new RedisSettings
        {
            Enabled = true,
            Configuration = "127.0.0.1:6399",
            ConnectTimeoutMilliseconds = 200,
            SyncTimeoutMilliseconds = 200,
            ConnectRetry = 1
        };

        var cache = new RedisCacheService(settings, Logger);

        var read = async () => await cache.GetAsync<int>("key");
        var write = async () => await cache.SetAsync("key", 1);

        await read.Should().NotThrowAsync();
        await write.Should().NotThrowAsync();
        (await cache.GetAsync<int>("key")).Should().Be(0);
        (await cache.IsAvailableAsync()).Should().BeFalse();

        cache.Dispose();
    }

    [Fact]
    public async Task RabbitMqBus_never_throws_when_disabled()
    {
        IMessageBus bus = new RabbitMqMessageBus(
            new RabbitMqSettings { Enabled = false, Host = "127.0.0.1", Port = 5699 },
            Logger);

        var publish = async () => await bus.PublishAsync(new { Id = 1 });

        await publish.Should().NotThrowAsync();
        (await bus.PublishAsync(new { Id = 1 })).Should().BeFalse();
    }
}
