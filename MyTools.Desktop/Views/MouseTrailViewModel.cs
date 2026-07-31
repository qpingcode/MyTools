using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MyTools.Desktop.Views;

public partial class MouseTrailViewModel : ObservableObject
{
    [ObservableProperty]
    private bool showTrail;

    [ObservableProperty]
    private string directionsText = string.Empty;

    [ObservableProperty]
    private string processName = string.Empty;

    [ObservableProperty]
    private string actionName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PossibleGesture> possibleGestures = new();

    [ObservableProperty]
    private string noMatchMessage = string.Empty;

    public ObservableCollection<Point> Points { get; } = new();

    public void AddPoint(Point point)
    {
        Points.Add(point);
        if (Points.Count > 0)
        {
            ShowTrail = true;
        }
    }
}