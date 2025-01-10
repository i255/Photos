using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class ImageBox : BaseView
    {
        public Func<(SKImage, SKRect)> Image;
        public Action OnClick;
        public Action OnAltClick;
        static SKPaint _paint = new ();

        public Val<bool> IsHighlighted = new Val<bool>(false);
        public readonly SKPaint Highlighted;

        public ImageBox()
        {
            RenderFrameHandler = ctx =>
            {
                (var img, var rect) = Image();
                if (img != default)
                {
                    if (rect == default)
                    {
                        var size = Size - img.Info.Size;
                        ctx.DrawImage(img, size.Width / 2, size.Height / 2, HighQualitySampling, _paint);
                    }
                    else
                        ctx.DrawImage(img, rect, HighQualitySampling, _paint);
                }

                if (IsHighlighted)
                    ctx.DrawRect(OuterRect, Highlighted);
            };
            MouseHandler = e => DetectClick(e, OnClick, OnAltClick);
            Highlighted = new SKPaint() { Color = SKColors.Orange.WithAlpha(0x70), Style = SKPaintStyle.Fill, IsAntialias = true };
            OnDispose += () => Highlighted?.Dispose();
        }
    }
}
