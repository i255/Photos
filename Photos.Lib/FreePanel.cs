using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class FreePanel : BaseView
    {
        public (BaseView, object) Attach(Func<SKRect> rect) => (this, rect);

        static Func<SKRect> Rect(BaseView item) => (Func<SKRect>)item.LayoutInfo;
        protected override void LayoutChildren()
        {
            foreach (var item in Children)
                if (item.Enabled)
                    item.Layout(Rect(item)());
        }
    }
}
