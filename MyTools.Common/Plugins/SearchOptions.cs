namespace MyTools.Common.Plugins;

public class SearchOptions(SearchFrom searchFrom)
{
    public SearchFrom SearchFrom => searchFrom;
}

public enum SearchFrom
{
    Global,
    Plugin
}