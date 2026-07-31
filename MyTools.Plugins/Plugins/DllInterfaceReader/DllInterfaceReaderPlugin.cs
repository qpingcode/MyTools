using System.Diagnostics;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.Plugins;
using MyTools.Plugins.Param;

namespace MyTools.Plugins
{
    public class DllInterfaceReaderPlugin : PluginBase
    {
        public override string Name => "Dll Interface Reader";
        public override string Description => "Reader interfaces from a specified DLL and allow searching by class name, table code, table name, and field names.";

        private IActionWithCommand IlSpyAction;
        public override List<IActionWithCommand> Actions => [IlSpyAction];

        private readonly Icon resultIcon = new StringIcon("🔍");
        
        public override bool IsGlobalSearchPlugin => false;

        private readonly string dllPath = @"C:\git\GitHub\WiseTechGlobal\Glow\DotNet\bin\CoreServer\CargoWise.Glow.Model.Interfaces.dll";
        private readonly string annotationsDllPath = @"C:\git\GitHub\WiseTechGlobal\Glow\DotNet\bin\CoreServer\WTG.Glow.Data.Annotations.dll";

        private readonly ILogger<DllInterfaceReaderPlugin> logger;
        private Type? tableCodeAttributeType;
        private Type? tableNameAttributeType;
        private Lazy<List<InterfaceInfo>>? interfaceInfos;
        private ConfigurationSetting? spyPathSetting;
        public DllInterfaceReaderPlugin(ILogger<DllInterfaceReaderPlugin> logger, IConfigurationRegistry registry)
        {
            this.logger = logger;
            IlSpyAction = new OpenInILSpyAction(() => spyPathSetting?.GetValue<string>()).WithDefaultCommand();
        }
        
        public override Task InitializeAsync()
        {
            var tempDllPath = File.Exists(dllPath) ? CopyDllToTemp(dllPath) : string.Empty;
            var tempAnnotationsDllPath = File.Exists(annotationsDllPath) ? CopyDllToTemp(annotationsDllPath) : string.Empty;

            if (tempDllPath == string.Empty || tempAnnotationsDllPath == string.Empty)
            {
                logger.LogError("DLL文件不存在，插件未能初始化");
            }
            var interfaceAssembly = Assembly.LoadFrom(tempDllPath);
            var annotationsAssembly = Assembly.LoadFrom(tempAnnotationsDllPath);

            tableCodeAttributeType = annotationsAssembly.GetType("WTG.Glow.Data.Annotations.TableCodeAttribute");
            tableNameAttributeType = annotationsAssembly.GetType("WTG.Glow.Data.Annotations.TableNameProviderAttribute");
            interfaceInfos = new Lazy<List<InterfaceInfo>>(() => ReadDllInterfaces(interfaceAssembly));
            return Task.CompletedTask;
        }
        
        public override async Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
        {
            try
            {
                await Task.Delay(10, cancellationToken);
                var searchQuery = query.Trim().ToLower();
                var matchedResults = SearchInterfaces(searchQuery);
                return Result.CreateSuccessResult(matchedResults);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "搜索DLL接口时发生错误");
                return Result.CreateFailure(ex.Message, ex);
            }
        }
        
        protected override void AddPluginSettings(ConfigurationCategory pluginCategory, IConfigurationRegistry configurationRegistry)
        {
            base.AddPluginSettings(pluginCategory, configurationRegistry);
            var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ILSpy", "ILSpy.exe");
            spyPathSetting = configurationRegistry.AddSetting(pluginCategory, "ILSpyPathSetting", "ILSpy Install Path", "ILSpy Install Path", defaultPath, options: SettingOptions.None);
        }

        private string CopyDllToTemp(string originPath)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "MyTools", "DllCache");
            Directory.CreateDirectory(tempDir);
            var fileName = Path.GetFileName(originPath);
            var tempPath = Path.Combine(tempDir, fileName ?? "temp.dll");
            File.Copy(originPath, tempPath, true);
            return tempPath;
        }

        private List<ResultItem> SearchInterfaces(string searchQuery)
        {
            var matchedResults = new List<ResultItem>();
            var initializedInterfaceInfos = interfaceInfos?.Value
                ?? throw new InvalidOperationException("The DLL interface reader plugin has not been initialized.");
            foreach (var interfaceInfo in initializedInterfaceInfos)
            {
                var extraScoreIfMatched = 200;
                AddResultIfMatch("Class",interfaceInfo.ClassName, searchQuery, interfaceInfo, matchedResults);
                AddResultIfMatch("TableCode",interfaceInfo.TableCode, searchQuery, interfaceInfo, matchedResults, extraScoreIfMatched);
                AddResultIfMatch("TableName",interfaceInfo.TableNames, searchQuery, interfaceInfo, matchedResults, extraScoreIfMatched);
                foreach (var fieldName in interfaceInfo.FieldNames)
                {
                    AddResultIfMatch("Field",fieldName, searchQuery, interfaceInfo, matchedResults);
                }
            }

            return matchedResults.OrderBy(i => i.Priority).ToList();
        }

        private void AddResultIfMatch(
            string prefix, 
            string? tryToMatch, 
            string query, 
            InterfaceInfo interfaceInfo,
            List<ResultItem> matchedResults, 
            int extraScoreIfMatched = 0)
        {
            if (tryToMatch == null)
            {
                return;
            }
            var matchScore = GetMatchScore(tryToMatch.ToLowerInvariant(), query.ToLowerInvariant());
            if (matchScore > 0)
            {
                if (matchScore > 40)
                {
                    matchScore += extraScoreIfMatched;
                }
                matchedResults.Add(new ResultItem(
                    resultIcon,
                    $"{prefix}: {tryToMatch}",
                    interfaceInfo.ToString(),
                    new ActionParamT<InterfaceInfo>(interfaceInfo),
                    matchScore
                ));
            }
        }

        private int GetMatchScore(string classInfo, string searchQuery)
        {
            return searchQuery switch
            {
                _ when classInfo.Equals(searchQuery) => 100,
                _ when classInfo.StartsWith(searchQuery) => 50,
                _ when classInfo.Contains(searchQuery)   => 10,
                _ => 0
            };
        }

        private List<InterfaceInfo> ReadDllInterfaces(Assembly interfaceAssembly)
        {
            var dllInterfaces = new List<InterfaceInfo>();
            var types = interfaceAssembly.GetExportedTypes()
                .Where(t => t.IsInterface || t.IsClass)
                .Where(t => !t.IsAbstract || t.IsInterface)
                .ToList();

            foreach (var type in types)
            {
                var interfaceInfo = AnalyzeType(type);
                if (interfaceInfo != null)
                {
                    dllInterfaces.Add(interfaceInfo);
                }
            }

            return dllInterfaces.OrderBy(i => i.ClassName).ToList();
        }

        private InterfaceInfo? AnalyzeType(Type type)
        {
            try
            {
                var tableCode = GetAttributeValueFromAllInterfaces(type, tableCodeAttributeType, "TableCode");
                var tableNames = GetAttributeValueFromAllInterfaces(type, tableNameAttributeType, "TableNames");

                var fieldNames = GetFieldNames(type);

                return new InterfaceInfo
                {
                    DllPath = dllPath,
                    ClassName = type.Name,
                    FullClassName = type.FullName ?? type.Name,
                    TableCode = tableCode,
                    FieldNames = fieldNames,
                    IsInterface = type.IsInterface,
                    AssemblyName = type.Assembly.GetName().Name ?? "Unknown",
                    TableNames = tableNames
                };
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Analyze Type {TypeName} Error", type.FullName);
                return null;
            }
        }

        private String? GetAttributeValueFromAllInterfaces(Type type, Type? attributeType, string attributePropertyName)
        {
            var value = GetAttributeValue(type, attributeType, attributePropertyName);
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
            
            var interfaces = type.GetInterfaces();
            foreach (var interfaceType in interfaces)
            {
                var value2 = GetAttributeValue(interfaceType, attributeType, attributePropertyName);
                if (!string.IsNullOrEmpty(value2))
                {
                    return value2;
                }
            }

            if (type.BaseType != null && type.BaseType != typeof(object))
            {
                var baseTableCode = GetAttributeValue(type.BaseType, attributeType, attributePropertyName);
                if (!string.IsNullOrEmpty(baseTableCode))
                {
                    return baseTableCode;
                }
            }

            return null;
        }

        private string? GetAttributeValue(Type type, Type? attributeType, string attributePropertyName)
        {
            if (attributeType == null)
            {
                return null;
            }
            
            try
            {
                var attr = type.GetCustomAttribute(attributeType);
                if (attr != null)
                {
                    var property = attributeType.GetProperty(attributePropertyName);
                    if (property != null)
                    {
                        var value = property.GetValue(attr);
                        if (value != null)
                        {
                            if (value is IEnumerable<string> eumEnumerable)
                            {
                                return string.Join(",", eumEnumerable);
                            }  
                            
                            if (value is string[] arryValue)
                            {
                                return string.Join(",", arryValue);
                            }

                            if (value is string stValue)
                            {
                                return stValue;
                            }

                            throw new NotSupportedException();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "获取类型 {TypeName} 的TableCode时发生错误", type.FullName);
            }

            return null;
        }

        private List<string> GetFieldNames(Type type)
        {
            var fieldNames = new List<string>();

            try
            {
                var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                fieldNames.AddRange(from property in properties where property.CanRead || property.CanWrite select property.Name);

                var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
                fieldNames.AddRange(fields.Select(field => field.Name));

                if (fieldNames.Count == 0)
                {
                    var interfaces = type.GetInterfaces();
                    foreach (var interfaceType in interfaces)
                    {
                        var interfaceFields = GetFieldNames(interfaceType);
                        fieldNames.AddRange(interfaceFields);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "获取类型 {TypeName} 的字段名时发生错误", type.FullName);
            }

            return fieldNames.Distinct().OrderBy(f => f).ToList();
        }
    }
    
    public class OpenInILSpyAction(Func<string?> getExecutePath) : IActionWithCommand
    {
        public string Name => "Open in ILSpy";
        public string Description => "Open the selected interface in ILSpy to view its implementation.";

        public Task<ActionResult> ExecuteAsync(IActionParams args)
        {
            if (args is not ActionParamT<InterfaceInfo> parameter)
            {
                return Task.FromResult(ActionResult.CreateFailure("Error Parameter"));
            }

            var interfaceInfo = parameter.GetValue();
            try
            {

                var ilSpyPath = getExecutePath();
                if (!File.Exists(ilSpyPath))
                {
                    throw new FileNotFoundException($"Cannot find ILSpy.exe in {ilSpyPath}");
                }

                var arguments = $"\"{interfaceInfo.DllPath}\" --newinstance --navigateto T:{interfaceInfo.FullClassName}";
            
                var startInfo = new ProcessStartInfo
                {
                    FileName = ilSpyPath,
                    Arguments = arguments,
                    UseShellExecute = true
                };

                Process.Start(startInfo);
                return Task.FromResult(ActionResult.CreateSuccess(""));
            }
            catch (Exception ex)
            {
                return Task.FromResult(ActionResult.CreateFailure(ex.Message));
            }
        }

        public string Command => Commands.DefaultCommand;
    }

    public class InterfaceInfo
    {
        public string DllPath { get; set; } = string.Empty;
        public string ClassName { get; set; } = string.Empty;

        public string FullClassName { get; set; } = string.Empty;

        public string? TableCode { get; set; } = string.Empty;

        public List<string> FieldNames { get; set; } = new();

        public bool IsInterface { get; set; }

        public string AssemblyName { get; set; } = string.Empty;

        public string? TableNames { get; set; } = string.Empty;

        public override string ToString()
        {
            var tableNamesInfo = TableNames != null ? $", TableNames: {TableNames}" : "";
            var tableCodeInfo = TableCode != null ? $", TableCode: {TableCode}" : "";
            return $"ClassName: {ClassName}{tableCodeInfo}{tableNamesInfo}";
        }
    }
}
