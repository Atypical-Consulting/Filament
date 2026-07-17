using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace Filament.Generator;

/// <summary>
/// One refusal: the code, why, and WHERE. All three are required.
///
/// THE CODES ARE THE SPEC'S, AND THE TOOL DOES NOT SQUAT THAT NAMESPACE (decision 61):
///   FIL0001  out-of-subset C# construct   (CSharpFrontEnd)
///   FIL0002  out-of-subset type           (CSharpFrontEnd)
///   FIL0003  out-of-subset Razor          (TemplateCompiler)
/// Failures of the TOOL -- bad wiring, an IR shape that cannot exist -- are not "your
/// source is unsupported" and carry FIL-WIRING, which cannot be mistaken for a spec code.
///
/// This record was nested inside TemplateCompiler while Phase 2 owned exactly one code.
/// Phase 3 adds a second producer, so it moved out here rather than being copied: a
/// diagnostic type described in two places is decision 53's "the test measured a COPY of
/// the wiring" waiting to happen.
/// </summary>
public sealed record Diagnostic(string Code, string Reason, string Message, SourceSpan? Source)
{
    /// <summary>"file(line,col)" -- 1-based, the way every compiler on earth prints it.</summary>
    public string Location => Source is { } s
        ? $"{Path.GetFileName(s.FilePath)}({s.LineIndex + 1},{s.CharacterIndex + 1})"
        : "<no source span>";

    public override string ToString() => $"{Location}: {Code}: [{Reason}] {Message}";
}

/// <summary>
/// Offsets inside a node's raw text -> a real (line, column) in the .razor file.
///
/// ONE description, used by both front ends. The template compiler needs it to point at
/// a token inside an @code block; the C# front end needs it to map every Roslyn span
/// back through the wrapper it parsed. Copying the arithmetic into both is how the two
/// drift and one of them starts reporting "somewhere in this file".
/// </summary>
public static class SourceOffset
{
    /// <summary>
    /// A span pointing INTO a node's raw text at <paramref name="offset"/>, so a
    /// diagnostic about the third line of an @code block points at the third line and
    /// not at the @code keyword. Razor gives the node the span of its first character;
    /// the rest is counting newlines.
    /// </summary>
    public static SourceSpan? At(IntermediateNode node, string raw, int offset)
    {
        if (node.Source is not { } s) return null;

        var before = raw[..Math.Clamp(offset, 0, raw.Length)];
        var newlines = before.Count(c => c == '\n');
        var lastNl = before.LastIndexOf('\n');
        var col = lastNl < 0 ? s.CharacterIndex + offset : offset - lastNl - 1;

        return new SourceSpan(s.FilePath, s.AbsoluteIndex + offset, s.LineIndex + newlines, col, 0);
    }
}
