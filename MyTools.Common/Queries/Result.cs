namespace MyTools.Common;

public class Result(bool success, string? errorMessages, IEnumerable<ResultItem> items, Exception? exception = null)
{
    public bool Success { get; } = success;
    public string? ErrorMessage { get; } = errorMessages;
    public IEnumerable<ResultItem> Items { get; } = items;
    
    public Exception? Exception { get; } = exception;

    public static Result CreateEmpty()
        => new Result(true, null, Enumerable.Empty<ResultItem>());
    
    public static Result CreateSuccessResult(IEnumerable<ResultItem> items)
        => new Result(true, null, items.OrderByDescending(x => x.SortScore).ThenByDescending(x => x.Priority));
    
    public static Result CreateFailure(string errorMessage, Exception? ex) 
        => new Result(false, errorMessage, Enumerable.Empty<ResultItem>(), ex);
}


