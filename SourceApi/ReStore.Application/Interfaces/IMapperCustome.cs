using System.Collections.Generic;

namespace ReStore.Application.Interfaces
{
    /// <summary>
    /// The mapping seam the base handlers talk to. It exists so Application does not
    /// reference Mapster: Infrastructure supplies the implementation (MapsterCustome).
    /// </summary>
    public interface IMapperCustome
    {
        TDestination Map<TSource, TDestination>(TSource source);

        /// <summary>
        /// Maps onto an existing instance, keeping properties the source does not
        /// provide. This is what makes a partial update command work against a
        /// partially loaded entity.
        /// </summary>
        TDestination Map<TSource, TDestination>(TSource source, TDestination destination);

        List<TDestination> MapList<TSource, TDestination>(IEnumerable<TSource> source);

        /// <summary>
        /// Maps an anonymous projection (the result of a Select expression) onto a
        /// type by property name. This is the trick that lets a query select only the
        /// columns it needs and still hand a typed entity to the DTO mapper.
        /// </summary>
        TDestination MapAnonymous<TDestination>(object? source);
    }
}
