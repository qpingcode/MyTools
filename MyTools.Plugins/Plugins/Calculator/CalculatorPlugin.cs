using Microsoft.Extensions.Logging;
using MyTools.Common;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using MyTools.Common.Plugins;
using MyTools.Common.Localization;
using MyTools.Plugins.Param;

namespace MyTools.Plugins
{
    public class CalculatorPlugin(ILogger<CalculatorPlugin> logger, ILocalizationService localization) : PluginBase
    {
        public override string PluginId => "Calculator";
        public override string Name => localization.GetCaption("Plugin.Calculator.Name", "Calculator");
        public override string Description => localization.GetCaption(
            "Plugin.Calculator.Description",
            "Perform basic arithmetic calculations.");
        public override List<IActionWithCommand> Actions => new() { WellKnownActions.Copy.WithDefaultCommand() };

        private readonly Icon resultIcon = new StringIcon("🧮");
        
        public override bool IsGlobalSearchPlugin => true;

        public override async Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Result.CreateEmpty();
            }

            var items = new List<ResultItem>();
            try
            {
                await Task.Delay(10, cancellationToken); // Simulate async operation

                // Validate if the query is a valid arithmetic expression
                if (IsArithmeticExpression(query))
                {
                    try
                    {
                        var result = EvaluateExpression(query);
                        items.Add(
                            new ResultItem
                            (
                                resultIcon, 
                                result.ToString(CultureInfo.InvariantCulture), 
                                "Calculator", 
                                ActionStringParam.From(result.ToString(CultureInfo.InvariantCulture)), 
                                100)
                            );
                        return Result.CreateSuccessResult(items);
                    }
                    catch (Exception)
                    {
                        return Result.CreateFailure(new LocalizedMessage(
                            "Plugin.Calculator.InvalidExpression",
                            "Invalid arithmetic expression."));
                    }
                }

                return Result.CreateFailure(new LocalizedMessage(
                    "Plugin.Calculator.InvalidQuery",
                    "Query is not a valid arithmetic expression."));
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

        private bool IsArithmeticExpression(string query)
        {
            // Check if the query contains only numbers, parentheses, and arithmetic operators
            return Regex.IsMatch(query, @"^[\d\s\.\+\-\*/\(\)]+$");
        }

        private double EvaluateExpression(string expression)
        {
            // Use DataTable to evaluate the arithmetic expression
            var dataTable = new DataTable();
            dataTable.CaseSensitive = false;
            return Convert.ToDouble(dataTable.Compute(expression, string.Empty));
        }
    }
}
