using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public static class UtilsUI
    {

        public static SKPoint Multiply(this SKPoint p, float v) => new SKPoint(p.X * v, p.Y * v);
        public static SKPoint Multiply(this SKPoint p, SKPoint v) => new SKPoint(p.X * v.X, p.Y * v.Y);

        public static SKRect Deflate(this SKRect r, float margin)
        {
            r.Inflate(-margin, -margin);
            return r;
        }

        public static SKRect ApplyPadding(this SKRect r, PaddingDef margin)
        {
            r.Left += margin.Left;
            r.Top += margin.Top;
            r.Right -= margin.Right;
            r.Bottom -= margin.Bottom;

            return r;
        }

        public static SKRect OffsetEdge(this SKRect r, float top = 0, float right = 0, float bottom = 0 , float left = 0)
        {
            r.Top += top;
            r.Right += right;
            r.Bottom += bottom;
            r.Left += left;
            return r;
        }

        public static SKRect OffsetRect(this SKRect r, float x = 0, float y = 0)
        {
            r.Top += y;
            r.Right += x;
            r.Bottom += y;
            r.Left += x;
            return r;
        }

        public static SKRect PositionInsideRect(this SKSize s, SKRect r, bool right = false, bool bottom = false)
        {
            var res = SKRect.Create(r.Left, r.Top, s.Width, s.Height);
            res.Offset(right ? r.Width - s.Width : 0, bottom ? r.Height - s.Height : 0);
            return res;
        }

        public static SKRect Crop(this SKRect rect, SKRect a)
        {
            var r = new SKRect(rect.Width * a.Left, rect.Height * a.Top, rect.Width * a.Right, rect.Height * a.Bottom);
            r.Offset(rect.Left, rect.Top);
            return r;
        }

        public static SKRect Select(this SKRect r, Func<SKRect, SKRect> f)
        {
            return f(r);
        }

        public static bool Contains(this SKSize s, SKPoint p)
        {
            return SKRect.Create(s).Contains(p);
        }

        public static SKMatrix GetTransform(SKRect src, SKRect dest, SKSize maxBounds = default)
        {
            var scaleX = src.Width / dest.Width;
            var scaleY = src.Height / dest.Height;
            var scale = Math.Max(scaleX, scaleY);
            if (maxBounds != default)
            {
                var maxScale = Math.Max(src.Width / maxBounds.Width, src.Height / maxBounds.Height);
                scale = Math.Max(maxScale, scale);
            }
            var size = new SKSize(src.Width / scale, src.Height / scale);
            return SKMatrix.CreateScaleTranslation(1 / scale, 1 / scale,
                (dest.Width - size.Width) / 2 + (dest.Left - src.Left / scale),
                (dest.Height - size.Height) / 2 + (dest.Top - src.Top / scale));
        }

        public static IEnumerable<T> Traverse<T>(this IEnumerable<T> source, Func<T, IEnumerable<T>> fnRecurse)
        {
            foreach (T item in source)
            {
                yield return item;

                var seqRecurse = fnRecurse(item);
                if (seqRecurse != null)
                    foreach (T itemRecurse in Traverse(seqRecurse, fnRecurse))
                        yield return itemRecurse;
            }
        }
        public static IEnumerable<T> Yield<T>(this T item)
        {
            yield return item;
        }
    }


    public static class IconStore
    {
        public static SKPath InfoCircle = SKPath.ParseSvgPathData("M8 15A7 7 0 1 1 8 1a7 7 0 0 1 0 14zm0 1A8 8 0 1 0 8 0a8 8 0 0 0 0 16z");
        public static SKPath ThreeDotsVertical = SKPath.ParseSvgPathData("M9.5 13a1.5 1.5 0 1 1-3 0 1.5 1.5 0 0 1 3 0zm0-5a1.5 1.5 0 1 1-3 0 1.5 1.5 0 0 1 3 0zm0-5a1.5 1.5 0 1 1-3 0 1.5 1.5 0 0 1 3 0z");
        public static SKPath ArrowLeft = SKPath.ParseSvgPathData("M15 8a.5.5 0 0 0-.5-.5H2.707l3.147-3.146a.5.5 0 1 0-.708-.708l-4 4a.5.5 0 0 0 0 .708l4 4a.5.5 0 0 0 .708-.708L2.707 8.5H14.5A.5.5 0 0 0 15 8z");

        public static SKPath ArrowBarDown = SKPath.ParseSvgPathData("M1 3.5a.5.5 0 0 1 .5-.5h13a.5.5 0 0 1 0 1h-13a.5.5 0 0 1-.5-.5zM8 6a.5.5 0 0 1 .5.5v5.793l2.146-2.147a.5.5 0 0 1 .708.708l-3 3a.5.5 0 0 1-.708 0l-3-3a.5.5 0 0 1 .708-.708L7.5 12.293V6.5A.5.5 0 0 1 8 6z");
        public static SKPath ArrowBarUp = SKPath.ParseSvgPathData("M8 10a.5.5 0 0 0 .5-.5V3.707l2.146 2.147a.5.5 0 0 0 .708-.708l-3-3a.5.5 0 0 0-.708 0l-3 3a.5.5 0 1 0 .708.708L7.5 3.707V9.5a.5.5 0 0 0 .5.5zm-7 2.5a.5.5 0 0 1 .5-.5h13a.5.5 0 0 1 0 1h-13a.5.5 0 0 1-.5-.5z");

        public static SKPath ArrowsMove = SKPath.ParseSvgPathData("M7.646.146a.5.5 0 0 1 .708 0l2 2a.5.5 0 0 1-.708.708L8.5 1.707V5.5a.5.5 0 0 1-1 0V1.707L6.354 2.854a.5.5 0 1 1-.708-.708l2-2zM8 10a.5.5 0 0 1 .5.5v3.793l1.146-1.147a.5.5 0 0 1 .708.708l-2 2a.5.5 0 0 1-.708 0l-2-2a.5.5 0 0 1 .708-.708L7.5 14.293V10.5A.5.5 0 0 1 8 10zM.146 8.354a.5.5 0 0 1 0-.708l2-2a.5.5 0 1 1 .708.708L1.707 7.5H5.5a.5.5 0 0 1 0 1H1.707l1.147 1.146a.5.5 0 0 1-.708.708l-2-2zM10 8a.5.5 0 0 1 .5-.5h3.793l-1.147-1.146a.5.5 0 0 1 .708-.708l2 2a.5.5 0 0 1 0 .708l-2 2a.5.5 0 0 1-.708-.708L14.293 8.5H10.5A.5.5 0 0 1 10 8z");

        public static SKPath MyArrowsAngleRight = SKPath.ParseSvgPathData("M 7 9 a 0.5 0.5 0 0 0 -0.707 0 l -4.096 4.096 v -2.768 a 0.5 0.5 0 0 0 -1 0 v 3.975 a 0.5 0.5 0 0 0 0.5 0.5 h 3.975 a 0.5 0.5 0 0 0 0 -1 h -2.768 l 4.096 -4.096 a 0.5 0.5 0 0 0 0 -0.707 z M 9 7 a 0.5 0.5 0 0 0 0.707 0 l 4.096 -4.096 v 2.768 a 0.5 0.5 0 1 0 1 0 v -3.975 a 0.5 0.5 0 0 0 -0.5 -0.5 h -3.975 a 0.5 0.5 0 0 0 0 1 h 2.768 l -4.096 4.096 a 0.5 0.5 0 0 0 0 0.707 z");

        public static SKPath MyArrowsAngleLeft = SKPath.ParseSvgPathData("M 7 7 a 0.5 0.5 90 0 0 -0 -0.707 l -4.096 -4.096 h 2.768 a 0.5 0.5 90 0 0 -0 -1 h -3.975 a 0.5 0.5 90 0 0 -0.5 0.5 v 3.975 a 0.5 0.5 90 0 0 1 -0 v -2.768 l 4.096 4.096 a 0.5 0.5 90 0 0 0.707 -0 z M 9 9 a 0.5 0.5 90 0 0 0 0.707 l 4.096 4.096 h -2.768 a 0.5 0.5 90 1 0 0 1 h 3.975 a 0.5 0.5 90 0 0 0.5 -0.5 v -3.975 a 0.5 0.5 90 0 0 -1 0 v 2.768 l -4.096 -4.096 a 0.5 0.5 90 0 0 -0.707 0 z");

        public static SKPath Copy = SKPath.ParseSvgPathData("M4 2a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2zm2-1a1 1 0 0 0-1 1v8a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1V2a1 1 0 0 0-1-1zM2 5a1 1 0 0 0-1 1v8a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1v-1h1v1a2 2 0 0 1-2 2H2a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h1v1z");
        public static SKPath CaretDown = SKPath.ParseSvgPathData("M3.204 5h9.592L8 10.481 3.204 5zm-.753.659 4.796 5.48a1 1 0 0 0 1.506 0l4.796-5.48c.566-.647.106-1.659-.753-1.659H3.204a1 1 0 0 0-.753 1.659z");
        public static SKPath CaretUp = SKPath.ParseSvgPathData("M3.204 11h9.592L8 5.519 3.204 11zm-.753-.659 4.796-5.48a1 1 0 0 1 1.506 0l4.796 5.48c.566.647.106 1.659-.753 1.659H3.204a1 1 0 0 1-.753-1.659z");

        public static SKPath HourglassSplit = SKPath.ParseSvgPathData("M2.5 15a.5.5 0 1 1 0-1h1v-1a4.5 4.5 0 0 1 2.557-4.06c.29-.139.443-.377.443-.59v-.7c0-.213-.154-.451-.443-.59A4.5 4.5 0 0 1 3.5 3V2h-1a.5.5 0 0 1 0-1h11a.5.5 0 0 1 0 1h-1v1a4.5 4.5 0 0 1-2.557 4.06c-.29.139-.443.377-.443.59v.7c0 .213.154.451.443.59A4.5 4.5 0 0 1 12.5 13v1h1a.5.5 0 0 1 0 1h-11zm2-13v1c0 .537.12 1.045.337 1.5h6.326c.216-.455.337-.963.337-1.5V2h-7zm3 6.35c0 .701-.478 1.236-1.011 1.492A3.5 3.5 0 0 0 4.5 13s.866-1.299 3-1.48V8.35zm1 0v3.17c2.134.181 3 1.48 3 1.48a3.5 3.5 0 0 0-1.989-3.158C8.978 9.586 8.5 9.052 8.5 8.351z");
        public static SKPath Check = SKPath.ParseSvgPathData("M10.97 4.97a.75.75 0 0 1 1.07 1.05l-3.99 4.99a.75.75 0 0 1-1.08.02L4.324 8.384a.75.75 0 1 1 1.06-1.06l2.094 2.093 3.473-4.425a.267.267 0 0 1 .02-.022z");
        public static SKPath X = SKPath.ParseSvgPathData("M4.646 4.646a.5.5 0 0 1 .708 0L8 7.293l2.646-2.647a.5.5 0 0 1 .708.708L8.707 8l2.647 2.646a.5.5 0 0 1-.708.708L8 8.707l-2.646 2.647a.5.5 0 0 1-.708-.708L7.293 8 4.646 5.354a.5.5 0 0 1 0-.708z");
        public static SKPath Dash = SKPath.ParseSvgPathData("M4 8a.5.5 0 0 1 .5-.5h7a.5.5 0 0 1 0 1h-7A.5.5 0 0 1 4 8z");
        public static SKPath MyArrowDownLeft = SKPath.ParseSvgPathData("M 10.096 5.146 A 0.5 0.5 0 1 1 10.803 5.854 L 6.707 9.95 H 9.475 A 0.5 0.5 0 1 1 9.475 10.95 H 5.5 A 0.5 0.5 0 0 1 5 10.45 V 6.475 A 0.5 0.5 0 1 1 6 6.475 V 9.243 L 10.096 5.146 Z");
        public static SKPath List = SKPath.ParseSvgPathData("M2.5 12a.5.5 0 0 1 .5-.5h10a.5.5 0 0 1 0 1H3a.5.5 0 0 1-.5-.5zm0-4a.5.5 0 0 1 .5-.5h10a.5.5 0 0 1 0 1H3a.5.5 0 0 1-.5-.5zm0-4a.5.5 0 0 1 .5-.5h10a.5.5 0 0 1 0 1H3a.5.5 0 0 1-.5-.5z");
        public static SKPath Share = SKPath.ParseSvgPathData("M13.5 1a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3zM11 2.5a2.5 2.5 0 1 1 .603 1.628l-6.718 3.12a2.499 2.499 0 0 1 0 1.504l6.718 3.12a2.5 2.5 0 1 1-.488.876l-6.718-3.12a2.5 2.5 0 1 1 0-3.256l6.718-3.12A2.5 2.5 0 0 1 11 2.5zm-8.5 4a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3zm11 5.5a1.5 1.5 0 1 0 0 3 1.5 1.5 0 0 0 0-3z");
        public static SKPath Funnel = SKPath.ParseSvgPathData("M1.5 1.5A.5.5 0 0 1 2 1h12a.5.5 0 0 1 .5.5v2a.5.5 0 0 1-.128.334L10 8.692V13.5a.5.5 0 0 1-.342.474l-3 1A.5.5 0 0 1 6 14.5V8.692L1.628 3.834A.5.5 0 0 1 1.5 3.5v-2zm1 .5v1.308l4.372 4.858A.5.5 0 0 1 7 8.5v5.306l2-.666V8.5a.5.5 0 0 1 .128-.334L13.5 3.308V2h-11z");
        public static SKPath SortUp = SKPath.ParseSvgPathData("M3.5 12.5a.5.5 0 0 1-1 0V3.707L1.354 4.854a.5.5 0 1 1-.708-.708l2-1.999.007-.007a.498.498 0 0 1 .7.006l2 2a.5.5 0 1 1-.707.708L3.5 3.707V12.5zm3.5-9a.5.5 0 0 1 .5-.5h7a.5.5 0 0 1 0 1h-7a.5.5 0 0 1-.5-.5zM7.5 6a.5.5 0 0 0 0 1h5a.5.5 0 0 0 0-1h-5zm0 3a.5.5 0 0 0 0 1h3a.5.5 0 0 0 0-1h-3zm0 3a.5.5 0 0 0 0 1h1a.5.5 0 0 0 0-1h-1z");
        public static SKPath Folder = SKPath.ParseSvgPathData("M.54 3.87.5 3a2 2 0 0 1 2-2h3.672a2 2 0 0 1 1.414.586l.828.828A2 2 0 0 0 9.828 3h3.982a2 2 0 0 1 1.992 2.181l-.637 7A2 2 0 0 1 13.174 14H2.826a2 2 0 0 1-1.991-1.819l-.637-7a1.99 1.99 0 0 1 .342-1.31zM2.19 4a1 1 0 0 0-.996 1.09l.637 7a1 1 0 0 0 .995.91h10.348a1 1 0 0 0 .995-.91l.637-7A1 1 0 0 0 13.81 4H2.19zm4.69-1.707A1 1 0 0 0 6.172 2H2.5a1 1 0 0 0-1 .981l.006.139C1.72 3.042 1.95 3 2.19 3h5.396l-.707-.707z");
        public static SKPath ArrowsScroll = SKPath.ParseSvgPathData("m 3 1 a 0.5 0.5 0 0 1 0.708 0 l 2 2 a 0.5 0.5 0 0 1 -0.708 0.708 l -1.146 -1.147 h -1 l -1.146 1.147 a 0.5 0.5 0 1 1 -0.708 -0.708 l 2 -2 z m 0.854 5.854 l 1.146 -1.147 a 0.5 0.5 0 0 1 0.708 0.708 l -2 2 a 0.5 0.5 0 0 1 -0.708 0 l -2 -2 a 0.5 0.5 0 0 1 0.708 -0.708 l 1.146 1.147 z");
        public static SKPath MenuButtonWide, EmojiDizzy, Gear, Eye, ArrowClockwise;

        public static SKPath Trash3 = SKPath.ParseSvgPathData("M6.5 1h3a.5.5 0 0 1 .5.5v1H6v-1a.5.5 0 0 1 .5-.5ZM11 2.5v-1A1.5 1.5 0 0 0 9.5 0h-3A1.5 1.5 0 0 0 5 1.5v1H2.506a.58.58 0 0 0-.01 0H1.5a.5.5 0 0 0 0 1h.538l.853 10.66A2 2 0 0 0 4.885 16h6.23a2 2 0 0 0 1.994-1.84l.853-10.66h.538a.5.5 0 0 0 0-1h-.995a.59.59 0 0 0-.01 0H11Zm1.958 1-.846 10.58a1 1 0 0 1-.997.92h-6.23a1 1 0 0 1-.997-.92L3.042 3.5h9.916Zm-7.487 1a.5.5 0 0 1 .528.47l.5 8.5a.5.5 0 0 1-.998.06L5 5.03a.5.5 0 0 1 .47-.53Zm5.058 0a.5.5 0 0 1 .47.53l-.5 8.5a.5.5 0 1 1-.998-.06l.5-8.5a.5.5 0 0 1 .528-.47ZM8 4.5a.5.5 0 0 1 .5.5v8.5a.5.5 0 0 1-1 0V5a.5.5 0 0 1 .5-.5Z");

        public static SKPath Star = SKPath.ParseSvgPathData("M2.866 14.85c-.078.444.36.791.746.593l4.39-2.256 4.389 2.256c.386.198.824-.149.746-.592l-.83-4.73 3.522-3.356c.33-.314.16-.888-.282-.95l-4.898-.696L8.465.792a.513.513 0 0 0-.927 0L5.354 5.12l-4.898.696c-.441.062-.612.636-.283.95l3.523 3.356-.83 4.73zm4.905-2.767-3.686 1.894.694-3.957a.565.565 0 0 0-.163-.505L1.71 6.745l4.052-.576a.525.525 0 0 0 .393-.288L8 2.223l1.847 3.658a.525.525 0 0 0 .393.288l4.052.575-2.906 2.77a.565.565 0 0 0-.163.506l.694 3.957-3.686-1.894a.503.503 0 0 0-.461 0z");

        public static SKPath StarFilled = SKPath.ParseSvgPathData("M3.612 15.443c-.386.198-.824-.149-.746-.592l.83-4.73L.173 6.765c-.329-.314-.158-.888.283-.95l4.898-.696L7.538.792c.197-.39.73-.39.927 0l2.184 4.327 4.898.696c.441.062.612.636.282.95l-3.522 3.356.83 4.73c.078.443-.36.79-.746.592L8 13.187l-4.389 2.256z");

        public static SKPath Crop = SKPath.ParseSvgPathData("M 3.15 0.45 A 0.45 0.45 90 0 1 3.6 0.9 V 12.6 H 15.3 A 0.45 0.45 90 0 1 15.3 13.5 H 13.5 V 15.3 A 0.45 0.45 90 0 1 12.6 15.3 V 13.5 H 3.15 A 0.45 0.45 90 0 1 2.7 13.05 V 3.6 H 0.9 A 0.45 0.45 90 0 1 0.9 2.7 H 2.7 V 0.9 A 0.45 0.45 90 0 1 3.15 0.45 Z M 5.4 3.15 A 0.45 0.45 90 0 1 5.85 2.7 H 13.05 A 0.45 0.45 90 0 1 13.5 3.15 V 10.35 A 0.45 0.45 90 0 1 12.6 10.35 V 3.6 H 5.85 A 0.45 0.45 90 0 1 5.4 3.15 Z");

        internal static SKRect IconBounds = SKRect.Create(16, 16);

        static IconStore()
        {
            MenuButtonWide = SKPath.ParseSvgPathData("M0 1.5A1.5 1.5 0 0 1 1.5 0h13A1.5 1.5 0 0 1 16 1.5v2A1.5 1.5 0 0 1 14.5 5h-13A1.5 1.5 0 0 1 0 3.5v-2zM1.5 1a.5.5 0 0 0-.5.5v2a.5.5 0 0 0 .5.5h13a.5.5 0 0 0 .5-.5v-2a.5.5 0 0 0-.5-.5h-13z");
            MenuButtonWide.AddPath(SKPath.ParseSvgPathData("M2 2.5a.5.5 0 0 1 .5-.5h3a.5.5 0 0 1 0 1h-3a.5.5 0 0 1-.5-.5zm10.823.323-.396-.396A.25.25 0 0 1 12.604 2h.792a.25.25 0 0 1 .177.427l-.396.396a.25.25 0 0 1-.354 0zM0 8a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2H2a2 2 0 0 1-2-2V8zm1 3v2a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1v-2H1zm14-1V8a1 1 0 0 0-1-1H2a1 1 0 0 0-1 1v2h14zM2 8.5a.5.5 0 0 1 .5-.5h9a.5.5 0 0 1 0 1h-9a.5.5 0 0 1-.5-.5zm0 4a.5.5 0 0 1 .5-.5h6a.5.5 0 0 1 0 1h-6a.5.5 0 0 1-.5-.5z"));

            EmojiDizzy = SKPath.ParseSvgPathData("M8 15A7 7 0 1 1 8 1a7 7 0 0 1 0 14zm0 1A8 8 0 1 0 8 0a8 8 0 0 0 0 16z");
            EmojiDizzy.AddPath(SKPath.ParseSvgPathData("M9.146 5.146a.5.5 0 0 1 .708 0l.646.647.646-.647a.5.5 0 0 1 .708.708l-.647.646.647.646a.5.5 0 0 1-.708.708l-.646-.647-.646.647a.5.5 0 1 1-.708-.708l.647-.646-.647-.646a.5.5 0 0 1 0-.708zm-5 0a.5.5 0 0 1 .708 0l.646.647.646-.647a.5.5 0 1 1 .708.708l-.647.646.647.646a.5.5 0 1 1-.708.708L5.5 7.207l-.646.647a.5.5 0 1 1-.708-.708l.647-.646-.647-.646a.5.5 0 0 1 0-.708zM10 11a2 2 0 1 1-4 0 2 2 0 0 1 4 0z"));

            Gear = SKPath.ParseSvgPathData("M8 4.754a3.246 3.246 0 1 0 0 6.492 3.246 3.246 0 0 0 0-6.492zM5.754 8a2.246 2.246 0 1 1 4.492 0 2.246 2.246 0 0 1-4.492 0z");
            Gear.AddPath(SKPath.ParseSvgPathData("M9.796 1.343c-.527-1.79-3.065-1.79-3.592 0l-.094.319a.873.873 0 0 1-1.255.52l-.292-.16c-1.64-.892-3.433.902-2.54 2.541l.159.292a.873.873 0 0 1-.52 1.255l-.319.094c-1.79.527-1.79 3.065 0 3.592l.319.094a.873.873 0 0 1 .52 1.255l-.16.292c-.892 1.64.901 3.434 2.541 2.54l.292-.159a.873.873 0 0 1 1.255.52l.094.319c.527 1.79 3.065 1.79 3.592 0l.094-.319a.873.873 0 0 1 1.255-.52l.292.16c1.64.893 3.434-.902 2.54-2.541l-.159-.292a.873.873 0 0 1 .52-1.255l.319-.094c1.79-.527 1.79-3.065 0-3.592l-.319-.094a.873.873 0 0 1-.52-1.255l.16-.292c.893-1.64-.902-3.433-2.541-2.54l-.292.159a.873.873 0 0 1-1.255-.52l-.094-.319zm-2.633.283c.246-.835 1.428-.835 1.674 0l.094.319a1.873 1.873 0 0 0 2.693 1.115l.291-.16c.764-.415 1.6.42 1.184 1.185l-.159.292a1.873 1.873 0 0 0 1.116 2.692l.318.094c.835.246.835 1.428 0 1.674l-.319.094a1.873 1.873 0 0 0-1.115 2.693l.16.291c.415.764-.42 1.6-1.185 1.184l-.291-.159a1.873 1.873 0 0 0-2.693 1.116l-.094.318c-.246.835-1.428.835-1.674 0l-.094-.319a1.873 1.873 0 0 0-2.692-1.115l-.292.16c-.764.415-1.6-.42-1.184-1.185l.159-.291A1.873 1.873 0 0 0 1.945 8.93l-.319-.094c-.835-.246-.835-1.428 0-1.674l.319-.094A1.873 1.873 0 0 0 3.06 4.377l-.16-.292c-.415-.764.42-1.6 1.185-1.184l.292.159a1.873 1.873 0 0 0 2.692-1.115l.094-.319z"));

            Eye = SKPath.ParseSvgPathData("M16 8s-3-5.5-8-5.5S0 8 0 8s3 5.5 8 5.5S16 8 16 8zM1.173 8a13.133 13.133 0 0 1 1.66-2.043C4.12 4.668 5.88 3.5 8 3.5c2.12 0 3.879 1.168 5.168 2.457A13.133 13.133 0 0 1 14.828 8c-.058.087-.122.183-.195.288-.335.48-.83 1.12-1.465 1.755C11.879 11.332 10.119 12.5 8 12.5c-2.12 0-3.879-1.168-5.168-2.457A13.134 13.134 0 0 1 1.172 8z");
            Eye.AddPath(SKPath.ParseSvgPathData("M8 5.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5zM4.5 8a3.5 3.5 0 1 1 7 0 3.5 3.5 0 0 1-7 0z"));

            ArrowClockwise = SKPath.ParseSvgPathData("M8 3a5 5 0 1 0 4.546 2.914.5.5 0 0 1 .908-.417A6 6 0 1 1 8 2v1z");
            ArrowClockwise.AddPath(SKPath.ParseSvgPathData("M8 4.466V.534a.25.25 0 0 1 .41-.192l2.36 1.966c.12.1.12.284 0 .384L8.41 4.658A.25.25 0 0 1 8 4.466z"));

            InfoCircle.AddPath(SKPath.ParseSvgPathData("m8.93 6.588-2.29.287-.082.38.45.083c.294.07.352.176.288.469l-.738 3.468c-.194.897.105 1.319.808 1.319.545 0 1.178-.252 1.465-.598l.088-.416c-.2.176-.492.246-.686.246-.275 0-.375-.193-.304-.533L8.93 6.588zM9 4.5a1 1 0 1 1-2 0 1 1 0 0 1 2 0z"));

            ArrowLeft.FillType = List.FillType = ArrowsScroll.FillType = MyArrowDownLeft.FillType = ArrowBarUp.FillType = ArrowBarDown.FillType 
                = ArrowsMove.FillType = MyArrowsAngleRight.FillType = MyArrowsAngleLeft.FillType = Copy.FillType = SKPathFillType.EvenOdd;
        }

        public static SKBitmap ToBitmap(int width, int height, int border, params (SKPath path, Action<SKPaint> init)[] paths)
        {
            var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            var rect = ((SKRect)bitmap.Info.Rect).Deflate(border);
            canvas.Clear();

            SKRect bounds = default;
             foreach (var (path, _) in paths)
                bounds.Union(path.Bounds);

            var t = UtilsUI.GetTransform(bounds, rect);

            foreach (var (path, init) in paths)
            {
                using var paint = new SKPaint() { Color = SKColors.Beige, IsAntialias = true };
                init?.Invoke(paint);

                var scaled = new SKPath(path);
                scaled.Transform(t);
                canvas.DrawPath(scaled, paint);
            }
            canvas.Flush();
            return bitmap;
        }
    }
}
