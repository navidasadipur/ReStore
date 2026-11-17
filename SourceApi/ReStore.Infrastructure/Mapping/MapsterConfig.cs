using System;
using Mapster;
using ReStore.Application.DTOs;

namespace ReStore.Infrastructure.Mapping
{
    /// <summary>
    /// Builds the one TypeAdapterConfig the whole process uses.
    ///
    /// <c>IgnoreNullValues</c> is not cosmetic: it is what makes a partial update
    /// command map onto a loaded entity without blanking columns the command did not
    /// mention. Removing it would silently turn every partial update into a full replace.
    /// </summary>
    public static class MapsterConfig
    {
        public static TypeAdapterConfig Create()
        {
            var config = TypeAdapterConfig.GlobalSettings;

            config.Default.IgnoreNullValues(true);

            // Picks up every IRegister in these assemblies, so feature mapping configs
            // added in later phases need no change here.
            config.Scan(typeof(MapsterConfig).Assembly, typeof(BaseDto).Assembly);

            return config;
        }
    }
}
