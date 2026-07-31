using System.IO;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Plugins;
using MyTools.Plugins.Param;

namespace MyTools.Plugins
{
    public class XmlFormatterPlugin(ILogger<XmlFormatterPlugin> logger) : PluginBase
    {
        public override string Name => "Xml Formatter";
        public override string Description => "Format and validate XML";
        public override List<IActionWithCommand> Actions => [WellKnownActions.Copy.WithDefaultCommand()];

        private readonly Icon resultIcon = new StringIcon("📄");

        public override ViewModelType ViewModelType => ViewModelType.LeftRight;

        public override Task InitializeAsync()
        {
            return Task.CompletedTask;
        }
        
        public override async Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Result.CreateEmpty();
            }
            
            var items = new List<ResultItem>();
            try
            {
                // 尝试格式化 XML
                var formattedXml = await FormatXmlAsync(query, cancellationToken);
                
                logger.LogInformation("Format result for '{Query}': {FormattedXml}", query, formattedXml);
                
                if (formattedXml != null)
                {
                    var item = new ResultItem(resultIcon, formattedXml, "Formatted XML", ActionStringParam.From(formattedXml), ResultItemPriorities.Medium);
                    items.Add(item);
                }
                else
                {
                    // 如果格式化失败，返回错误信息
                    var errorItem = new ResultItem(resultIcon, "Invalid XML format - Please check your XML syntax", "Error", ActionStringParam.From("Invalid XML format"), ResultItemPriorities.Medium);
                    items.Add(errorItem);
                }
                    
                return Result.CreateSuccessResult(items);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while formatting XML");
                return Result.CreateFailure(ex.Message, ex);
            }
        }

        private Task<string?> FormatXmlAsync(string input, CancellationToken cancellationToken)
        {
            try
            {
                // 清理输入字符串，移除前后空白字符
                var cleanInput = input.Trim();
                
                // 使用 XmlDocument 进行解析和格式化
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(cleanInput);
                
                // 创建格式化选项
                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = "\t",  // 使用制表符作为缩进
                    NewLineChars = "\n",
                    OmitXmlDeclaration = false,
                    Encoding = Encoding.UTF8
                };
                
                // 重新序列化以格式化
                using var stringWriter = new StringWriter();
                using var xmlWriter = XmlWriter.Create(stringWriter, settings);
                xmlDoc.Save(xmlWriter);
                
                var result = stringWriter.ToString();
                logger.LogInformation("Formatted XML: {Result}", result);
                return Task.FromResult<string?>(result);
            }
            catch (XmlException ex)
            {
                // XML 格式无效，记录错误并返回 null
                logger.LogWarning(ex, "Invalid XML format: {Input}", input);
                return Task.FromResult<string?>(null);
            }
            catch (Exception ex)
            {
                // 其他异常，记录错误并返回 null
                logger.LogError(ex, "Error formatting XML: {Input}", input);
                return Task.FromResult<string?>(null);
            }
        }
    }
}
