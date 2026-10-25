using System.Collections.Generic;
using Mapster;
using ReStore.Application.Interfaces;

namespace ReStore.Infrastructure.Mapping
{
    /// <summary>
    /// Mapster behind <see cref="IMapperCustome"/>.
    ///
    /// The config sets <c>IgnoreNullValues</c>, which is what makes a partial update
    /// work: a command that leaves a property null maps onto the loaded entity without
    /// wiping the column.
    /// </summary>
    public sealed class MapsterCustome : IMapperCustome
    {
        private readonly TypeAdapterConfig _config;

        public MapsterCustome(TypeAdapterConfig config) => _config = config;

        public TDestination Map<TSource, TDestination>(TSource source)
            => source is null ? default! : TypeAdapter.Adapt<TSource, TDestination>(source, _config);

        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
            => source is null ? destination : TypeAdapter.Adapt(source, destination, _config);

        public List<TDestination> MapList<TSource, TDestination>(IEnumerable<TSource> source)
            => source is null
                ? new List<TDestination>()
                : TypeAdapter.Adapt<IEnumerable<TSource>, List<TDestination>>(source, _config);

        public TDestination MapAnonymous<TDestination>(object? source)
            => source is null ? default! : TypeAdapter.Adapt<object, TDestination>(source, _config);
    }
}
