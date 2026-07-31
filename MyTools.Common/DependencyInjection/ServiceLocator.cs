using Microsoft.Extensions.DependencyInjection;

namespace MyTools.Common.DependencyInjection;

public static class ServiceLocator
{
    static IServiceProvider? serviceProvider;
    
    public static IServiceProvider ServiceProvider
    {
        get
        {
            if (serviceProvider == null)
            {
                throw  new InvalidOperationException("ServiceProvider has not been initialized. Please Initialize first.");
            }
            return serviceProvider;
        }
        set
        {
            if (serviceProvider != null)
            {
                throw new InvalidOperationException("ServiceProvider has already been initialized.");
            }
            serviceProvider = value;
        }
    }
    
    public static T GetRequiredService<T>() where T : class
    {
        return ServiceProvider.GetRequiredService<T>();
    }
    
    public static T? GetService<T>() where T : class
    {
        return ServiceProvider.GetService<T>();
    }
    
    public static IEnumerable<T> GetServices<T>() where T : class
    {
        return ServiceProvider.GetServices<T>();
    }
}