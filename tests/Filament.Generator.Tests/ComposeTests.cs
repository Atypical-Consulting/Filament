using Xunit;

namespace Filament.Generator.Tests;

public class ComposeTests
{
    /// <summary>
    /// Static-leaf composition: &lt;Greeting Name="World" /&gt; resolves the sibling Greeting.razor, folds
    /// the static param, and INLINES the child's span. No unresolved &lt;Greeting&gt; element, no import of a
    /// child, and the @Name read is the compile-time constant, not the literal expression text.
    /// </summary>
    [Fact]
    public void EmittedCompose_InlinesTheChildWithTheFoldedParam()
    {
        var js = File.ReadAllText(Generate.ComposeToTemp());
        Assert.Contains("document.createElement('span')", js);   // the child's root, inlined
        Assert.Contains("greeting", js);                          // its id survives
        Assert.Contains("World", js);                             // the param folded to a constant
        Assert.DoesNotContain("createElement('Greeting')", js);   // NOT emitted as an unknown element
        Assert.DoesNotContain("@Name", js);                       // NOT the literal expression
    }
}
