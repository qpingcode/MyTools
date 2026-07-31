using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Extensions.Logging;
using MyTools.Desktop.Utils;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;

namespace MyTools.Desktop.Views;

public partial class MouseTrailWindow
{
    private double _dpiScale = -1;
    private readonly ILogger<MouseTrailWindow> _logger;

    private int _originalTop;
    private int _originalLeft;

    private Polyline? _trail;
    private Polyline? _trailBorder;
    private TextBlock? _processNameTextBlock;
    private Border? _processNameBorder;
    private StackPanel? _possibleGesturesPanel;
    private Border? _possibleGesturesBorder;
    private TextBlock? _noMatchTextBlock;
    private Border? _noMatchBorder;
    private readonly MouseTrailViewModel viewModel;
    private double _maxActionNameWidth = 0;
    
    public MouseTrailWindow(ILogger<MouseTrailWindow> logger, MouseTrailViewModel viewModel)
    {
        _logger = logger;
        this.viewModel = viewModel;
        DataContext = viewModel;
        
        InitializeComponent();
        InitializeCanvas();
    }

    private void InitializeCanvas()
    {
        _trail = new Polyline
        {
            Stroke = Brushes.CadetBlue,
            StrokeThickness = 2,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        _trailBorder = new Polyline
        {
            Stroke = Brushes.White,
            StrokeThickness = 4,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        TrailCanvas.Children.Add(_trailBorder);
        TrailCanvas.Children.Add(_trail);

        // ProcessName TextBlock - 单独放置，固定位置，不受其他元素影响
        _processNameTextBlock = new TextBlock()
        {
            Text = string.Empty,
            Foreground = Brushes.White,
            FontSize = 24,
            FontWeight = FontWeights.Normal,
            Background = Brushes.Transparent,
            Padding = new Thickness(5),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        _processNameBorder = new Border
        {
            Padding = new Thickness(5),
            Child = _processNameTextBlock,
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            MinWidth = 200,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        TrailCanvas.Children.Add(_processNameBorder);

        _possibleGesturesPanel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 10, 0, 0),
        };

        _possibleGesturesBorder = new Border
        {
            Padding = new Thickness(10),
            Child = _possibleGesturesPanel,
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            MinWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        TrailCanvas.Children.Add(_possibleGesturesBorder);

        // 无匹配提示
        _noMatchTextBlock = new TextBlock()
        {
            Text = string.Empty,
            Foreground = Brushes.Orange,
            FontSize = 24,
            FontWeight = FontWeights.Normal,
            Background = Brushes.Transparent,
            Padding = new Thickness(5),
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };

        _noMatchBorder = new Border
        {
            Padding = new Thickness(5),
            Child = _noMatchTextBlock,
            Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)),
            MinWidth = 300,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        TrailCanvas.Children.Add(_noMatchBorder);

        // Get the DPI scale and screen bounds
        SourceInitialized += (_, _) =>
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget == null) return;
            _dpiScale = source.CompositionTarget.TransformToDevice.M11;
            var screen = Screen.FromHandle(new WindowInteropHelper(this).Handle);
            var screenBounds = screen.Bounds;
            _logger.LogInformation("Screen bounds: Left {Left}, Top {Top}, Width {Width}, Height {Height}",
                screenBounds.Left, screenBounds.Top, screenBounds.Width, screenBounds.Height);

            Left = screenBounds.Left / _dpiScale;
            Top = screenBounds.Top / _dpiScale;
            Width = screenBounds.Width / _dpiScale;
            Height = screenBounds.Height / _dpiScale;

            _originalLeft = screenBounds.Left;
            _originalTop = screenBounds.Top;

            _logger.LogInformation("Window initialized with DPI scale: {DpiScale}, Size: {Width}x{Height}",
                _dpiScale, Width, Height);
        };
    }

    public void UpdateDrawing()
    {
        if (_dpiScale < 0)
        {
            return;
        }
        
        DrawPolyLine();
        DrawText();
    }

    private void DrawText()
    {
        Dispatcher.Invoke(() =>
        {
            if (_processNameTextBlock != null) _processNameTextBlock.Text = viewModel.ProcessName;
            
            UpdatePossibleGestures();
            
            if (_noMatchTextBlock != null)
            {
                _noMatchTextBlock.Text = viewModel.NoMatchMessage;
            }
        });
    }

    private void UpdatePossibleGestures()
    {
        if (_possibleGesturesPanel == null) return;

        _possibleGesturesPanel.Children.Clear();

        if (viewModel.PossibleGestures.Count == 0)
        {
            return;
        }

        // 先计算所有 actionName 的最大宽度（每次重新计算，不累积）
        var tempTextBlock = new TextBlock
        {
            FontSize = 24,
            FontWeight = FontWeights.Normal
        };
        
        double maxWidth = 0; 
        foreach (var gesture in viewModel.PossibleGestures)
        {
            tempTextBlock.Text = gesture.ActionName;
            tempTextBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            maxWidth = Math.Max(maxWidth, tempTextBlock.DesiredSize.Width);
        }
        
        _maxActionNameWidth = _maxActionNameWidth > (maxWidth + 20) ? _maxActionNameWidth : (maxWidth + 20);
        
        var matchedColor = Brushes.LightBlue;
        var unmatchedColor = Brushes.Gray;

        foreach (var gesture in viewModel.PossibleGestures.Take(5))
        {
            var gesturePanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 5, 0, 5)
            };
            
            var actionNameTextBlock = new TextBlock
            {
                Text = gesture.ActionName,
                Foreground = Brushes.LightGreen,
                FontSize = 24,
                FontWeight = FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Width = _maxActionNameWidth,
                TextAlignment = TextAlignment.Left,
                Margin = new Thickness(0, 0, 15, 0)
            };

            gesturePanel.Children.Add(actionNameTextBlock);
            
            var directionTextBlock = new TextBlock
            {
                FontSize = 32,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center
            };

            for (int i = 0; i < gesture.Gesture.Length; i++)
            {
                var run = new Run(DirectionToString(gesture.Gesture[i]))
                {
                    Foreground = i < gesture.MatchedLength ? matchedColor : unmatchedColor
                };
                directionTextBlock.Inlines.Add(run);
                if (i < gesture.Gesture.Length - 1)
                {
                    directionTextBlock.Inlines.Add(new Run(" ") { Foreground = i < gesture.MatchedLength ? matchedColor : unmatchedColor });
                }
            }

            gesturePanel.Children.Add(directionTextBlock);

            _possibleGesturesPanel.Children.Add(gesturePanel);
        }
    }

    private string DirectionToString(MoveDirection direction)
    {
        return direction switch
        {
            MoveDirection.Up => "↑",
            MoveDirection.Down => "↓",
            MoveDirection.Left => "←",
            MoveDirection.Right => "→",
            _ => ""
        };
    }

    private void DrawPolyLine()
    {
        if (_trail == null || _trailBorder == null) return;
        
        Dispatcher.Invoke(() =>
        {
            _trail.Points.Clear();
            _trailBorder.Points.Clear();
            
            foreach (var screenPoint in viewModel.Points)
            {
                var relativePoint = new Point(
                    (screenPoint.X - _originalLeft) / _dpiScale,
                    (screenPoint.Y - _originalTop) / _dpiScale
                );
                _trail.Points.Add(relativePoint);
                _trailBorder.Points.Add(relativePoint);
            }
            
            double processNameYPos = 0;
            if (_processNameBorder != null)
            {
                double borderWidth = Math.Max(_processNameBorder.MinWidth, _processNameBorder.ActualWidth);
                double processNameXPos = (Width - borderWidth) / 2;
                processNameYPos = Height * 0.35; // 距离顶部15%的位置，居中偏上
                Canvas.SetLeft(_processNameBorder, processNameXPos);
                Canvas.SetTop(_processNameBorder, processNameYPos);
            }
            
            if (_possibleGesturesBorder != null)
            {
                double borderWidth = Math.Max(_possibleGesturesBorder.MinWidth, _possibleGesturesBorder.ActualWidth);
                double gesturesXPos = (Width - borderWidth) / 2;
                double gesturesYPos = processNameYPos + (_processNameBorder?.ActualHeight ?? 0) + 15; // 15像素间距
                Canvas.SetLeft(_possibleGesturesBorder, gesturesXPos);
                Canvas.SetTop(_possibleGesturesBorder, gesturesYPos);
                _possibleGesturesBorder.Visibility = viewModel.PossibleGestures.Count > 0 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
            
            if (_noMatchBorder != null)
            {
                double borderWidth = Math.Max(_noMatchBorder.MinWidth, _noMatchBorder.ActualWidth);
                double noMatchXPos = (Width - borderWidth) / 2;
                double noMatchYPos = processNameYPos + (_processNameBorder?.ActualHeight ?? 0) + 15; // 15像素间距
                Canvas.SetLeft(_noMatchBorder, noMatchXPos);
                Canvas.SetTop(_noMatchBorder, noMatchYPos);
                _noMatchBorder.Visibility = !string.IsNullOrEmpty(viewModel.NoMatchMessage) 
                    ? Visibility.Visible 
                    : Visibility.Collapsed;
            }
        });
    }
}