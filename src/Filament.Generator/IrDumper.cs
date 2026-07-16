using System.Text;
using Microsoft.AspNetCore.Razor.Language.Intermediate;

namespace Filament.Generator;

/// <summary>
/// `--dump-ir`. Not decoration: the IR is the only place where "the tag helper chain
/// is wired" and "the markup passes are removed" are OBSERVABLE. Both fail silently,
/// so the ability to look at the artifact is a debugging tool AND the evidence.
///
/// Read it like this:
///   MarkupElementIntermediateNode  &lt;button&gt;      structure survived  => passes removed
///   HtmlAttributeIntermediateNode  attr 'onclick'  the '@' is GONE      => descriptors resolved
///   HtmlAttributeIntermediateNode  attr '@onclick' the '@' SURVIVED     => descriptors MISSING (decision 53)
///   MarkupBlockIntermediateNode    *OPAQUE*        HTML as a string     => passes NOT removed (decision 52)
/// </summary>
public static class IrDumper
{
    public static string Dump(IntermediateNode root)
    {
        var sb = new StringBuilder();
        new Walker(sb).Visit(root);
        return sb.ToString();
    }

    sealed class Walker(StringBuilder sb) : IntermediateNodeWalker
    {
        int _depth;

        public override void VisitDefault(IntermediateNode node)
        {
            sb.Append(new string(' ', _depth * 2)).Append(node.GetType().Name);
            // The Source span is what every diagnostic must carry, and it is also the
            // only way to tell a node the USER wrote from one Razor SYNTHESISED (the
            // default @usings have no span). Both facts are load-bearing, so the dump
            // shows them rather than leaving them to be assumed.
            sb.Append(node.Source is { } s
                ? $"  @{Path.GetFileName(s.FilePath)}({s.LineIndex + 1},{s.CharacterIndex + 1})"
                : "  @<synthesised>");
            switch (node)
            {
                case MarkupElementIntermediateNode e: sb.Append($"  <{e.TagName}>"); break;
                case MarkupBlockIntermediateNode m: sb.Append($"  *OPAQUE* {Q(m.Content)}"); break;
                case HtmlAttributeIntermediateNode a: sb.Append($"  attr '{a.AttributeName}'"); break;
                case TagHelperIntermediateNode t: sb.Append($"  TAGHELPER <{t.TagName}>"); break;
                case SetKeyIntermediateNode: sb.Append("  @key"); break;
                case ReferenceCaptureIntermediateNode: sb.Append("  @ref"); break;
                case ClassDeclarationIntermediateNode c: sb.Append($"  class {c.ClassName}"); break;
                case MethodDeclarationIntermediateNode m: sb.Append($"  method {m.MethodName}"); break;
                case HtmlAttributeValueIntermediateNode h: sb.Append($"  htmlValue prefix={Q(h.Prefix)}"); break;
                case CSharpExpressionAttributeValueIntermediateNode c: sb.Append($"  csValue prefix={Q(c.Prefix)}"); break;
                case IntermediateToken t: sb.Append($"  [{(t.IsHtml ? "HTML" : "CS")}] {Q(t.Content)}"); break;
            }
            sb.AppendLine();
            _depth++;
            base.VisitDefault(node);
            _depth--;
        }

        static string Q(string? s) => s is null
            ? "<null>"
            : "\"" + (s.Length > 120 ? s[..120] + "..." : s).Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    }
}
