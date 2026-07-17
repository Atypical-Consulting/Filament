using System.Text;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace Filament.Generator;

/// <summary>
/// The C# text Roslyn parses, plus the map back to the .razor the author actually wrote.
///
/// WHY THIS IS A CLASS AND NOT AN `int _prefix`. Phase 3 fed Roslyn ONE block (@code) after
/// ONE constant prefix, so mapping an offset back was a subtraction. Rows' template C# is
/// SPLICED TOGETHER OUT OF MANY SPANS from many places in the file -- the @foreach header
/// from (12,14), the `}` from (15,1), `row.Id` from (14,57) -- interleaved with text this
/// compiler synthesised (the class wrapper, the markers). A subtraction cannot describe
/// that, and a diagnostic that points at synthesised text points at a file the author
/// never wrote.
///
/// So every character is either LITERAL (this compiler's, unmapped) or a SEGMENT (one IR
/// node's raw text, mapped exactly). Segments are contiguous copies of a node's raw text,
/// so an offset inside one is an offset inside that node, and SourceOffset does the rest.
///
/// A refusal that lands on literal text is the TOOL being broken, not the input, so it is
/// reported as such rather than being given a plausible-looking neighbouring location.
/// </summary>
public sealed class WrappedSource
{
    readonly StringBuilder _sb = new();
    readonly List<Segment> _segments = [];

    readonly record struct Segment(int Start, int Length, IntermediateNode Node, string Raw);

    public int Length => _sb.Length;
    public override string ToString() => _sb.ToString();

    /// <summary>Text this compiler synthesised. It maps to nothing, because it IS nothing.</summary>
    public void Literal(string text) => _sb.Append(text);

    /// <summary>One IR node's raw text, copied verbatim and remembered.</summary>
    public void Node(IntermediateNode node, string raw)
    {
        _segments.Add(new Segment(_sb.Length, raw.Length, node, raw));
        _sb.Append(raw);
    }

    /// <summary>An offset in the wrapped text -> the exact (line, column) in the .razor, or null.</summary>
    public SourceSpan? Map(int offset)
    {
        foreach (var s in _segments)
            if (offset >= s.Start && offset <= s.Start + s.Length)
                return SourceOffset.At(s.Node, s.Raw, offset - s.Start);
        return null;
    }
}
