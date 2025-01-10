using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Data.SqlTypes;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class Button : FreePanel
    {
        public readonly Val<SKPath> Icon = new(default(SKPath));
        public Val<string> Text = new Val<string>(string.Empty);
        public Val<bool> IsHighlighted = new Val<bool>(false);
        public readonly SKPaint Highlighted;
        public float CornerRadius = 3;

        public readonly TextLabel TextLabel;

        public Action OnClick;

        public Button()
        {
            Foreground.Color = SKColors.White;
            Foreground.Style = SKPaintStyle.StrokeAndFill;
            Foreground.StrokeWidth = 1;
            Background.Color = SKColors.Beige.WithAlpha(0x35);
            Background.Style = SKPaintStyle.Fill;
            Highlighted = new SKPaint() { Color = SKColors.Orange.WithAlpha(0x70), Style = SKPaintStyle.Fill, IsAntialias = true };
            OnDispose += () => Highlighted?.Dispose();

            Padding.Const = 8;

            TextLabel = new TextLabel()
            {
                Link = Attach(() =>
                {
                    var res = InnerRect;
                    if (Icon.Value != null)
                    {
                        var align = TextLabel.TextAlign.Value;
                        if (align == SKTextAlign.Right)
                            res = res.OffsetEdge(right: -OuterRect.Height);
                        else
                            res = res.OffsetEdge(left: OuterRect.Height);
                    }

                    if (res.Width < 0)
                        res.Right = res.Left;

                    return res;
                }),
                Text = () => Text,
                Font = { Size = 18 },
                TextAlign = { Const = SKTextAlign.Left },
                DrawBorder = false,
                Padding = { Const = 0 },
            };

            MouseHandler = OnMouse;
            RenderFrameHandler = RenderFrame;
        }

        void OnMouse(libMouseEvent e)
        {
            DetectClick(e, OnClick);

            if ((e.Kind == libMouseEventKind.FreeMove || e.Kind == libMouseEventKind.DragScroll) && OuterRect.Contains(e.CurrentPoint) != OuterRect.Contains(e.InitialPoint))
                Invalidate();
        }

        public float MeasureWidth() => TextLabel.MeasureText() + RenderPadding.TotalWidth + (Icon.Value == null ? 0 : Size.Height) + 1;

        void RenderFrame(SKCanvas ctx)
        {
            var color = Foreground.Color;
            var rect = OuterRect;
            if (IsMouseOver(ctx))
                color = SKColors.Orange;

            TextLabel.Foreground.Color = color;
            //CornerRadius = (int)rect.Width/ 2;
            var roundRect = new SKRoundRect(rect, CornerRadius);
            ctx.DrawRoundRect(roundRect, IsHighlighted ? Highlighted : Background);

            if (Icon.Value != null)
            {
                var iconRect = InnerRect;
                if (Text != null && iconRect.Width > iconRect.Height)
                {
                    if (TextLabel.TextAlign == SKTextAlign.Right)
                        iconRect.Left =  iconRect.Right - iconRect.Height;
                    else
                        iconRect.Right = iconRect.Left + iconRect.Height;
                }

                var scaled = new SKPath(Icon);
                var t = UtilsUI.GetTransform(IconStore.IconBounds, iconRect);
                scaled.Transform(t);
                var savedColor = Foreground.Color;
                Foreground.Color = color;
                ctx.DrawPath(scaled, Foreground);
                Foreground.Color = savedColor;
            }

        }

    }
}
