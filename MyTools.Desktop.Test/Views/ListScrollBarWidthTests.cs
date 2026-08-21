using System.Windows;
using System.Windows.Controls.Primitives;
using MyTools.Desktop.Components;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class ListScrollBarWidthTests
{
    [Test]
    public void BasicListView_ScrollBarStyle_IsThin()
    {
        var resources = new BasicListView().Resources;
        var style = resources[typeof(ScrollBar)] as Style;

        Assert.That(style, Is.Not.Null);
        var setters = style!.Setters.OfType<Setter>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(FindSetter(setters, FrameworkElement.WidthProperty), Is.EqualTo(6.0));
            Assert.That(FindSetter(setters, FrameworkElement.MinWidthProperty), Is.EqualTo(6.0));
            Assert.That(FindSetter(setters, FrameworkElement.MaxWidthProperty), Is.EqualTo(6.0));
            Assert.That(resources[SystemParameters.VerticalScrollBarWidthKey], Is.EqualTo(6.0));
        });
    }

    private static object? FindSetter(IEnumerable<Setter> setters, DependencyProperty property)
    {
        return setters.FirstOrDefault(setter => setter.Property == property)?.Value;
    }
}
