using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Plugins;
using MyTools.Plugins.Param;

namespace MyTools.Plugins
{
    public class UuidGeneratorPlugin(ILogger<UuidGeneratorPlugin> logger) : PluginBase
    {
        public override string Name => "UUID/GUID Generator";
        public override string Description => "Generate UUIDs by default, or GUIDs when specified. Supports various formats and batch generation.";
        public override List<IActionWithCommand> Actions => new() { WellKnownActions.Copy.WithDefaultCommand() };

        private readonly Icon resultIcon = new StringIcon("🔑");
        
        public override bool IsGlobalSearchPlugin => false;

        public override async Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                var items2 = new List<ResultItem>();
                items2.Add(
                    new ResultItem
                    (
                        resultIcon, 
                        "GUID Generator Help", 
                        GetHelpText(), 
                        ActionStringParam.From(GetHelpText()), 
                        90)
                );
                    
                return Result.CreateSuccessResult(items2);
            }

            var items = new List<ResultItem>();
            try
            {
                await Task.Delay(10, cancellationToken); // Simulate async operation

                var lowerQuery = query.ToLowerInvariant().Trim();

                   // Check if the query is asking for help
                if (IsHelpQuery(lowerQuery))
                {
                    items.Add(
                        new ResultItem
                        (
                            resultIcon, 
                            "GUID Generator Help", 
                            GetHelpText(), 
                            ActionStringParam.From(GetHelpText()), 
                            ResultItemPriorities.Medium)
                        );
                    
                    return Result.CreateSuccessResult(items);
                }

                var options = ParseUuidOptions(lowerQuery);
                var uuids = GenerateUuids(options.Count, options.Format);
                
                foreach (var uuid in uuids)
                {
                    items.Add(
                        new ResultItem
                        (
                            resultIcon, 
                            uuid, 
                            $"GUID ({options.Format})", 
                            ActionStringParam.From(uuid), 
                            100)
                        );
                }
                
                return Result.CreateSuccessResult(items);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while processing query");
                return Result.CreateFailure(ex.Message, ex);
            }
        }

        private bool IsHelpQuery(string query)
        {
            return query.Contains("help");
        }

        private UuidOptions ParseUuidOptions(string query)
        {
            var options = new UuidOptions
            {
                Count = 1,
                Format = UuidFormat.Standard
            };

            // Parse format
            if (query.Contains("uppercase") || query.Contains("upper"))
                options.Format = UuidFormat.Uppercase;
            else if (query.Contains("nodash") || query.Contains("no-dash") || query.Contains("no dash"))
                options.Format = UuidFormat.NoDashes;
            else if (query.Contains("braces") || query.Contains("curly") || query.Contains("{}"))
                options.Format = UuidFormat.Braces;
            else if (query.Contains("parentheses") || query.Contains("parens") || query.Contains("()"))
                options.Format = UuidFormat.Parentheses;
            else if (query.Contains("base64") || query.Contains("b64"))
                options.Format = UuidFormat.Base64;

            return options;
        }

        private List<string> GenerateUuids(int count, UuidFormat format)
        {
            var uuids = new List<string>();
            
            for (int i = 0; i < count; i++)
            {
                var uuid = Guid.NewGuid();
                var formattedUuid = FormatUuid(uuid, format);
                uuids.Add(formattedUuid);
            }
            
            return uuids;
        }

        private string FormatUuid(Guid uuid, UuidFormat format)
        {
            return format switch
            {
                UuidFormat.Uppercase => uuid.ToString("D").ToUpperInvariant(),
                UuidFormat.NoDashes => uuid.ToString("N").ToUpperInvariant(),
                UuidFormat.NoDashesLower => uuid.ToString("N").ToUpperInvariant(),
                UuidFormat.Braces => uuid.ToString("B").ToUpperInvariant(),
                UuidFormat.Parentheses => uuid.ToString("P").ToUpperInvariant(),
                UuidFormat.Base64 => Convert.ToBase64String(uuid.ToByteArray()),
                _ => uuid.ToString("D") // Standard format
            };
        }

        private string GetHelpText()
        {
            return @"Press any character to start Generator.
Format Options:
• uppercase
• nodash
• braces
• parentheses
• base64
";
        }
    }

    public class UuidOptions
    {
        public int Count { get; set; } = 1;
        public UuidFormat Format { get; set; } = UuidFormat.Standard;
    }

    public enum UuidFormat
    {
        Standard,
        Uppercase,
        NoDashes,
        NoDashesLower,
        Braces,
        Parentheses,
        Base64,
    }
}
