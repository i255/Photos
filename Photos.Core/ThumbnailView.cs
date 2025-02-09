using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Photos.Lib;
using Photos.Core.StoredTypes;
using System.Globalization;

namespace Photos.Core
{

    public class ThumbnailView : FreePanel
    {
        int ThumbnailDrawSize => photoProvider.DB.Settings.ThumbnailDrawSize;

        public float HeaderHeight = 80;

        public const int ThumbnailMargin = 5;
        int LineHeight => photoProvider.IsFullScreenDrawMode ? throw new Exception() : ThumbnailDrawSize + ThumbnailMargin;

        string _dateTimeFormat;

        PhotoProvider photoProvider;

        public readonly ScrollContainer ScrollContainer;

        FileRecord lastShownFile;
        Replicator imgReplicator, groupReplicator;

        public readonly HashSet<int> SelectedItems = new();
        public bool SelectionActive => SelectedItems.Count > 0;

        public ThumbnailView(PhotoProvider photoProvider, Val<float> buttonSize)
        {
            ScrollContainer = new()
            {
                Link = Attach(() => OuterRect),
                ScrollerWidth = { Func = () => buttonSize },
                ScrollStep = { Func = () => photoProvider.IsFullScreenDrawMode ? Size.Height / 10 : 
                    Math.Min(photoProvider.DB.Settings.ThumbnailDrawSize / 2, Size.Height / 10) },
                ScrollPositionHintSize = { Func = () => buttonSize * 0.9f }
            };

            this.photoProvider = photoProvider;

            Enabled.OnConstSet += (o, n) =>
            {
                if (!Enabled /*|| photoProvider.DispalyGroups.Count == 0*/)
                    return;
                if (photoProvider.IsFullScreenDrawMode)
                {
                    ScrollContainer.ScrollOffsetY = imgPositions[photoProvider.Idx].Item1.Top - HeaderHeight / 2;
                }
                else
                {
                    var groupIdx = photoProvider.DispalyGroups.FindLastIndex(x => x.StartIdx <= photoProvider.Idx);
                    ScrollContainer.ScrollOffsetY = (int)GetGroupPosition()[groupIdx].Top + (photoProvider.Idx - photoProvider.DispalyGroups[groupIdx].StartIdx)
                        / ImgPerRow * LineHeight + HeaderHeight - (int)Size.Height * 1 / 3;
                }
            };

            new TextLabel()
            {
                Text = () => $"{photoProvider.DisplayedCount} / {photoProvider.TotalCount}",
                Link = ScrollContainer.Attach(() => SKRect.Create(0, 0, Size.Width - buttonSize * 4, buttonSize * 0.9f)),
                Padding = { Const = 3 },
                TextAlign = { Const = SKTextAlign.Right },
                DrawBorder = false,
            };

            groupReplicator = new Replicator()
            {
                Link = ScrollContainer.Attach(() => ScrollContainer.OuterRect),
                GetChildRect = i => GetGroupPosition()[i],
                ItemsCount = { Func = () => GetGroupPosition().Count },
            };

            var groupContainer = new FreePanel()
            {
                Link = groupReplicator.Attach(),
            };

            var title = new TextLabel()
            {
                Link = groupContainer.Attach(() => groupContainer.InnerRect.Select(x => new SKRect(x.Left, HeaderHeight * 0.2f, x.Right, HeaderHeight * 0.8f))),
                Text = () => photoProvider.DispalyGroups[groupReplicator.CurrentIndex].Key,
                DrawBorder = false,
            };

            imgReplicator = new Replicator()
            {
                Link = groupContainer.Attach(() => groupContainer.OuterRect with { Top = HeaderHeight }),
                TileSize = { Func = () => new SKSize(ThumbnailDrawSize, ThumbnailDrawSize) },
                ItemMargin = { Const = ThumbnailMargin },
                ItemsCount = { Func = () => photoProvider.DispalyGroups[groupReplicator.CurrentIndex].Count },
            };

            new ImageBox()
            {
                Link = imgReplicator.Attach(),
                Image = () =>
                {
                    //var farJump = Math.Abs(lastScrollOffsetY - ScrollContainer.ScrollOffsetY) > Size.Height;

                    //var y = groupContainer.Rect().Top + imgReplicator.Children[0].Rect().Top - ScrollContainer.ScrollOffsetY;
                    var imgIndex = photoProvider.DispalyGroups[groupReplicator.CurrentIndex].StartIdx + imgReplicator.CurrentIndex;
                    minImgIdx = Math.Min(minImgIdx, imgIndex);
                    maxImgIdx = Math.Max(maxImgIdx, imgIndex);

                    lastShownFile = photoProvider.GetFile(imgIndex);
                    var img = photoProvider.GetScaledThumbnail(lastShownFile,
                        //y > photoProvider.UserSettings.ThumbnailDrawSize + ThumbnailMargin && 
                        //!farJump &&
                        true);
                    if (img != null)
                    {
                        SKRect r = default;
                        if (photoProvider.IsFullScreenDrawMode)
                            r = SKRect.Create(imgPositions[imgIndex].Item1.Size);
                        return (img, r);
                    }

                    // draw micro
                    var size = photoProvider.GetImageDimsAfterOrientation(lastShownFile);
                    var rect = SKRect.Create(Math.Min(ThumbnailDrawSize, size.Width), Math.Min(ThumbnailDrawSize, size.Height));
                    rect.Offset(-(rect.Width - ThumbnailDrawSize) / 2, -(rect.Height - ThumbnailDrawSize) / 2);

                    return (photoProvider.GetMicroThumbnail(lastShownFile), rect);
                },
                OnClick = () =>
                {
                    var imgIndex = photoProvider.DispalyGroups[groupReplicator.CurrentIndex].StartIdx + imgReplicator.CurrentIndex;
                    photoProvider.Idx = imgIndex;
                    photoProvider.SelectPhoto();
                },
                OnAltClick = () =>
                {
                    //(Window.RootView as MainView).MainToast.Show("ALT TEST");
                    var imgIndex = photoProvider.DispalyGroups[groupReplicator.CurrentIndex].StartIdx + imgReplicator.CurrentIndex;
                    if (!SelectedItems.Contains(imgIndex))
                        SelectedItems.Add(imgIndex);
                    else
                        SelectedItems.Remove(imgIndex);
                },
                IsHighlighted = { Func = () => {
                    var imgIndex = photoProvider.DispalyGroups[groupReplicator.CurrentIndex].StartIdx + imgReplicator.CurrentIndex;
                    return SelectedItems.Contains(imgIndex);
                } }
            };

            ScrollContainer.ScrollPositionHint = () =>
            {
                if (photoProvider.DispalyGroups.Count == 0 || lastShownFile == null)
                    return "";

                return photoProvider.FilterService.CurrentSortMode switch
                {
                    SortModeEnum.Date => FileRecord.TimeFrom(lastShownFile.DT).ToString(_dateTimeFormat),
                    SortModeEnum.Filename => lastShownFile.SrcForSorting.GetFullPath(photoProvider).display,
                    _ => throw new NotImplementedException(),
                };
            };


            _dateTimeFormat = CultureInfo.CurrentCulture.DateTimeFormat.LongDatePattern;
            _dateTimeFormat = _dateTimeFormat.Replace("dddd", "").Replace("dddd", "").Trim(',').Trim();
        }
        int minImgIdx, maxImgIdx;
        protected override void RouteRenderFrame(SKCanvas ctx)
        {
            minImgIdx = int.MaxValue; 
            maxImgIdx = int.MinValue;

            base.RouteRenderFrame(ctx);

            if (minImgIdx != int.MaxValue)
            {
                photoProvider.MinPreloadImage = minImgIdx;
                photoProvider.MaxPreloadImage = maxImgIdx;
            }
            
        }

        public readonly Val<float> RightImageMargin = new(0);
        int ImgPerRow => photoProvider.IsFullScreenDrawMode ? 1 : imgReplicator.GetItemsPerRow(Size.Width - RightImageMargin);


        object lastDisplayGroups;
        int lastImgSize;
        float lastWidth;
        List<SKRect> groupPositions = new();
        List<(SKRect, float)> imgPositions = new();

        List<SKRect> GetGroupPosition()
        {
            var curDispalyGroups = photoProvider.DispalyGroups; // to avoid concurrency issues
            var imgPerRow = ImgPerRow;
            var width = Size.Width - RightImageMargin;

            if (!ReferenceEquals(lastDisplayGroups, curDispalyGroups) || lastWidth != width || lastImgSize != ThumbnailDrawSize)
            {
                imgReplicator.GetChildRect = photoProvider.IsFullScreenDrawMode 
                    ? idx => {
                        var hint = imgPositions[photoProvider.DispalyGroups[groupReplicator.CurrentIndex].StartIdx + idx];
                        return hint.Item1.OffsetRect(y: -hint.Item2); 
                    }
                    : null;
                groupPositions.Clear();
                imgPositions.Clear();
                var bottomOffset = 0f;

                foreach (var item in curDispalyGroups)
                {
                    SKRect r = default;
                    r.Top = bottomOffset;
                    
                    bottomOffset += HeaderHeight;

                    if (photoProvider.IsFullScreenDrawMode) 
                    {
                        var groupStart = bottomOffset;
                        for (var i = 0; i < item.Count; i++)
                        {
                            var size = photoProvider.GetImageDimsAfterOrientation(photoProvider.GetFile(i + item.StartIdx));
                            var imgW = Math.Min(size.Width, width);
                            var imgH = imgW / size.Width * size.Height;
                            imgPositions.Add((SKRect.Create((width - imgW) / 2, bottomOffset, imgW, imgH), groupStart));
                            bottomOffset += (imgH + ThumbnailMargin);
                        }
                    }
                    else
                        bottomOffset += (int)Math.Ceiling((double)item.Count / imgPerRow) * LineHeight;

                    r.Bottom = bottomOffset;
                    r.Right = width;
                    groupPositions.Add(r);
                }

                lastDisplayGroups = curDispalyGroups;
                lastWidth = width;
                lastImgSize = ThumbnailDrawSize;
            }

            return groupPositions;
        }
    }
}
