using MyTools.Desktop.Views;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class AutoHideSuppressionStateTests
{
    [Test]
    public void Enter_WhenScopesAreNested_RemainsSuppressedUntilLastScopeEnds()
    {
        var state = new AutoHideSuppressionState();

        using (state.Enter())
        {
            Assert.That(state.IsSuppressed, Is.True);
            using (state.Enter())
            {
                Assert.That(state.IsSuppressed, Is.True);
            }

            Assert.That(state.IsSuppressed, Is.True);
        }

        Assert.That(state.IsSuppressed, Is.False);
    }

    [Test]
    public void Dispose_WhenCalledMoreThanOnce_ReleasesScopeOnlyOnce()
    {
        var state = new AutoHideSuppressionState();
        var scope = state.Enter();

        scope.Dispose();
        scope.Dispose();

        Assert.That(state.IsSuppressed, Is.False);
    }
}
