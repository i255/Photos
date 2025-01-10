using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class FlowPanel : BaseView
    {
        public required Func<BaseView, SKSize> MeasureChild;
        public Val<SKSize> ItemMargin = new(new SKSize(5, 5));

        public FlowPanel()
        {
            Padding.Const = 0;
        }

        protected override void LayoutChildren()
        {
            float x = 0;
            float y = 0;
            var size = InnerRect.Size;
            float lastRowHeight = 0;
            var margin = ItemMargin.Value;

            foreach (var item in Children)
            {
                if (!item.Enabled)
                    continue;

                var measure = MeasureChild(item);
                if (x > 0 && measure.Width + x > size.Width)
                {
                    x = 0;
                    y += lastRowHeight;
                    lastRowHeight = 0;
                }

                item.Layout(SKRect.Create(RenderPadding.Left + x, RenderPadding.Top + y, measure.Width, measure.Height));
                lastRowHeight = Math.Max(lastRowHeight, measure.Height + margin.Height);
                x += measure.Width + margin.Width;
            }
        }

    }
}
