using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.Logging;
using MyTools.Desktop.Views;

namespace MyTools.Desktop.Utils;

public class MouseGestureDetector : IDisposable
{
    private readonly ILogger<MouseGestureDetector> _logger;
    private readonly ILogger<MouseTrailWindow> _trailWindowLogger;
    private readonly Queue<GestureMessage> _msgQueue = new(32);
    private Point _point;
    
    private string? _previousProcessName;
    private bool _isCapturing;
    private readonly MouseHook _mouseHook;
    private MouseTrailWindow? _trailWindow;
    private MouseTrailViewModel? _trailViewModel;
    private readonly GestureDirectionStorage _gestureDirectionStorage;
    
    private Point _startPoint;
    private const int InitialValidMove = 5;
    private bool _initialMoveValid;
    private readonly MouseHelper _mouseHelper;
    private Func<string?, MoveDirection[], string?>? _findActionName;
    private Func<string?, MoveDirection[]?, int, List<PossibleGesture>>? _getPossibleGestures;

    public event EventHandler<MouseGestureEventArgs>? GestureDetected;

    /// <summary>
    /// 设置查找 actionName 的方法
    /// </summary>
    /// <param name="findActionName">查找 actionName 的委托，参数为进程名和当前手势方向数组，返回匹配的 actionName</param>
    public void SetActionNameFinder(Func<string?, MoveDirection[], string?> findActionName)
    {
        _findActionName = findActionName;
    }

    /// <summary>
    /// 设置获取可能手势的方法
    /// </summary>
    /// <param name="getPossibleGestures">获取可能手势的委托，参数为进程名、当前手势方向数组和最大数量，返回可能的手势列表</param>
    public void SetPossibleGesturesFinder(Func<string?, MoveDirection[]?, int, List<PossibleGesture>> getPossibleGestures)
    {
        _getPossibleGestures = getPossibleGestures;
    }

    public MouseGestureDetector(MouseHelper mouseHelper, ILogger<MouseGestureDetector> logger, ILogger<MouseTrailWindow> trailWindowLogger)
    {
        _logger = logger;
        _trailWindowLogger = trailWindowLogger;
        _mouseHook = new MouseHook(logger);
        _gestureDirectionStorage = new();
        _mouseHook.MouseHookEvent += OnMouseHookEvent;
        _mouseHelper = mouseHelper;
    }

    private void OnMouseHookEvent(MouseHook.MouseHookEventArgs e)
    {
        if (_mouseHelper.IsSimulatingInput || e.ExtraInfo == MouseHelper.SimulatedEventTag)
        {
            return;
        }
        var message = e.Msg;
        switch (message)
        {
            case Native.MouseMsg.WM_RBUTTONDOWN:
                _startPoint = GetCurrentPoint();
                _point = _startPoint;
                Post(GestureMessage.GestureButtonDown);
                e.Handled = true;
                break;
            case Native.MouseMsg.WM_MOUSEMOVE:
                if (Throttling()) break;
                _point = GetCurrentPoint();
                Post(GestureMessage.GestureButtonMove);
                break;
            case Native.MouseMsg.WM_RBUTTONUP:
                _point = GetCurrentPoint();
                Post(GestureMessage.GestureButtonUp);
                e.Handled = true;
                break;
        }
    }

    private Point GetCurrentPoint()
    {
        Native.GetCursorPos(out var point);
        return new Point(point.x, point.y);
    }

    private int maxAllowedIntervalMilliseconds = 60;

    private DateTime? lastEventTime;
    bool Throttling()
    {
        if (!_initialMoveValid)
        {
            return false;
        }
        if (lastEventTime == null)
        {
            lastEventTime = new DateTime();
            return false;
        }

        var now = DateTime.Now;
        var timeInterval = now - lastEventTime.Value;
        if (timeInterval.TotalMilliseconds > maxAllowedIntervalMilliseconds)
        {
            lastEventTime = now;
            return false;
        }

        return true;
    }

    
    public void Start()
    {
        _mouseHook.StartListening();
        _logger.LogInformation("MouseGestureDetector started");

        while (true)
        {
            var message = WaitForMessage();
            switch (message)
            {
                case GestureMessage.GestureButtonDown:
                    OnMouseDown();
                    break;
                case GestureMessage.GestureButtonMove:
                    OnMouseMove();
                    break;
                case GestureMessage.GestureButtonUp:
                    OnMouseUp();
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
    
    GestureMessage WaitForMessage()
    {
        GestureMessage gestureMessage;
        lock (_msgQueue)
        {
            if (_msgQueue.Count == 0) Monitor.Wait(_msgQueue);
            gestureMessage = _msgQueue.Dequeue();
        }
        return gestureMessage;
    }
    
    private void OnMouseDown()
    {
        if (_isCapturing)
        {
            _logger.LogDebug("Mouse Right Click Down, but ignore as isCapturing");
            return;
        }
        else
        {
            _isCapturing = true;
            _logger.LogDebug("Mouse Right Click Down, start capturing");
        }

        ResetInvalidMove();
        _gestureDirectionStorage.Reset();

        _previousProcessName = GetProcessName();
        _trailViewModel = new MouseTrailViewModel();
        _trailViewModel.ProcessName = _previousProcessName;
        
        // 初始状态：显示前5个可能的手势
        UpdatePossibleGestures();
        
        Application.Current.Dispatcher.Invoke(() =>
        {
            _trailWindow = new MouseTrailWindow(_trailWindowLogger, _trailViewModel);
            _trailWindow.Show();
        });

    }

    private string GetProcessName()
    {
        var previousFocusHwd = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(previousFocusHwd, out uint processId);
        var process = Process.GetProcessById((int)processId);
        return process.ProcessName;
    }

    void ResetInvalidMove()
    {
        _initialMoveValid = false;
    }

    private void OnMouseMove()
    {
        if (!_isCapturing)
        {
            return;
        }

        if (!IsValidMove())
        {
            return;
        }
        _gestureDirectionStorage.Detect(_point);
        if (_trailViewModel != null)
        {
            _trailViewModel.DirectionsText = _gestureDirectionStorage.DirectionsToDisplay;
            _trailViewModel.AddPoint(_point);
            
            // 更新可能的手势列表
            UpdatePossibleGestures();
        }
       
        Application.Current.Dispatcher.Invoke(() =>
        {
            _trailWindow?.UpdateDrawing();
        });
        
        bool IsValidMove()
        {
            if (_initialMoveValid) return true;
        
            var initialMoveDist = GetPointDistance(ref _point, ref _startPoint);
            if (initialMoveDist > InitialValidMove)
            {
                _initialMoveValid = true;
            }
            return _initialMoveValid;
        }
        
        float GetPointDistance(ref Point a, ref Point b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return (int)Math.Sqrt(dx * dx + dy * dy);
        }
    }

    private void OnMouseUp()
    {
    
        _isCapturing = false;
        
        if (!_initialMoveValid)
        {
           // _logger.LogDebug("Mouse Right Click Up ( Invalid Move, Simulating Right Click )");
            _mouseHelper.RightClick(_point);
            Application.Current.Dispatcher.Invoke(() =>
            {
                _trailWindow?.Close();
                _trailViewModel = null;
            });
            return;
        }
        else
        {
            ResetInvalidMove();
           // _logger.LogDebug("Mouse Right Click Up");
        }
        
        Application.Current.Dispatcher.Invoke(() =>
        {
            _trailWindow?.Close();
            _trailViewModel = null;
        });
        
        try
        {
            _logger.LogDebug("Starting gesture for process: {ProcessName} {Gestures}", _previousProcessName, _gestureDirectionStorage.DirectionsToDisplay);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get process name or file name, {processName}", _previousProcessName);
        }

        var selection = Clipboard.GetText();
        var directions = _gestureDirectionStorage.Directions;
        GestureDetected?.Invoke(
            this,
            new MouseGestureEventArgs(_previousProcessName, _point, directions, selection));

        _gestureDirectionStorage.Reset();
    }
    
    private void Post(GestureMessage msg)
    {
        lock (_msgQueue)
        {
            _msgQueue.Enqueue(msg);
            Monitor.Pulse(_msgQueue);
        }
    }

    private enum GestureMessage : uint
    {
        GestureButtonDown = 1,
        GestureButtonUp = 2,
        GestureButtonMove = 3,
    }

    private void UpdatePossibleGestures()
    {
        if (_trailViewModel == null) return;

        var currentDirections = _gestureDirectionStorage.Directions;
        
        if (_getPossibleGestures != null)
        {
            var possibleGestures = _getPossibleGestures(_previousProcessName, currentDirections.Length > 0 ? currentDirections : null, 5);
            
            _trailViewModel.PossibleGestures.Clear();
            foreach (var gesture in possibleGestures)
            {
                _trailViewModel.PossibleGestures.Add(gesture);
            }
            
            // 如果没有匹配的手势，显示提示信息
            if (possibleGestures.Count == 0 && currentDirections.Length > 0)
            {
                _trailViewModel.NoMatchMessage = "当前没有可匹配的手势";
            }
            else
            {
                _trailViewModel.NoMatchMessage = string.Empty;
            }
        }
        else
        {
            _trailViewModel.PossibleGestures.Clear();
            _trailViewModel.NoMatchMessage = string.Empty;
        }
    }

    public void Dispose()
    {
        _trailWindow?.Close();
        _mouseHook.Dispose();
    }
}

public class MouseGestureEventArgs(
    string? processName,
    Point lastPoint,
    MoveDirection[] directions,
    string? selectionText)
    : EventArgs
{
    public MoveDirection[] Gesture { get; } = directions;

    public Point LastPoint { get; } = lastPoint;
    public string? ProcessName { get; } = processName;

    public string? SelectionText { get; } = selectionText;
}