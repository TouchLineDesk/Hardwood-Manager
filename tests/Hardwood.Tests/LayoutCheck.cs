using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Hardwood.Tests;

/// <summary>
/// Finds text the player can't fully read on the current screen (roadmap 1: "nothing covered up or lost"):
/// <list type="bullet">
/// <item>trimmed: a TextBlock showing "…" because its text didn't fit;</item>
/// <item>overflowing: single-line text wider than its own box;</item>
/// <item>too tall: wrapped text with more lines than its box shows;</item>
/// <item>cut by row: text spilling out of its table cell;</item>
/// <item>clipped: text cut by a clipping parent (a table cell, a fixed-size panel). Text inside a scroll viewer that
/// is merely scrolled out of view doesn't count; it can be scrolled to.</item>
/// </list>
/// </summary>
public static class LayoutCheck
{
    public sealed record Problem(string Kind, string Text, string Where, double Missing)
    {
        public override string ToString() => $"{Kind,-11} {Missing,5:0}px  \"{Text}\"  [{Where}]";
    }

    /// <summary>Text blocks are allowed this many pixels of slack (antialiasing, rounding).</summary>
    private const double Slack = 2;

    public static List<Problem> Scan(TopLevel root)
    {
        root.UpdateLayout();
        var problems = new List<Problem>();
        // Measure positions inside the text-size scale, in the same units as the text widths.
        Visual space = root.GetVisualDescendants().OfType<LayoutTransformControl>().FirstOrDefault()?.Child ?? (Visual)root;
        foreach (var tb in root.GetVisualDescendants().OfType<TextBlock>())
        {
            if (!tb.IsEffectivelyVisible || tb.Bounds.Width <= 0 || tb.Bounds.Height <= 0) continue;
            var text = tb.Text ?? tb.Inlines?.Text;
            if (string.IsNullOrWhiteSpace(text)) continue;
            if (tb.Opacity <= 0.01) continue;
            var layout = tb.TextLayout;
            if (layout == null) continue;
            string where = Describe(tb) + (Environment.GetEnvironmentVariable("HW_LAYOUT_DEBUG") == "1" ? $" w={tb.Bounds.Width:0} lw={tb.TextLayout.Width:0} nat={NaturalWidth(tb, text):0} m={tb.Margin} chain={string.Join(">", tb.GetVisualAncestors().Take(4).Select(a => a.GetType().Name + ":" + a.Bounds.Width.ToString("0") + (a is Avalonia.Controls.Primitives.TemplatedControl t ? "p" + t.Padding : "")))}" : "");

            if (layout.TextLines.Any(l => l.HasCollapsed))
            {
                problems.Add(new Problem("trimmed", Short(text), where, NaturalWidth(tb, text) - tb.Bounds.Width));
                continue;
            }
            double inner = tb.Bounds.Width - tb.Padding.Left - tb.Padding.Right;
            double textW = layout.Width;
            if (tb.TextWrapping == TextWrapping.NoWrap && textW > inner + Slack)
            {
                problems.Add(new Problem("overflowing", Short(text), where, textW - inner));
                continue;
            }

            // Wrapped text taller than the box it was given (a fixed-height table row): the extra lines are lost.
            if (tb.TextWrapping != TextWrapping.NoWrap && tb.MaxLines == 0)
            {
                double fullH = WrappedHeight(tb, text, tb.Bounds.Width - tb.Padding.Left - tb.Padding.Right);
                double boxH = tb.Bounds.Height - tb.Padding.Top - tb.Padding.Bottom;
                if (fullH > boxH + Math.Max(Slack, tb.FontSize / 2))
                {
                    problems.Add(new Problem("too tall", Short(text), where, fullH - boxH));
                    continue;
                }
            }

            // Where does the ink actually sit in window coordinates?
            var origin = tb.TranslatePoint(new Point(tb.Padding.Left, 0), space);
            if (origin == null) continue;
            double inkW = Math.Min(textW, inner);
            if (tb.TextAlignment == TextAlignment.Right) origin = origin.Value.WithX(origin.Value.X + inner - inkW);
            else if (tb.TextAlignment == TextAlignment.Center) origin = origin.Value.WithX(origin.Value.X + (inner - inkW) / 2);
            var ink = new Rect(origin.Value, new Size(inkW, Math.Min(layout.Height, tb.Bounds.Height)));
            if (ink.Width <= 0 || ink.Height <= 0) continue;

            // A table cell is narrower or shorter than its text: the row hides the rest (rows don't clip themselves;
            // the rows panel does, so the generic check below can't see it).
            if (tb.FindAncestorOfType<DataGridCell>() is { } cell && cell.TranslatePoint(new Point(0, 0), space) is { } ctl)
            {
                var seen = ink.Intersect(new Rect(ctl, cell.Bounds.Size));
                double missW = ink.Width - seen.Width, missH = ink.Height - seen.Height;
                if (seen.Width > 0 && (missW > Slack || missH > Math.Max(Slack, ink.Height / 4)))
                {
                    problems.Add(new Problem("cut by row", Short(text), where, Math.Max(missW, missH)));
                    continue;
                }
            }

            foreach (var anc in tb.GetVisualAncestors().OfType<Visual>())
            {
                if (anc is ScrollContentPresenter or DataGridRowsPresenter or DataGridColumnHeadersPresenter or DataGridRow or DataGridCellsPresenter) break; // scrolled out of view is fine; it can be scrolled to
                if (anc is TopLevel) break;
                if (!anc.ClipToBounds) continue;
                var tl = anc.TranslatePoint(new Point(0, 0), space);
                if (tl == null) continue;
                var clip = new Rect(tl.Value, anc.Bounds.Size);
                var seen = ink.Intersect(clip);
                if (seen.Width <= 0 || seen.Height <= 0) break; // wholly hidden (a collapsed panel, an animation), not cut
                double missW = ink.Width - seen.Width, missH = ink.Height - seen.Height;
                // Descenders and line spacing may brush a cell edge; a third of a line is a real cut.
                if (missW > Slack || missH > Math.Max(Slack, ink.Height / 3))
                {
                    problems.Add(new Problem("clipped", Short(text), where + " in " + anc.GetType().Name, Math.Max(missW, missH)));
                    break;
                }
            }
        }
        return problems;
    }

    /// <summary>How wide the text is on one line with nothing in the way.</summary>
    public static double NaturalWidth(TextBlock tb, string text)
    {
        var tf = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
        using var layout = new Avalonia.Media.TextFormatting.TextLayout(text, tf, tb.FontSize, null, letterSpacing: tb.LetterSpacing);
        return layout.WidthIncludingTrailingWhitespace;
    }

    /// <summary>How tall the text is when wrapped to <paramref name="width"/> with no height limit.</summary>
    public static double WrappedHeight(TextBlock tb, string text, double width)
    {
        var tf = new Typeface(tb.FontFamily, tb.FontStyle, tb.FontWeight, tb.FontStretch);
        using var layout = new Avalonia.Media.TextFormatting.TextLayout(text, tf, tb.FontSize, null, textWrapping: tb.TextWrapping,
            maxWidth: Math.Max(1, width), lineHeight: tb.LineHeight, letterSpacing: tb.LetterSpacing);
        return layout.Height;
    }

    private static string Short(string s)
    {
        s = s.Replace('\n', ' ');
        return s.Length > 60 ? s[..57] + "..." : s;
    }

    /// <summary>The nearest named view or data context type, to say which page a problem is on.</summary>
    private static string Describe(Visual v)
    {
        foreach (var a in v.GetVisualAncestors())
            if (a is UserControl uc) return uc.GetType().Name;
        return v.GetType().Name;
    }
}
