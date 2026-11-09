using DisputePortal.Application.Abstractions;
using DisputePortal.Application.Common;
using DisputePortal.Domain.Common;
using DisputePortal.Domain.Disputes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace DisputePortal.Application;

public static class DependencyInjection
{
    private static readonly Type[] HandlerInterfaces =
        [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>), typeof(IDomainEventHandler<>)];

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddOptions<DisputeOptions>().BindConfiguration(DisputeOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<PortalOptions>().BindConfiguration(PortalOptions.SectionName).ValidateDataAnnotations().ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPortalClock, PortalClock>();
        services.AddSingleton(sp => new DisputeEligibilityPolicy(sp.GetRequiredService<IOptions<DisputeOptions>>().Value.WindowDays));
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();

        RegisterHandlers(services);
        return services;
    }

    /// <summary>
    /// Registers every handler in this assembly against the interfaces it implements, so adding a use case
    /// is just adding a class, with no list here to keep in sync.
    /// </summary>
    private static void RegisterHandlers(IServiceCollection services)
    {
        var registrations =
            from type in typeof(DependencyInjection).Assembly.GetTypes()
            where type is { IsClass: true, IsAbstract: false, IsGenericTypeDefinition: false }
            from contract in type.GetInterfaces()
            where contract.IsGenericType && HandlerInterfaces.Contains(contract.GetGenericTypeDefinition())
            select (contract, type);

        foreach (var (contract, implementation) in registrations)
        {
            services.AddScoped(contract, implementation);
        }
    }
}

/// <summary>Publishes domain events to every registered <see cref="IDomainEventHandler{TEvent}"/>.</summary>
internal sealed class DomainEventDispatcher(IServiceProvider services) : IDomainEventDispatcher
{
    public async Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            var handlerType = typeof(IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
            foreach (var handler in services.GetServices(handlerType))
            {
                await ((Task)handlerType.GetMethod(nameof(IDomainEventHandler<IDomainEvent>.HandleAsync))!
                    .Invoke(handler, [domainEvent, cancellationToken])!).ConfigureAwait(false);
            }
        }
    }
}
