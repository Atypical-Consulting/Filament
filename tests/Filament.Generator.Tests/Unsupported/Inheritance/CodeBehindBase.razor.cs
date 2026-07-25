// The .cs HALF of the code-behind partial base CodeBehindBase (register A3, decision 173).
//
// This is DATA for the generator's File.Exists probe, not code this test project compiles: it is a
// `partial class` whose other half is a .razor the test project does not compile as a component, so it
// is excluded from the build in Filament.Generator.Tests.csproj (<Compile Remove="Unsupported/**/*.cs" />).
//
// It IS valid Blazor code-behind, and the register built exactly this shape (Base.razor + Base.razor.cs)
// with `dotnet build` succeeding. It contributes a lifecycle override this compiler cannot read, so
// merging only the .razor half would leave `count` at 0 where Blazor renders 7. The generator refuses.
using Microsoft.AspNetCore.Components;

namespace Filament.Generator.Tests.Fixtures;

public partial class CodeBehindBase : ComponentBase
{
    protected override void OnInitialized() => count = 7;
}
