using MyTools.Plugins;

namespace MyTools.Common;

public interface ISearcher
{
    Task<Result> SearchAsync(IPlugin? plugin, string searchText, CancellationToken cancellationToken);
}