using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{

    public struct PaddingDef
    {
        public float Left,Top, Right, Bottom;

        public readonly float TotalWidth => Right + Left;
        public readonly float TotalHeight => Top + Bottom;

        public PaddingDef(float left = 0, float top = 0, float right = 0, float bottom = 0)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        //public static implicit operator float(PaddingDef v) => v.Top;
        public static implicit operator PaddingDef(float v) => new (v, v, v, v);

        public PaddingDef Expand(float h, float v)
        {
            var r = this;
            r.Left += h;
            r.Top += v;
            r.Right += h;
            r.Bottom += v;
            return r;
        }
    }

    public class BaseView : IDisposable
    {
        //public static SKTypeface DefaultTypeface = SKTypeface.Default;

        public WindowAdapter Window { get; internal set; }
        public Val<PaddingDef> Padding = new(10);
        public readonly SKPaint Foreground;
        public readonly SKPaint Background;

        public static SKSamplingOptions HighQualitySampling = new(SKFilterMode.Linear, SKMipmapMode.Nearest);

        public BaseView()
        {
            Enabled.OnConstSet += (o, n) => Invalidate();
            Foreground = new() { IsAntialias = true };
            Background = new() { IsAntialias = true };
        }

        //protected virtual bool OnLayout() => true;
        public event Action OnAfterLayout;

        Func<SKRect, SKRect> _adjustPos;

        public void AdjustRect(Func<SKRect, SKRect> f)
        {
            if (_adjustPos == null)
                _adjustPos = f;
            else
            {
                var lastFunc = _adjustPos;
                _adjustPos = x => f(lastFunc(x));
            }
        }

        public void Layout(SKRect rect)
        {
            if (_adjustPos != null)
                rect = _adjustPos(rect);

            Window.LayoutChanged |= rect != Position;
            Position = rect;
            RenderPadding = Padding.Value;
            InnerRect = OuterRect.ApplyPadding(RenderPadding);

            //if (OnLayout())
            LayoutChildren();

            OnAfterLayout?.Invoke();
        }

        protected virtual void LayoutChildren()
        {

        }

        //public Func<SKRect> Rect { get;  set; }

        public SKRect Position { get; private set; }
        public PaddingDef RenderPadding { get; private set; }
        public SKRect InnerRect { get; private set; }

        public SKRect OuterRect => SKRect.Create(Position.Size);
        public SKSize Size => Position.Size;

        public readonly Val<bool> Enabled = new(true);

        public IReadOnlyList<BaseView> Children => _children;
        List<BaseView> _children = new();

        BaseView _parent;
        public object LayoutInfo;

        public (BaseView, object) Link { set => AttachToParent(value.Item1, value.Item2); }

        public (BaseView, object) LinkFirst { set => AttachToParent(value.Item1, value.Item2, 0); }

        void AttachToParent(BaseView parent, object info, int index = -1)
        {
            if (_parent != null)
                throw new InvalidOperationException();

            _parent = parent;

            if (index < 0)
                _parent._children.Add(this);
            else
                _parent._children.Insert(index, this);

            LayoutInfo = info;
            Parent.Window?.InitializeInternal(this);
        }

        public BaseView Parent => _parent;

        public IEnumerable<BaseView> Ancestors
        {
            get
            {
                var p = Parent;
                while (p != null)
                {
                    yield return p;
                    p = p.Parent;
                }
            }
        }

        //public void AddChildDynamic(BaseView child, int pos = -1)
        //{
        //    if (child._parent != null)
        //        throw new InvalidOperationException();
        //    child._parent = this;
        //    if (pos < 0)
        //        _children.Add(child);
        //    else
        //        _children.Insert(pos, child);
        //    Window.InitializeInternal(child);
        //}

        public void RemoveChildren(Predicate<BaseView> filter) => _children.RemoveAll(x =>
        {
            var remove = filter(x);
            if (remove)
                x.Dispose();
            return remove;
        });

        public void Invalidate() => Window?.Invalidate();

        public virtual void Init() { }
        public Action<libMouseEvent> MouseHandler;
        public Action<libKeyMessage> KeyUpHandler;
        public Action<SKCanvas> RenderFrameHandler;

        protected internal virtual void RouteKeyEvent(libKeyMessage e)
        {
            for (int i = Children.Count - 1; i >= 0; i--)
                if (Children[i].EnabledForEvents && !e.Consumed)
                    Children[i].RouteKeyEvent(e);

            if (!e.Consumed)
                KeyUpHandler?.Invoke(e);
        }

        protected internal virtual void RouteRenderFrame(SKCanvas ctx)
        {
            if (_disposed)
                throw new Exception("disposed");

            RenderFrameHandler?.Invoke(ctx);

            foreach (var item in Children)
            {
                if (!item.EnabledForEvents)
                    continue;

                var rect = item.Position;
                //if (rect.Width < 0 || rect.Height < 0)
                //    throw new InvalidOperationException("negative size");

                using var restore = new SKAutoCanvasRestore(ctx);

                if (!ctx.TotalMatrix.MapRect(rect).IntersectsWith(ctx.DeviceClipBounds))
                    continue;

                ctx.ClipRect(rect.Deflate(-1));
                ctx.Translate(rect.Location);

                item.RouteRenderFrame(ctx);

                //ctx.DrawRect(item.OuterRect, new SKPaint() { Color = SKColors.Pink.WithAlpha(0x30) });
               
            }

            if (Window.ShowGrid)
            {
                var gridRect = OuterRect;
                using var pt = new SKPaint() { IsStroke = true, StrokeWidth = 1, Color = SKColors.Lime };
                ctx.DrawRect(gridRect, pt);

                var offset = (4 * Ancestors.Count());
                var bottomLeft = new SKPoint(1, gridRect.Bottom - 1);
                var textPos = bottomLeft + new SKPoint(offset, -offset);

                using var pt3 = new SKPaint() { Color = SKColors.Red, StrokeWidth = 2 };
                ctx.DrawLine(bottomLeft, textPos, pt3);
                using var pt2 = new SKPaint() { Color = SKColors.Fuchsia };
                ctx.DrawText(GetType().Name /*+ (Desc == null ? null : $" {Desc}")*/, textPos, SKTextAlign.Left, DebugFont, pt2);
            }
        }

        static SKFont DebugFont = new() { Size = 11f };

        private bool EnabledForEvents => Enabled && Position.Width > 0 && Position.Height > 0;

        protected internal virtual void RouteMouseEvent(libMouseEvent e)
        {
            if (e.Consumed)
                return;

            for (int i = Children.Count - 1; i >= 0 ; i--)
            {
                var item = Children[i];
                if (!item.EnabledForEvents)
                    continue;
                
                //if (e.InitialEvent?.Consumer != null && !e.InitialEvent.Consumer.Ancestors.Contains(item) && e.InitialEvent.Consumer != item) // route to the same view, if already consumed
                //    continue;

                var rect = item.Position;
                if (float.IsNaN(rect.Top) || float.IsNaN(rect.Right) || float.IsNaN(rect.Left) || float.IsNaN(rect.Bottom))
                    throw new Exception("nan");

                var offset = rect.Location;

                e.InitialPoint -= offset;
                e.CurrentPoint -= offset;
                
                /*|| e.InitialEvent?.Consumer != null*/
                if (rect.Size.Contains(e.InitialPoint) || e.Kind == libMouseEventKind.FreeMove && rect.Size.Contains(e.CurrentPoint))
                    item.RouteMouseEvent(e);

                e.InitialPoint += offset;
                e.CurrentPoint += offset;

                if (e.Consumed)
                    return;
            }

            MouseHandler?.Invoke(e);
        }

        public SKRect CenterRect(SKSize size, float x = -1, float y = -1)
        {
            var s = Size;
            if (size.Width == 0)
                size.Width = s.Width;
            if (size.Height == 0)
                size.Height = s.Height;

            return SKRect.Create(x >= 0 ? x : (s.Width - size.Width) / 2, y >= 0 ? y : (s.Height - size.Height) / 2, size.Width, size.Height);
        }

        protected bool IsMouseOver(SKCanvas ctx)
        {
            var pos = Window.MousePosition?.Invoke();

            return pos.HasValue ? ctx.TotalMatrix.MapRect(OuterRect).Contains(pos.Value) : false;
        }

        public void DetectClick(libMouseEvent e, Action onClick, Action onAltClick = null)
        {
            if (Window.ManualClickDetection)
            {
                e.Consume(this);
                if (OuterRect.Contains(e.CurrentPoint))
                {
                    if (e.Kind == libMouseEventKind.Up)
                    {
                        Invalidate();
                        onClick?.Invoke();
                    }
                    else if (e.Kind == libMouseEventKind.AltUp)
                    {
                        Invalidate();
                        onAltClick?.Invoke();
                    }
                }
            }
            else if (e.Kind == libMouseEventKind.Click)
            {
                e.Consume(this);
                Invalidate();
                onClick?.Invoke();
            }
            else if (e.Kind == libMouseEventKind.AltClick)
            {
                e.Consume(this);
                Invalidate();
                onAltClick?.Invoke();
            }
            //else if (e.Kind == libMouseEventKind.DragScroll)
            //    e.Rejected = OuterRect.Contains(e.CurrentPoint);
        }

        bool _disposed;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                OnDispose?.Invoke();
                Background?.Dispose();
                Foreground?.Dispose();

                foreach (var item in Children)
                    item.Dispose();
            }
        }

        protected event Action OnDispose;
    }

}
