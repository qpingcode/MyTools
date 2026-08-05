using Microsoft.Extensions.DependencyInjection;
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
}
