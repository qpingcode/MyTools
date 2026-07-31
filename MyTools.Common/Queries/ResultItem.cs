using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MyTools.Common;

public sealed partial class ResultItem(Icon icon, string title, string subTitle, IActionParams args, int priority = 0, PreviewContentType previewContentType = PreviewContentType.Text, byte[]? content = null) : ObservableObject
{
    public Icon Icon { get; } = icon;
    public string Title { get; } = title;
    public string? SubTitle { get; } = subTitle;
    public IActionParams Args { get; } = args;
    public int Priority { get; } = priority;
    public double SortScore { get; set; } = priority;
    public PreviewContentType PreviewContentType { get; set; } = previewContentType;
    public string Category { get; set; } = string.Empty;
    public byte[] Content { get; set; } = content ?? [];
    public string SourcePluginId { get; set; } = string.Empty;
    public string SourcePluginName { get; set; } = string.Empty;
    public string ResultKey { get; set; } = string.Empty;
    public string SearchQuery { get; set; } = string.Empty;

    public string ContentAsString
    {
        get => Encoding.UTF8.GetString(Content);
        set => Content = Encoding.UTF8.GetBytes(value);
    }
    
    [ObservableProperty]
    private string numberLabel = string.Empty;
    
    public IEnumerable<IActionWithCommand> AllowedActions { get; set; } = Enumerable.Empty<IActionWithCommand>();

    public ResultItem Clone()
    {
        return new ResultItem(Icon, Title, SubTitle ?? string.Empty, Args, Priority, PreviewContentType, [.. Content])
        {
            SortScore = SortScore,
            Category = Category,
            SourcePluginId = SourcePluginId,
            SourcePluginName = SourcePluginName,
            ResultKey = ResultKey,
            SearchQuery = SearchQuery,
            AllowedActions = AllowedActions,
            NumberLabel = NumberLabel,
        };
    }
}