using MyTools.Common.Localization;

namespace MyTools.Common;

public class Result(bool success, string? errorMessages, IEnumerable<ResultItem> items, Exception? exception = null,
    LocalizedMessage? localizedErrorMessage = null, string? emptyStateTitle = null,
    string? emptyStateDescription = null)
{
    public bool Success { get; } = success;
    public string? ErrorMessage { get; } = errorMessages;
    public LocalizedMessage? LocalizedErrorMessage { get; } = localizedErrorMessage;
    public IEnumerable<ResultItem> Items { get; } = items;
    public string? EmptyStateTitle { get; } = emptyStateTitle;
    public string? EmptyStateDescription { get; } = emptyStateDescription;
    
    public Exception? Exception { get; } = exception;

    public static Result CreateEmpty()
        => new Result(true, null, Enumerable.Empty<ResultItem>());
    
    public static Result CreateSuccessResult(
        IEnumerable<ResultItem> items, string? emptyStateTitle = null, string? emptyStateDescription = null)
        => new Result(true, null, items
            .OrderByDescending(x => x.SortScore)
            .ThenByDescending(x => x.Priority)
            .ThenByDescending(x => x.CreatedAt ?? DateTime.MinValue),
            emptyStateTitle: emptyStateTitle,
            emptyStateDescription: emptyStateDescription);
    
    public static Result CreateFailure(string errorMessage, Exception? ex) 
        => new Result(false, errorMessage, Enumerable.Empty<ResultItem>(), ex);

    public static Result CreateFailure(LocalizedMessage errorMessage, Exception? ex = null)
        => new Result(false, errorMessage.FormatFallback(), Enumerable.Empty<ResultItem>(), ex, errorMessage);
}


