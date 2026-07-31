using MyTools.Common;

namespace MyTools.Desktop.Components;

public interface IVisibleItemProvider
{
    List<ResultItem> GetVisibleItems();
}