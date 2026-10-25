using System;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ReStore.Application.Common;
using ReStore.Application.Controls;
using ReStore.Application.Interfaces;
using ReStore.Domain.Entities;
using ReStore.Domain.Interfaces.Repository;
using ReStore.Infrastructure.Caching;
using ReStore.Infrastructure.Mapping;
using ReStore.Infrastructure.Messaging;
using ReStore.Infrastructure.Persistence;
using ReStore.Infrastructure.Repositories;
using ReStore.Infrastructure.Services;

namespace ReStore.Infrastructure.IoC
{
    /// <summary>
    /// The one place the kernel is wired up. Program.cs calls
    /// <see cref="AddInfrastructure"/> and nothing else from this project.
    ///
    /// Lifetimes matter here (decision D6 removed the service locator, so nothing can
    /// be resolved lazily by name): the DbContext, repositories, controls and mappers
    /// are Scoped; settings, logging and the two integration seams are Singleton.
    /// </summary>
    public static class ServiceRegistration
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddDbContext<StoreContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

            AddMapping(services);
            AddKernel(services);

            AddIntegrationSeams(services, configuration);

            return services;
        }

        private static void AddMapping(IServiceCollection services)
        {
            var mapsterConfig = MapsterConfig.Create();

            services.AddSingleton(mapsterConfig);

            // Mapster's own IMapper, for code that wants the standard interface.
            services.AddScoped<IMapper, ServiceMapper>();

            // What the base handlers actually use, so Application stays Mapster-free.
            services.AddScoped<IMapperCustome, MapsterCustome>();
        }

        private static void AddKernel(IServiceCollection services)
        {
            services.AddSingleton<IAppLogger, AppLogger>();

            services.AddScoped(typeof(IGenericQueryRepositoryBase<>), typeof(GenericQueryRepositoryBase<>));
            services.AddScoped(typeof(IGenericCommandRepositoryBase<>), typeof(GenericCommandRepositoryBase<>));

            // Lets a command property's [CheckKeyExists(typeof(ISomeRepository))] be honoured
            // without the handler doing a by-name lookup itself.
            services.AddScoped<IKeyExistenceChecker, KeyExistenceChecker>();

            // The default control for any entity; a feature registers its own
            // IControls<T> when it needs business rules. The closed registrations below
            // win over the open one, because DI resolves the last match.
            services.AddScoped(typeof(IControls<>), typeof(BaseControls<>));

            AddProductSlice(services);
        }

        /// <summary>Products: its own control, its own read repository, and image storage.</summary>
        private static void AddProductSlice(IServiceCollection services)
        {
            services.AddScoped<IControls<ProductEntity>, ProductControls>();
            services.AddScoped<IProductQueryRepository, ProductQueryRepository>();
            services.AddScoped<IImageStorage, CloudinaryImageStorage>();
        }

        /// <summary>
        /// Redis and RabbitMQ exist in code but are **not** connected (decision D9).
        /// The no-op implementations are registered, so a handler may already depend on
        /// ICache/IMessageBus and nothing reaches the network.
        ///
        /// To switch on, once the server has the two services:
        ///   1. set Redis:Enabled / RabbitMq:Enabled to true,
        ///   2. replace the two registrations below with RedisCacheService and
        ///      RabbitMqMessageBus,
        ///   3. redeploy. No handler changes.
        /// </summary>
        private static void AddIntegrationSeams(IServiceCollection services, IConfiguration configuration)
        {
            var redis = ReadRedisSettings(configuration);
            var rabbitMq = ReadRabbitSettings(configuration);

            services.AddSingleton(redis);
            services.AddSingleton(rabbitMq);

            services.AddSingleton<ICache, NullCache>();
            services.AddSingleton<IMessageBus, NullMessageBus>();

            // TODO(server): install Redis + RabbitMQ, then swap the two lines above for:
            // services.AddSingleton<ICache, RedisCacheService>();
            // services.AddSingleton<IMessageBus, RabbitMqMessageBus>();
        }

        /// <summary>
        /// Bound by hand from IConfiguration: Microsoft.Extensions.Configuration.Binder
        /// is not referenced, so GetSection().Get&lt;T&gt;() is not available and the
        /// indexer keeps the dependency surface at Configuration.Abstractions only.
        /// </summary>
        private static RedisSettings ReadRedisSettings(IConfiguration configuration)
        {
            var section = configuration.GetSection("Redis");

            return new RedisSettings
            {
                Enabled = TryReadBool(section, nameof(RedisSettings.Enabled)),
                Configuration = section[nameof(RedisSettings.Configuration)] ?? "localhost:6379",
                InstanceName = section[nameof(RedisSettings.InstanceName)] ?? "restore:",
                ConnectTimeoutMilliseconds = TryReadInt(section, nameof(RedisSettings.ConnectTimeoutMilliseconds), 1000),
                SyncTimeoutMilliseconds = TryReadInt(section, nameof(RedisSettings.SyncTimeoutMilliseconds), 1000),
                ConnectRetry = TryReadInt(section, nameof(RedisSettings.ConnectRetry), 3)
            };
        }

        private static RabbitMqSettings ReadRabbitSettings(IConfiguration configuration)
        {
            var section = configuration.GetSection("RabbitMq");

            return new RabbitMqSettings
            {
                Enabled = TryReadBool(section, nameof(RabbitMqSettings.Enabled)),
                Host = section[nameof(RabbitMqSettings.Host)] ?? "localhost",
                Port = (ushort)TryReadInt(section, nameof(RabbitMqSettings.Port), 5672),
                VirtualHost = section[nameof(RabbitMqSettings.VirtualHost)] ?? "/",
                Username = section[nameof(RabbitMqSettings.Username)] ?? "guest",
                Password = section[nameof(RabbitMqSettings.Password)] ?? "guest",
                QueueName = section[nameof(RabbitMqSettings.QueueName)] ?? "restore.messages",
                StartTimeoutMilliseconds = TryReadInt(section, nameof(RabbitMqSettings.StartTimeoutMilliseconds), 3000)
            };
        }

        private static bool TryReadBool(IConfigurationSection section, string key)
            => bool.TryParse(section[key], out var value) && value;

        private static int TryReadInt(IConfigurationSection section, string key, int fallback)
            => int.TryParse(section[key], out var value) && value > 0 ? value : fallback;
    }
}
