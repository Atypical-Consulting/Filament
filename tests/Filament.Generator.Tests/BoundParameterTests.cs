using Xunit;

namespace Filament.Generator.Tests;

/// <summary>
/// The first deferred #88 sub-slice: a BOUND scalar parameter (<Display Value="@count" />). #88's own
/// refusal called this "parent->child reactive plumbing that is not implemented". It IS now: the child's
/// @Value inlines into the parent's scope as a live effect on the parent's signal (decision 90).
/// BoundComposeTests MEASURES it against Blazor.
/// </summary>
public class BoundParameterTests
{
    // A bound reactive parameter makes the child's @Value a LIVE binding: an effect + setText on the
    // parent's signal, NOT a folded constant.
    [Fact]
    public void BoundReactiveParameter_InlinesAsALiveEffectOnTheParentSignal()
    {
        var js = File.ReadAllText(Generate.ToTempFixture("BoundParamInline.razor"));
        Assert.Contains("effect(", js);
        Assert.Contains("count.value", js);        // reads the PARENT's lifted signal
        Assert.DoesNotContain("[bound-parameter]", js);
    }
}
