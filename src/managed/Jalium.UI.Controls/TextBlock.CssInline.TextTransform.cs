using System.Text;

namespace Jalium.UI.Controls;

public partial class TextBlock
{
    internal sealed partial class CssTextFlowLayout
    {
        private void PrepareTextTransformContext(ref List<CssInlineUnit> units)
        {
            if (!units.Any(static unit => !unit.Source.Atomic && !unit.Source.EmptyFrame &&
                unit.Source.Text.HasTextTransformRequest()))
                return;

            var joined = new StringBuilder();
            var hidden = new List<bool>();
            var caseModes = new List<byte>();
            var sources = new HashSet<CssInlineSource>();
            foreach (var unit in units)
            {
                var source = unit.Source;
                if (!sources.Add(source) || source.EmptyFrame) continue;
                source.TransformOffset = joined.Length;
                if (source.Atomic)
                {
                    joined.Append(source.Block ? '\n' : '\ufffc');
                    hidden.Add(false);
                    caseModes.Add(0);
                }
                else
                {
                    source.Text.AppendTransformContext(joined, hidden, caseModes);
                }
            }

            var context = new TextTransformContextData(joined.ToString(), hidden.ToArray(), caseModes.ToArray());
            var changed = false;
            foreach (var source in sources)
            {
                if (source.Atomic || source.EmptyFrame) continue;
                source.TransformContext = context;
                changed |= source.Text.SetSharedTransformContext(context, source.TransformOffset);
            }

            if (!changed) return;

            var rebuilt = new List<CssInlineUnit>(units.Count);
            var indexMap = new int[units.Count + 1];
            var collapsedSpace = false;
            for (var i = 0; i < units.Count;)
            {
                var source = units[i].Source;
                var j = i + 1;
                while (j < units.Count && ReferenceEquals(units[j].Source, source)) j++;
                var newStart = rebuilt.Count;
                for (var k = i; k < j; k++) indexMap[k] = newStart;
                if (source.EmptyFrame || source.Atomic)
                {
                    for (var k = i; k < j; k++)
                    {
                        indexMap[k] = rebuilt.Count;
                        rebuilt.Add(units[k]);
                    }
                    if (source.Atomic) collapsedSpace = false;
                }
                else
                {
                    EmitSourceUnits(source, rebuilt, ref collapsedSpace);
                }
                source.LastUnit = rebuilt.Count - 1;
                i = j;
            }
            indexMap[units.Count] = rebuilt.Count;
            for (var f = 0; f < _floatEvents.Count; f++)
            {
                var ev = _floatEvents[f];
                if (ev.Position >= 0 && ev.Position < indexMap.Length)
                    _floatEvents[f] = ev with { Position = indexMap[ev.Position] };
            }
            foreach (var frame in _frames)
            {
                var last = -1;
                foreach (var source in _sources)
                    if (BelongsTo(source, frame)) last = Math.Max(last, source.LastUnit);
                if (last >= 0) frame.LastUnit = last;
            }
            units = rebuilt;
        }
    }
}
