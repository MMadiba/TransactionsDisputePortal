using System.Reflection;
using DisputePortal.Application.Abstractions;
using DisputePortal.Domain.Common;
using DisputePortal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace DisputePortal.UnitTests.Architecture;

/// <summary>
/// Guards the clean-architecture dependency rule: dependencies point inwards only
/// (Api → Infrastructure → Application → Domain). A violation fails the build, not a code review.
/// </summary>
public sealed class LayeringTests
{
    private static readonly Assembly Domain = typeof(AggregateRoot).Assembly;
    private static readonly Assembly Application = typeof(IUnitOfWork).Assembly;
    private static readonly Assembly Infrastructure = typeof(ApplicationDbContext).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    private static IEnumerable<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name!);

    [Fact]
    public void Domain_depends_on_nothing_but_the_base_library()
    {
        var offenders = References(Domain).Where(name => !name.StartsWith("System", StringComparison.Ordinal) && name != "netstandard");

        Assert.Empty(offenders);
    }

    [Theory]
    [InlineData("DisputePortal.Infrastructure")]
    [InlineData("DisputePortal.Api")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    [InlineData("Microsoft.AspNetCore")]
    public void Application_does_not_depend_on_outer_layers_or_frameworks(string forbiddenPrefix)
    {
        Assert.DoesNotContain(References(Application), name => name.StartsWith(forbiddenPrefix, StringComparison.Ordinal));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_web_host()
    {
        Assert.DoesNotContain(References(Infrastructure), name => name == Api.GetName().Name);
    }

    [Fact]
    public void Controllers_never_touch_the_database_directly()
    {
        var controllers = Api.GetTypes().Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract);

        var offenders =
            from controller in controllers
            from parameter in controller.GetConstructors().SelectMany(c => c.GetParameters())
                .Concat(controller.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).SelectMany(m => m.GetParameters()))
            where parameter.ParameterType == typeof(ApplicationDbContext)
            select $"{controller.Name}({parameter.Name})";

        Assert.Empty(offenders);
    }

    [Fact]
    public void Use_case_handlers_are_sealed_and_internal()
    {
        var handlerInterfaces = new[] { typeof(ICommandHandler<,>), typeof(IQueryHandler<,>) };
        var handlers = Application.GetTypes().Where(t => t.GetInterfaces()
            .Any(i => i.IsGenericType && handlerInterfaces.Contains(i.GetGenericTypeDefinition())));

        Assert.NotEmpty(handlers);
        Assert.All(handlers, h => Assert.True(h.IsSealed && !h.IsPublic, $"{h.Name} should be internal sealed"));
    }

    [Fact]
    public void Domain_entities_do_not_expose_public_setters()
    {
        var offenders =
            from type in Domain.GetTypes()
            where type.IsClass && type.Namespace?.StartsWith("DisputePortal.Domain", StringComparison.Ordinal) == true
            where !typeof(Exception).IsAssignableFrom(type)
            from property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            where property.SetMethod?.IsPublic == true && !IsInitOnly(property.SetMethod)
            select $"{type.Name}.{property.Name}";

        Assert.Empty(offenders);
    }

    // `init` accessors (records, value objects) can only be used during construction, so they're still immutable.
    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));
}
