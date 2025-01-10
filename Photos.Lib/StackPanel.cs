using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class StackPanel : BaseView
    {
        public bool IsHorizontal;
        public readonly Val<bool> IsReversed = new(false);
        public readonly Val<bool> IsCentered = new(false);

        public Func<BaseView, float> MeasureChild;
        public Val<float> ItemMargin = new(5);

        public StackPanel()
        {
            Padding.Const = 0;
        }

        public (BaseView, object) Attach(Func<float> rect) => (this, rect);
        public (BaseView, object) Attach() => MeasureChild != null ? (this, null) : throw new InvalidOperationException();

        float Measure(BaseView v) => ((Func<float>)v.LayoutInfo)?.Invoke() ?? MeasureChild(v);

        protected override void LayoutChildren()
        {
            float offset = 0;
            var size = InnerRect.Size;
            var isReversed = IsReversed;

            if (isReversed)
                offset = IsHorizontal ? size.Width : size.Height;

            if (IsCentered)
                offset += (((IsHorizontal ? size.Width : size.Height) - GetTotalSize()) / 2) * (isReversed ? -1 : 1);

            foreach (var item in Children)
            {
                if (!item.Enabled)
                    continue;

                var measure = Measure(item);
                if (isReversed)
                    offset -= measure;

                item.Layout(IsHorizontal ? SKRect.Create(InnerRect.Left + offset, InnerRect.Top, measure, size.Height) : SKRect.Create(InnerRect.Left, InnerRect.Top + offset, size.Width, measure));

                if (!isReversed)
                    offset += (measure + ItemMargin);
                else
                    offset -= ItemMargin;
            }
        }

        public float GetTotalSize()
        {
            var totalMeasure = Children.Where(x => x.Enabled).Select(x => Measure(x) + ItemMargin).Sum();
            totalMeasure = Math.Max(0, totalMeasure - ItemMargin);
            return totalMeasure;
        }
    }
}
