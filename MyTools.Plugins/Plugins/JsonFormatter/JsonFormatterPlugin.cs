using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Plugins;
using MyTools.Plugins.Param;

namespace MyTools.Plugins
{
    public class JsonFormatterPlugin(ILogger<JsonFormatterPlugin> logger) : PluginBase
    {
        public override string Name => "Json Formatter";
        public override string Description => "Format and validate JSON";
        public override List<IActionWithCommand> Actions => [WellKnownActions.Copy.WithDefaultCommand()];

        private readonly Icon resultIcon = new StringIcon("📄");

        public override ViewModelType ViewModelType => ViewModelType.LeftRight;

        public override Task InitializeAsync()
        {
            return Task.CompletedTask;
        }
        
        public override Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Task.FromResult(Result.CreateEmpty());
            }
            
            var items = new List<ResultItem>();
            try
            {
                var formattedJson = FormatJsonAsync(query);
                
                if (formattedJson != null)
                {
                    var item = new ResultItem(resultIcon, formattedJson, "Formatted JSON", ActionStringParam.From(formattedJson), ResultItemPriorities.Medium);
                    items.Add(item);
                }
                else
                {
                    var errorItem = new ResultItem(resultIcon, "Invalid JSON format", "Error", ActionStringParam.From("Invalid JSON format"), ResultItemPriorities.Medium);
                    items.Add(errorItem);
                }
                    
                return Task.FromResult(Result.CreateSuccessResult(items));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while formatting JSON");
                return Task.FromResult(Result.CreateFailure(ex.Message, ex));
            }
        }

        private string? FormatJsonAsync(string input)
        {
            try
            {
                using var document = JsonDocument.Parse(input);
                
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                
                var formattedJson = JsonSerializer.Serialize(document, options);
                return formattedJson;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}

