using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class ScrollContainer : FreePanel
    {
        float _scrollY;

        public readonly Val<float> ScrollStep = new(30);
        public float ScrollerSize = 60;
        public readonly Val<float> ScrollPositionHintSize = new(-1);
        readonly Toast ScrollPositionHintToast;
        public Func<string> ScrollPositionHint;
        public readonly Val<float> ScrollerWidth = new(20);
        int? dragOffset;
        public bool IsDragging => dragOffset != null;
        public SKRect ScrollerPos;
        float ScrollerTopOffset;
        public SKPath Icon;
        bool preventChildRender;


        public ScrollContainer()
        {
            Background.Color = SKColors.Beige.WithAlpha(0x20);
            Background.Style = SKPaintStyle.Fill;

            Foreground.Color = SKColors.Beige;
            Foreground.Style = SKPaintStyle.Fill;

            ScrollPositionHintToast = new Toast()
            {
                Link = Attach(() => _scrollerArea == default ? default : SKRect.Create(0, ScrollerPos.Top + (ScrollerPos.Height - ScrollPositionHintSize) / 2, ScrollerPos.Left - 6, ScrollPositionHintSize)),
                Text = () => ScrollPositionHint?.Invoke(), 
                TextAlign = { Const = SKTextAlign.Right }, 
                Padding = { Const = 3 },
            };

            ScrollPositionHintToast.Enabled.Adjust(x => x && !preventChildRender);

            KeyUpHandler = KeyUp;
            MouseHandler = OnMouse;
        }

        public float ScrollOffsetY
        {
            get => _scrollY;
            set
            {
                var oldVal = _scrollY;
                _scrollY = value;
                GetMaxScrollOffsetY();

                if (_scrollY != oldVal)
                {
                    if (ScrollPositionHint != null)
                        ScrollPositionHintToast.Show();

                    Invalidate();
                }
            }
        }

        public int GetMaxScrollOffsetY()
        {
            var res = (int)Math.Max(0, Children.Where(x => x.Enabled && x != ScrollPositionHintToast).Select(x => x.Position.Bottom).DefaultIfEmpty().Max() - Size.Height);
            _scrollY = Math.Clamp(_scrollY, 0, Math.Max(0, res));
            return res;
        }

        void OnMouse(libMouseEvent e)
        {
            if (_scrollerArea == default)
                return;

            if (e.Kind == libMouseEventKind.DragScroll)
            {
                if (ScrollerPos.Contains(e.InitialPoint) && dragOffset == null)
                {
                    dragOffset = (int)(e.InitialPoint.Y - ScrollerTopOffset + RenderPadding.Top);
                    e.Consume(this);
                }
                if (dragOffset != null)
                {
                    var maxOffset = GetMaxScrollOffsetY();
                    ScrollOffsetY = (int)((e.CurrentPoint.Y - dragOffset) / (_scrollerArea.Height - ScrollerSize) * maxOffset);
                    e.Consume(this);
                }
            }
            else if (e.Kind == libMouseEventKind.Up)
            {
                dragOffset = null;
                Invalidate();
            }
            else if (e.Kind == libMouseEventKind.Click && ScrollerPos.Contains(e.InitialPoint))
            {
                e.Consume(this);
            }
            //else if (_scrollerArea.Contains(e.CurrentPoint) && e.Kind == libMouseEventKind.Click)
            //{
            //    ScrollOffsetY = (int)((e.CurrentPoint.Y - _scrollerArea.Top) / _scrollerArea.Height * GetMaxScrollOffsetY());
            //    e.Consume(this);
            //}
            else if (e.Kind == libMouseEventKind.Wheel)
            {
                ScrollOffsetY -= (int)(e.Offset.Y * ScrollStep);
                e.Consume(this);
            }
        }

    
        void KeyUp(libKeyMessage e)
        {
            if (_scrollerArea == default)
                return;

            if (e.Key == libKeys.Up)
                ScrollOffsetY -= ScrollStep;
            else if (e.Key == libKeys.Down)
                ScrollOffsetY += ScrollStep;
            else if (e.Key == libKeys.PageUp)
                ScrollOffsetY -= (int)(OuterRect.Height / ScrollStep * ScrollStep);
            else if (e.Key == libKeys.PageDown)
                ScrollOffsetY += (int)(OuterRect.Height / ScrollStep * ScrollStep);
            else if (e.Key == libKeys.Home)
                ScrollOffsetY = 0;
            else if (e.Key == libKeys.End)
                ScrollOffsetY = int.MaxValue;
            else
                return;

            e.Consume();
        }

        protected internal override void RouteMouseEvent(libMouseEvent e)
        {
            MouseHandler?.Invoke(e);

            var handler = MouseHandler;
            try
            {
                MouseHandler = null;

                var offset = new SKPoint(0, -ScrollOffsetY);
                e.InitialPoint -= offset;
                e.CurrentPoint -= offset;
                base.RouteMouseEvent(e);
                e.InitialPoint += offset;
                e.CurrentPoint += offset;
            }
            finally { MouseHandler = handler; }
        }

        protected internal override void RouteRenderFrame(SKCanvas ctx)
        {
            ctx.Translate(0, -ScrollOffsetY);

            preventChildRender = true; // prevent double drawing
            base.RouteRenderFrame(ctx);
            preventChildRender = false;

            ctx.Translate(0, ScrollOffsetY);
            RenderFrame(ctx);

            if (ScrollPositionHintToast.Enabled)
            {
                ctx.Translate(ScrollPositionHintToast.Position.Location);
                ScrollPositionHintToast.RouteRenderFrame(ctx);
            }
        }

        SKRect _scrollerArea;

        protected override void LayoutChildren()
        {
            base.LayoutChildren();
            var maxOffset = GetMaxScrollOffsetY();
            if (maxOffset == 0) // prevent div by 0, don't draw without scroll
                _scrollerArea = default;
            else
            {

                _scrollerArea = SKRect.Create(Size.Width - ScrollerWidth, RenderPadding.Top, ScrollerWidth, Size.Height - RenderPadding.Top);

                var pct = (float)ScrollOffsetY / maxOffset;
                ScrollerTopOffset = (_scrollerArea.Height - ScrollerSize) * pct + _scrollerArea.Top;
                ScrollerPos = SKRect.Create(_scrollerArea.Left, ScrollerTopOffset, _scrollerArea.Width, ScrollerSize);
            }
        }

        void RenderFrame(SKCanvas ctx)
        {
            if (_scrollerArea == default)
                return;

            ctx.DrawRect(_scrollerArea, Background);

            if (Icon == null)
                ctx.DrawRect(ScrollerPos, Foreground);
            else
            {
                //ctx.DrawRect(ScrollerPos, paint);
                var rOval = ScrollerPos;
                //rOval.Right += rOval.Width;
                rOval.Offset(rOval.Width * 0.4f, 0);
                ctx.DrawOval(rOval, Foreground);

                var scaled = new SKPath(Icon);
                var iconRect = ScrollerPos.Deflate(ScrollerPos.Width * 0.25f);
                iconRect.Offset(ScrollerPos.Width * 0.25f, 0);
                var t = UtilsUI.GetTransform(Icon.Bounds, iconRect);
                scaled.Transform(t);
                using var paint = Foreground.Clone();
                paint.Color = SKColors.Black;
                paint.Style = SKPaintStyle.StrokeAndFill;
                ctx.DrawPath(scaled, paint);
            }
        }
    }
}
