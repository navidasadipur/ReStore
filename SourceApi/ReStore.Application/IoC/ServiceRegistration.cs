using Microsoft.Extensions.DependencyInjection;

namespace ReStore.Application.IoC;

public static class ServiceRegistration
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssembly(typeof(ServiceRegistration).Assembly));
        return services;
    }
}
