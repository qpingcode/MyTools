using MyTools.Common.Localization;

namespace MyTools.Common;

public class Result(bool success, string? errorMessages, IEnumerable<ResultItem> items, Exception? exception = null,
    LocalizedMessage? localizedErrorMessage = null)
{
    public bool Success { get; } = success;
    public string? ErrorMessage { get; } = errorMessages;
    public LocalizedMessage? LocalizedErrorMessage { get; } = localizedErrorMessage;
    public IEnumerable<ResultItem> Items { get; } = items;
    
    public Exception? Exception { get; } = exception;

    public static Result CreateEmpty()
        => new Result(true, null, Enumerable.Empty<ResultItem>());
    
    public static Result CreateSuccessResult(IEnumerable<ResultItem> items)
        => new Result(true, null, items
            .OrderByDescending(x => x.SortScore)
            .ThenByDescending(x => x.Priority)
            .ThenByDescending(x => x.CreatedAt ?? DateTime.MinValue));
    
    public static Result CreateFailure(string errorMessage, Exception? ex) 
        => new Result(false, errorMessage, Enumerable.Empty<ResultItem>(), ex);

    public static Result CreateFailure(LocalizedMessage errorMessage, Exception? ex = null)
        => new Result(false, errorMessage.FormatFallback(), Enumerable.Empty<ResultItem>(), ex, errorMessage);
}


