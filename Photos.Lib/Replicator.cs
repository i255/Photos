using SkiaSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{
    public class Replicator : BaseView
    {
        public Val<int> ItemsCount = new(0);

        public readonly Val<int> ItemMargin = new(0);

        public int CurrentIndex = -1;

        public Func<int,SKRect> GetChildRect;
        public readonly Val<SKSize> TileSize = new Val<SKSize>(SKSize.Empty);

        //public Func<Task> Preload;

        public int GetItemsPerRow(float width) => Math.Max(1, (int)((width + ItemMargin) / (TileSize.Value.Width + ItemMargin)));

        public (BaseView, object) Attach() => Children.Count == 0 ? (this, null) :  throw new InvalidOperationException("single child expected");

        public override void Init()
        {
            AdjustRect(r =>
            {
                var count = ItemsCount.Value;
                var tileSize = TileSize.Value;
                if (count == 0)
                    r.Bottom = 0;
                else if (GetChildRect != null)
                    r.Bottom = GetChildRect.Invoke(count - 1).Bottom;
                else
                {
                    var itemsPerRow = GetItemsPerRow(r.Width);
                    r.Bottom = (int)Math.Ceiling((double)count / itemsPerRow) * (tileSize.Height + ItemMargin);
                }
                r.Bottom += r.Top;
                return r;
            });
        }

        //int cntr;
        private SKRect ComputeChildRect(int idx)
        {
            //cntr++;
            if (GetChildRect != null)
                return GetChildRect(idx);
            else
            {
                var itemsPerRow = GetItemsPerRow(Size.Width);
                var y = idx / itemsPerRow;
                var x = idx % itemsPerRow;
                var loc = new SKPoint(x * (TileSize.Value.Width + ItemMargin), y * (TileSize.Value.Height + ItemMargin));
                return SKRect.Create(loc, TileSize);
            }
        }

        protected internal override void RouteKeyEvent(libKeyMessage e)
        {
            // no yet implemented
        }
        protected internal override void RouteMouseEvent(libMouseEvent e)
        {
            LoopItems(() => { base.RouteMouseEvent(e); return !e.Consumed; }, Math.Min(e.InitialPoint.Y, e.CurrentPoint.Y), Math.Max(e.InitialPoint.Y, e.CurrentPoint.Y));
        }

        protected internal override void RouteRenderFrame(SKCanvas ctx)
        {
            var rect = ctx.LocalClipBounds;
            LoopItems(() => { base.RouteRenderFrame(ctx); return true; }, rect.Top, rect.Bottom);
        }

        void LoopItems(Func<bool> action, float min, float max)
        {
            //var c = cntr;
            if (Children.Count != 1)
                throw new Exception("exacly one child expected");
            var count = ItemsCount.Value;

            var start = BinarySearch(0, count, 4, x => ComputeChildRect(x).Bottom >= min).low;
            var end = BinarySearch(start, count, 4, x => ComputeChildRect(x).Top > max).high;
            for (CurrentIndex = start; CurrentIndex <= end; CurrentIndex++)
            {
                Children[0].Layout(ComputeChildRect(CurrentIndex));

                if (!action())
                    break;
            }
            CurrentIndex = -1;

            //Debug.WriteLine($"{cntr - c} for {count}");
        }

        public static (int low, int high) BinarySearch(int low, int count, int treshold, Func<int, bool> pred)
        {
            //int low = 0;
            int high = count - 1;
            while (high - low > treshold)
            {
                var mid = low + (high - low) / 2;
                if (pred(mid))
                    high = mid - 1;
                else
                    low = mid + 1;
            }

            return (low, high);
        }

        public void ForceFullLayout()
        {
            for (CurrentIndex = 0; CurrentIndex < ItemsCount.Value; CurrentIndex++)
                Children[0].Layout(ComputeChildRect(CurrentIndex));
        }
    }
}
