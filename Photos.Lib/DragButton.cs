using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class DragButton : BaseView
    {
        public readonly Val<SKSize> ButtonSize = new(new SKSize(100, 100));
        public SKPoint DragMidPoint;
        SKRect DragArea;

        public required Action<SKPoint> UpdatePosition;
        public required Func<SKPoint> GetPosition;
        SKPoint dragOffset;
        public bool IsDragging { get; private set; }
        public SKPath Icon;

        public DragButton()
        {
            RenderFrameHandler = Render;
            MouseHandler = OnMouse;
            Background.Color = SKColors.Black.WithAlpha(0x30);
            Foreground.Color = SKColors.Beige.WithAlpha(0xB0);
            Foreground.Style = SKPaintStyle.StrokeAndFill;
            Foreground.StrokeWidth = 1;
        }

        private void OnMouse(libMouseEvent e)
        {
            if (e.Kind == libMouseEventKind.Down && DragArea.Contains(e.InitialPoint))
            {
                dragOffset = e.InitialPoint - DragArea.Location;
                dragOffset.Offset(ButtonSize.Value.ToPoint().Multiply(DragMidPoint).Multiply(-1));
                IsDragging = true;
                e.Consume(this);
            }
            else if (e.Kind == libMouseEventKind.DragScroll && IsDragging)
            {
                UpdatePosition(e.CurrentPoint - dragOffset);
                e.Consume(this);
                Invalidate();
            }
            else if (e.Kind == libMouseEventKind.Up)
            {
                IsDragging = false;
                Invalidate();
            }
            else if (e.Kind == libMouseEventKind.Click && DragArea.Contains(e.InitialPoint))
            {
                e.Consume(this);
            }

        }

        private void Render(SKCanvas ctx)
        {
            DragArea = SKRect.Create(GetPosition(), ButtonSize);
            DragArea.Offset(ButtonSize.Value.ToPoint().Multiply(DragMidPoint).Multiply(-1));
            ctx.DrawRoundRect(DragArea, DragArea.Width * 0.1f, DragArea.Height * 0.1f, Background);

            if (Icon != null)
            {
                var scaled = new SKPath(Icon);
                var t = UtilsUI.GetTransform(IconStore.IconBounds, DragArea.Crop(SKRect.Create(.1f, .1f, .8f, .8f)));
                scaled.Transform(t);
                ctx.DrawPath(scaled, Foreground);
            }
        }
    }
}
