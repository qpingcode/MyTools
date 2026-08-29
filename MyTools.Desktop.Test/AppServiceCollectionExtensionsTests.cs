using Microsoft.Extensions.DependencyInjection;
using MyTools.Common.Config.Interfaces;
using MyTools.Desktop.Storage;
using NUnit.Framework;

namespace MyTools.Desktop.Test;

[TestFixture]
public class AppServiceCollectionExtensionsTests
{
    [Test]
    public void AddApplicationServices_RegistersAppBootstrapperAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddApplicationServices();

        var registration = services.Single(service =>
            service.ServiceType == typeof(AppBootstrapper));
        Assert.That(registration.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
    }

    [Test]
    public void AddConfigurationSystem_BuildsWithoutCircularStorageDependency()
    {
        var services = new ServiceCollection();
        services.AddConfigurationSystem();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.That(provider.GetRequiredService<IConfigurationStorage>(),
            Is.InstanceOf<CompositeConfigurationStorage>());
    }
}
