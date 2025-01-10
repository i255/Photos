using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class TextLabel : BaseView
    {
        public Func<string> Text;
        public bool DrawBorder = true;

        public readonly Val<float> TextScale = new(1);
        public readonly Val<SKTextAlign> TextAlign = new(SKTextAlign.Left);
        public float BorderRadius;
        public bool ScaleToMiddle;

        public readonly SKFont Font;

        public TextLabel()
        {
            RenderFrameHandler = RenderFrame;
            Background.Color = SKColors.Black.WithAlpha(0xA0);
            Background.Style = SKPaintStyle.Fill;
            Padding.Const = 0;
            Foreground.Color = SKColors.White;
            Font = new SKFont { Size = 22 };
            OnDispose += () => Font?.Dispose();
        }

        float fontTotalSizeRatio, fontDescentRatio;

        void RenderFrame(SKCanvas ctx)
        {
            if (fontTotalSizeRatio == 0) // no layout!?!?
                return;

            var text = Text?.Invoke();
            if (text == null)
                return;

            var outerRect = OuterRect;
            var innerRect = InnerRect;

            var chars = Font.BreakText(text, innerRect.Width, Foreground);
            if (chars < text.Length)
                text = string.Concat(text.AsSpan(0, Math.Max(chars - 3, 1)), "...");

            var textWOverf = outerRect.Width - Font.MeasureText(text) - RenderPadding.TotalWidth;

            var align = TextAlign.Value;

            switch (align)
            {
                case SKTextAlign.Left:
                    outerRect.Right -= textWOverf;
                    break;
                case SKTextAlign.Center:
                    outerRect.Right -= textWOverf / 2;
                    outerRect.Left += textWOverf / 2;
                    break;
                case SKTextAlign.Right:
                    outerRect.Left += textWOverf;
                    break;
                default:
                    throw new NotImplementedException();
            }

            if (DrawBorder)
            {
                if (BorderRadius > 0)
                    ctx.DrawRoundRect(new SKRoundRect(outerRect, BorderRadius), Background);
                else
                    ctx.DrawRect(outerRect, Background);
            }

            var textBottom = outerRect.Bottom - (ScaleToMiddle ? (1 - TextScale) / 2 * outerRect.Height : 0) - Font.Size * fontDescentRatio;

            ctx.DrawText(text, align switch
            {
                SKTextAlign.Left => innerRect.Left,
                SKTextAlign.Right => innerRect.Right,
                SKTextAlign.Center => innerRect.MidX,
                _ => throw new NotImplementedException()
            }, textBottom, align, Font, Foreground);
        }

        protected override void LayoutChildren()
        {
            UpdateTextSize(InnerRect.Height);
        }

        public float MeasureText() => Font.MeasureText(Text?.Invoke()) + RenderPadding.TotalWidth + 1;

        private void UpdateTextSize(float height)
        {
            if (fontTotalSizeRatio == 0)
            {
                Font.Size = 100;
                Font.GetFontMetrics(out var metrics);
                fontTotalSizeRatio = (metrics.Descent - metrics.Ascent) / 100;
                fontDescentRatio = metrics.Descent / 100;
            }

            Font.Size = height / fontTotalSizeRatio * TextScale;
        }
    }
}
