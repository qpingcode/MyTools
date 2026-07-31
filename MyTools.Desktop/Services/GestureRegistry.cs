using Microsoft.Extensions.Logging;
using MyTools.Desktop.Utils;
using MyTools.Desktop.Views;

namespace MyTools.Desktop.Services;

public class GestureRegistry : IDisposable
{
    private MouseGestureDetector mouseGestureDetector;
    private readonly Dictionary<GestureKey, Action<MouseGestureEventArgs>> gestureActions = new();
    private readonly Dictionary<GestureKey, string> gestureActionNames = new();
    
    public GestureRegistry(ILogger<GestureRegistry> logger, MouseGestureDetector mouseGestureDetector)
    {
        this.mouseGestureDetector = mouseGestureDetector;
        // 设置查找 actionName 的方法，使用 Func 解耦依赖
        mouseGestureDetector.SetActionNameFinder(FindActionName);
        // 设置获取可能手势的方法
        mouseGestureDetector.SetPossibleGesturesFinder(GetPossibleGestures);
        mouseGestureDetector.GestureDetected += (_, args) =>
        {
            if (gestureActions.TryGetValue(new GestureKey(args.ProcessName ?? "", args.Gesture), out var action))
            {
                logger.LogDebug("Trigger Gesture: {Gestures} for {ProcessName}", args.Gesture, args.ProcessName);
                action.Invoke(args);
            }
            else if (gestureActions.TryGetValue(new GestureKey("*", args.Gesture), out action))
            {
                logger.LogDebug("Trigger Gesture: {Gestures} for {ProcessName}", args.Gesture, "*");
                action.Invoke(args);
            }
        };
    }
    
    public void StartListening()
    {
        mouseGestureDetector.Start();
    }
    
    public void RegisterGesture(MoveDirection gesture, Action<MouseGestureEventArgs> action, string actionName)
    {
        RegisterGesture([gesture], action, actionName);
    }
    
    public void RegisterGesture(MoveDirection[] gesture, Action<MouseGestureEventArgs> action, string actionName)
    {
        RegisterGesture(gesture, "*", action, actionName);
    }
    
    public void RegisterGesture(MoveDirection[] gesture, string[] processNames, Action<MouseGestureEventArgs> action, string actionName)
    {
        foreach (var processName in processNames)
        {
            RegisterGesture(gesture, processName, action, actionName);
        }
    }
    
    public void RegisterGesture(MoveDirection[] gesture, string processName, Action<MouseGestureEventArgs> action, string actionName)
    {
        if (mouseGestureDetector == null)
        {
            throw new InvalidOperationException("MouseGestureDetector is not initialized.");
        }
        if (gesture == null || gesture.Length == 0)
        {
            throw new InvalidOperationException("gesture cannot be null.");
        }
        
        var gestureKey = new GestureKey(processName, gesture);
        gestureActions[gestureKey] = action;
        gestureActionNames[gestureKey] = actionName;
    }

    /// <summary>
    /// 根据当前的手势方向和进程名查找匹配的 actionName
    /// 支持部分匹配（当前手势可能是完整手势的前缀）
    /// </summary>
    public string? FindActionName(string? processName, MoveDirection[] currentDirections)
    {
        if (currentDirections == null || currentDirections.Length == 0)
        {
            return null;
        }

        var processNameLower = processName?.ToLower() ?? "";
        
        // 先尝试精确匹配当前进程名
        var exactKey = new GestureKey(processNameLower, currentDirections);
        if (gestureActionNames.TryGetValue(exactKey, out var actionName))
        {
            return actionName;
        }
        
        // 尝试匹配通配符 "*"
        var wildcardKey = new GestureKey("*", currentDirections);
        if (gestureActionNames.TryGetValue(wildcardKey, out actionName))
        {
            return actionName;
        }
        
        // 尝试部分匹配（当前手势是某个已注册手势的前缀）
        foreach (var kvp in gestureActionNames)
        {
            var key = kvp.Key;
            // 检查进程名是否匹配（精确匹配或通配符）
            if (key.ProcessName != processNameLower && key.ProcessName != "*")
            {
                continue;
            }
            
            // 检查当前手势是否是已注册手势的前缀
            if (key.Gestures.Length >= currentDirections.Length)
            {
                bool isPrefix = true;
                for (int i = 0; i < currentDirections.Length; i++)
                {
                    if (key.Gestures[i] != currentDirections[i])
                    {
                        isPrefix = false;
                        break;
                    }
                }
                if (isPrefix)
                {
                    return kvp.Value;
                }
            }
        }
        
        return null;
    }

    /// <summary>
    /// 获取可能的手势列表
    /// </summary>
    /// <param name="processName">进程名</param>
    /// <param name="currentDirections">当前输入的手势方向（可为空）</param>
    /// <param name="maxCount">最大返回数量</param>
    /// <returns>可能的手势列表</returns>
    public List<PossibleGesture> GetPossibleGestures(string? processName, MoveDirection[]? currentDirections, int maxCount = 10)
    {
        var result = new List<PossibleGesture>();
        var processSpecificGestures = new List<PossibleGesture>();
        var globalGestures = new List<PossibleGesture>();
        var processNameLower = processName?.ToLower() ?? "";
        currentDirections ??= Array.Empty<MoveDirection>();

        foreach (var kvp in gestureActionNames)
        {
            var key = kvp.Key;
            // 检查进程名是否匹配（精确匹配或通配符）
            if (key.ProcessName != processNameLower && key.ProcessName != "*")
            {
                continue;
            }

            int matchedLength = 0;
            
            // 如果有当前输入的手势，检查匹配长度
            if (currentDirections.Length > 0)
            {
                // 检查当前手势是否是已注册手势的前缀
                if (key.Gestures.Length >= currentDirections.Length)
                {
                    bool isPrefix = true;
                    for (int i = 0; i < currentDirections.Length; i++)
                    {
                        if (key.Gestures[i] != currentDirections[i])
                        {
                            isPrefix = false;
                            break;
                        }
                    }
                    if (isPrefix)
                    {
                        matchedLength = currentDirections.Length;
                    }
                    else
                    {
                        // 不匹配，跳过
                        continue;
                    }
                }
                else
                {
                    // 当前输入的手势长度超过已注册手势，不匹配
                    continue;
                }
            }

            var possibleGesture = new PossibleGesture
            {
                Gesture = key.Gestures,
                ActionName = kvp.Value,
                MatchedLength = matchedLength
            };

            // 区分精准匹配和全局匹配
            if (key.ProcessName == processNameLower)
            {
                processSpecificGestures.Add(possibleGesture);
            }
            else if (key.ProcessName == "*")
            {
                globalGestures.Add(possibleGesture);
            }
        }
        
        result.AddRange(processSpecificGestures);

        // 对于全局手势，只有当精准匹配中不存在相同手势时才添加
        foreach (var globalGesture in globalGestures)
        {
            bool existsInProcessSpecific = processSpecificGestures.Any(ps => 
                ps.Gesture.SequenceEqual(globalGesture.Gesture));
            
            if (!existsInProcessSpecific)
            {
                result.Add(globalGesture);
            }
        }

        // 按匹配长度降序排序（完全匹配的优先），然后按手势长度排序
        result = result.OrderByDescending(g => g.MatchedLength)
                      .ThenBy(g => g.Gesture.Length)
                      .Take(maxCount)
                      .ToList();

        return result;
    }
    
    public void Dispose()
    {
        mouseGestureDetector.Dispose();
    }

    private class GestureKey
    {
        public string ProcessName { get; }
        public MoveDirection[] Gestures { get; }

        public GestureKey(string? processName, MoveDirection[] gestures)
        {
            ProcessName = processName?.ToLower() ?? "";
            Gestures = gestures;
        }

        public override int GetHashCode()
        {
            int hash = ProcessName.GetHashCode();
            foreach (var gesture in Gestures)
            {
                hash = hash * 31 + gesture.GetHashCode();
            }
            return hash;
        }

        public override bool Equals(object? obj)
        {
            if (obj is GestureKey other)
            {
                if (ProcessName != other.ProcessName)
                    return false;
                return Gestures.SequenceEqual(other.Gestures);
            }
            return false;
        }
    }
}