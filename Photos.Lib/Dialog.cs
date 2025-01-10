using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class Dialog : FreePanel
    {
        //public Func<SKSize> ContentSize;
        public readonly Val<float> ButtonSize = new(-1);

        protected FreePanel DialogContainer;
        public SKPaint OverlayPaint;
        public Func<SKRect> DialogRect;
        public bool Modal = true;

        public Dialog()
        {
            Enabled.Const = false;
            DialogContainer = new FreePanel() { Link = Attach(() => DialogRect()), Padding = { Func = () => Padding } };
            //Rect = () => Parent.OuterRect;

            MouseHandler = e =>
            {
                if (Modal)
                    e.Consume(null);
            };

            KeyUpHandler = e =>
            {
                if (Modal)
                    e.Consume();
            };

            RenderFrameHandler = RenderFrame;

            Background.Color = SKColors.Black.WithAlpha(0xF0);
            Background.Style = SKPaintStyle.Fill;
            OverlayPaint = new SKPaint() { Color = SKColors.Black.WithAlpha(0x50), Style = SKPaintStyle.Fill, IsAntialias = true };
            OnDispose += () => OverlayPaint.Dispose();
        }

        void RenderFrame(SKCanvas ctx)
        {
            ctx.DrawRect(OuterRect, OverlayPaint);

            var rect = DialogContainer.Position;
            var roundRect = new SKRoundRect(rect, 3);
            ctx.DrawRoundRect(roundRect, Background);
        }
    }
}
