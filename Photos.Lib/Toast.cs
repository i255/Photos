using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class Toast: TextLabel
    {
        float FadeTimeSec = 3;
        public float BackToFrontRatio = 0.6f;
        Animation _animation;
        float alpha;
        public Toast()
        {
            TextAlign.Const = SKTextAlign.Center;
            Padding.Const = 3;
            Enabled.Func = () => alpha > 0;
            BorderRadius = 5;

            _animation = new(this, () =>
            {
                alpha -= 255 / FadeTimeSec * _animation.TimeDeltaSec;
                if (alpha <= 0)
                    _animation.Stop();
                else
                    UpdatePaint();
            });
        }

        private void UpdatePaint()
        {
            Foreground.Color = Foreground.Color.WithAlpha((byte)alpha);
            Background.Color = Background.Color.WithAlpha((byte)(alpha * BackToFrontRatio));
        }

        public void Show(float fadeTimeSec = 3)
        {
            FadeTimeSec = fadeTimeSec;
            alpha = 255;
            if (fadeTimeSec != float.PositiveInfinity)
                _animation.Start();
            else
                UpdatePaint();
        }

        public void Show(string s, float fadeTimeSec = 3)
        {
            Text = () => s;
            Show(fadeTimeSec);
        }
    }
}
