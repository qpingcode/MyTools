using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using MyTools.Common;
using MyTools.Common.Plugins;
using MyTools.Common.Utils;
using MyTools.Plugins.Param;

namespace MyTools.Plugins;

public class ProcessKillerPlugin(IMemoryCache memoryCache) : PluginBase
{
    public override string Name => "Process Killer";
    public override string Description => "Search and manage running processes";
    public override List<IActionWithCommand> Actions => [new KillProcessAction().WithDefaultCommand()];
    
    private Icon _icon = new StringIcon("💀");

    public override async Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
    {
        var results = new List<ResultItem>();

        await Task.Delay(500, cancellationToken);
        
        var allProcessInfos = await memoryCache.GetOrCreateAsync(PluginConstants.PluginCachePrefix + "ProcessKiller_AllProcess", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5);
            
            var processPortMap = await GetProcessPortMapAsync(cancellationToken);
            var processes = Process.GetProcesses();
            var processInfos = processes.Select(process => ProcessInfo.FromProcess(process, processPortMap.GetValueOrDefault(process.Id)))
                .OrderBy(p => p.Name);
            return processInfos.ToList();
        });
        
        if (allProcessInfos != null && !cancellationToken.IsCancellationRequested)
        {
            bool isQueryInteger = int.TryParse(query, out int intValue);
            
            var processInfos = allProcessInfos
                .Where(p =>
                {
                    if (isQueryInteger && (p.Port == intValue || p.Id == intValue))
                    {
                        return true;
                    }
                    return string.IsNullOrEmpty(query) || StringUtils.IsSubsequence(query, p.Name);
                })
                .Take(30);
            
            foreach (var process in processInfos)
            {
                results.Add(new ResultItem(_icon, process.DisplayTitle, process.DisplaySubTitle, ActionStringParam.From(process.Id.ToString()), ResultItemPriorities.Medium));
            }
        }
        
        cancellationToken.ThrowIfCancellationRequested();
        
        var result = Result.CreateSuccessResult(results);
        return result;
    }

    private async Task<Dictionary<int, int>> GetProcessPortMapAsync(CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, int>();
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return result;
            }
            
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 5) continue;

                // 检查是否是TCP或UDP连接
                if (parts[0] != "TCP" && parts[0] != "UDP") continue;

                // 解析本地地址和端口
                var localAddress = parts[1];
                var portStr = localAddress.Split(':').Last();
                if (int.TryParse(portStr, out int port) && int.TryParse(parts[4], out int pid))
                {
                    result[pid] = port;
                }
            }
        }
        catch
        {
            // ignored
        }
        return result;
    }
} 