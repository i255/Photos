using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.ConstrainedExecution;
using System.Text;
using System.Threading.Tasks;
using Photos.Lib;
using Photos.Core.StoredTypes;

namespace Photos.Core
{

    class PhotoViewCacheEntry
    {
        public SKImage Image;
        public bool IsNew;
        public DateTime ErrorExpireTime;
        public bool ErrorExpired => Image == null && ErrorExpireTime != default && ErrorExpireTime < DateTime.UtcNow;
    }
    public class PhotoView : FreePanel
    {
        const int winSize = 3;

        SKMatrix _userTransform;

        Cache<FileRecordSourceKey, PhotoViewCacheEntry> bitmapData = new(winSize * 2 + 2) { OnDispose = x => x.Image?.Dispose() };
        FileRecordSourceKey GetKey(FileRecord f) => f.Src.Length == 0 ? default : new(f.Src.First());
        SpringAnimation animation;
        Toast InfoToast;

        readonly PhotoProvider photoProvider;
        public Menu MainMenu;
        public readonly Toast MainToast;
        PhotoViewCacheEntry _zoomImage;
        FileRecord _zoomImgFile;
        public readonly Button MenuButton;
        Menu ToolbarMenu;

        public event Action OnBackPressed;

        public readonly List<MenuItem> MenuItems;

        public readonly Val<float> ButtonPanelButtonSize = new(-1); // must be changed
        public readonly Val<float> FileNameLabelSize = new(-1); // must be changed

        public void GoBack()
        {
            if (IsEditMode)
                CloseEditor(false);
            else
            {
                ToolbarMenu.Close();
                OnBackPressed?.Invoke();
            }
        }

        public void StyleButton(Button b)
        {
            var orig = b.RenderFrameHandler;

            b.RenderFrameHandler = ctx =>
            {
                if (Enabled)
                {
                    var rect = b.OuterRect;
                    var midPoint = new SKPoint(rect.MidX, rect.MidY);
                    using var shader = SKShader.CreateRadialGradient(midPoint, midPoint.Length * 1.2f,
                                new SKColor[] { SKColors.Black.WithAlpha(0x90), SKColors.Transparent }, null, SKShaderTileMode.Clamp);

                    var radius = b.CornerRadius;
                    b.CornerRadius = Math.Min(midPoint.X, midPoint.Y);
                    b.Background.Shader = shader;
                    var mode = b.Background.BlendMode;
                    b.Background.BlendMode = SKBlendMode.Darken;
                    orig(ctx);
                    b.CornerRadius = radius;
                    b.Background.BlendMode = mode;
                    b.Background.Shader = null;
                }
                else
                    orig(ctx);
            };

            //b.Background.Color = SKColors.Black;
            //b.CornerRadius = 
        }

        bool IsEditMode => _crop != null;

        public PhotoView(PhotoProvider photoProvider)
        {
            this.photoProvider = photoProvider;
            photoProvider.IndexChanged += () => Invalidate();

            KeyUpHandler = KeyUp;
            MouseHandler = OnMouse;
            RenderFrameHandler = RenderFrame;
            animation = new(this) { Animate = Animate, Mass = 1, Damping = 0.6f, Stiffness = 300 };

            FileNameLabelSize.Func = () => ButtonPanelButtonSize * 0.5f;

            InfoToast = new Toast()
            {
                Link = Attach(() => InnerRect.OffsetEdge(top: InnerRect.Height - 5 - FileNameLabelSize, bottom: -5)),
                BackToFrontRatio = 0.4f
            };

            MainToast = new Toast()
            {
                Link = Attach(() => CenterRect(new SKSize(0, FileNameLabelSize * 1.5f))),
                //BackToFrontRatio = 0.4f
            };

            StyleButton(new Button()
            {
                Link = Attach(() => SKRect.Create(InnerRect.Left, InnerRect.Top, ButtonPanelButtonSize, ButtonPanelButtonSize)),
                Icon = { Const = IconStore.ArrowLeft },
                OnClick = GoBack,
                Enabled = { Func = () => !IsEditMode }
            });

            //new DragButton()
            //{
            //    Link = Attach(() => OuterRect),
            //    Enabled = { Func = () => _crop != null },
            //    GetPosition = () => _lastTransform.MapPoint(new SKPoint(_crop.Value.MidX * _lastRenderInfo.Width, _crop.Value.MidY * _lastRenderInfo.Height)),
            //    UpdatePosition = p =>
            //    {
            //        var imgPoint = _lastInverse.MapPoint(p);
            //        var pctCropStart = new SKPoint(imgPoint.X / _lastRenderInfo.Width, imgPoint.Y / _lastRenderInfo.Height) - _crop.Value.Size.ToPoint().Multiply(0.5f);
            //        pctCropStart.X = Math.Clamp(pctCropStart.X, 0, 1 - _crop.Value.Width);
            //        pctCropStart.Y = Math.Clamp(pctCropStart.Y, 0, 1 - _crop.Value.Height);
            //        _crop = SKRect.Create(pctCropStart, _crop.Value.Size);
            //    },
            //    DragMidPoint = new SKPoint(.5f, .5f)
            //};

            var dragButtons = new List<DragButton>();
            IsCropDrag = () => dragButtons.Any(x => x.IsDragging);

            MakeCropButton(x => new SKPoint(x.MidX, x.MidY), (crop, p) =>
            {
                p -= crop.Size.ToPoint().Multiply(0.5f);
                p.X = Math.Clamp(p.X, 0, 1 - crop.Width);
                p.Y = Math.Clamp(p.Y, 0, 1 - crop.Height);
                return SKRect.Create(p, crop.Size);
            }, new SKPoint(.5f, .5f), IconStore.ArrowsMove);

            MakeCropButton(x => new SKPoint(x.Left, x.Top), (crop, p) =>
            {
                crop.Left = Math.Clamp(p.X, 0, crop.Right);
                crop.Top = Math.Clamp(p.Y, 0, crop.Bottom);
                return crop;
            }, new SKPoint(0, 0), IconStore.MyArrowsAngleLeft);

            MakeCropButton(x => new SKPoint(x.Right, x.Top), (crop, p) =>
            {
                crop.Right = Math.Clamp(p.X, crop.Left, 1);
                crop.Top = Math.Clamp(p.Y, 0, crop.Bottom);
                return crop;
            }, new SKPoint(1, 0), IconStore.MyArrowsAngleRight);

            MakeCropButton(x => new SKPoint(x.Left, x.Bottom), (crop, p) =>
            {
                crop.Left = Math.Clamp(p.X, 0, crop.Right);
                crop.Bottom = Math.Clamp(p.Y, crop.Top, 1);
                return crop;
            }, new SKPoint(0, 1), IconStore.MyArrowsAngleRight);

            MakeCropButton(x => new SKPoint(x.Right, x.Bottom), (crop, p) =>
            {
                crop.Right = Math.Clamp(p.X, crop.Left, 1);
                crop.Bottom = Math.Clamp(p.Y, crop.Top, 1);
                return crop;
            }, new SKPoint(1, 1), IconStore.MyArrowsAngleLeft);

            void MakeCropButton(Func<SKRect, SKPoint> getPoint, Func<SKRect, SKPoint, SKRect> updateCrop, SKPoint midPoint, SKPath icon)
            {
                dragButtons.Add(new DragButton()
                {
                    Link = Attach(() => OuterRect),
                    Enabled = { Func = () => _crop != null },
                    GetPosition = () => 
                    {
                        var pt = getPoint(_crop.Value);
                        return _lastTransform.MapPoint(new SKPoint(pt.X * _lastRenderInfo.Width, pt.Y * _lastRenderInfo.Height));
                    },
                    UpdatePosition = p =>
                    {
                        var imgPoint = _lastInverse.MapPoint(p);
                        var pctPoint = new SKPoint(imgPoint.X / _lastRenderInfo.Width, imgPoint.Y / _lastRenderInfo.Height);
                        _crop = updateCrop(_crop.Value, pctPoint);
                        UpdateCropToAspectRatio(midPoint);
                    },
                    DragMidPoint = midPoint,
                    ButtonSize = { Func = () => new SKSize(ButtonPanelButtonSize, ButtonPanelButtonSize) },
                    Icon = icon
                });
            }

            MenuItems = new List<MenuItem>()
            {
                new MenuItem()
                {
                    Icon = IconStore.InfoCircle,
                    Text = "Info",
                    OnClick = () => MainMenu.Show(ShowImageInfoMenu),
                },
                new MenuItem()
                {
                    Icon = IconStore.ArrowClockwise,
                    Text = "Rotate",
                    OnClick = Rotate,
                },
                new MenuItem()
                {
                    Icon = IconStore.Crop,
                    Text = "Crop",
                    Submenu = Crop,
                },
                new MenuItem()
                {
                    Icon = IconStore.Trash3,
                    Text = "Delete",
                    OnClick = Trash,
                },
            };

            foreach (var item in MenuItems)
                item.TextAlign = SKTextAlign.Right;

            MenuButton = new Button()
            {
                Icon = { Func = () => ToolbarMenu.Enabled ? IconStore.CaretUp : IconStore.CaretDown },
                OnClick = () =>
                {
                    if (ToolbarMenu.Enabled)
                        ToolbarMenu.Close();
                    else
                        ToolbarMenu.Show(m => { m.Items.AddRange(MenuItems); m.FitToSize = true; });
                },
                Link = Attach(() => new SKSize(ButtonPanelButtonSize, ButtonPanelButtonSize).PositionInsideRect(InnerRect, right: true)),
                Enabled = { Func = () => !IsEditMode }
            };
            StyleButton(MenuButton);

            StyleButton(new Button()
            {
                Icon = { Func = () => photoProvider.DB.GetAdditionalImageData(CurrentFile).IsFavorite ? IconStore.StarFilled : IconStore.Star },
                OnClick = ToggleFavorite,
                Link = Attach(() => MenuButton.Position.Select(x => x.OffsetRect(x: -ButtonPanelButtonSize - MainView.ButtonPadding))),
                Enabled = { Func = () => !IsEditMode && CurrentFile.IsIndexed() }
            });

            ToolbarMenu = new Menu()
            {
                Link = Attach(() => OuterRect),
                DialogRect = () => MenuButton.Position.Select(x => x.OffsetRect(y: ButtonPanelButtonSize + MainView.ButtonPadding)
                    .OffsetEdge(left: -Size.Width * 2 / 3, bottom: (ButtonPanelButtonSize + MainView.ButtonPadding) * (ToolbarMenu.ActiveMenu?.Items.Count ?? 1 - 0.9f))),
                RightPadding = { Func = () => 0 },
                Padding = { Const = 0 },
                Background = { Color = SKColors.Transparent },
                OverlayPaint = { Color = SKColors.Transparent },
                ItemMargin = { Func = () => MainView.ButtonPadding },
                ShowHeader = false,
                Modal = false,
                ButtonSize = { Func = () => ButtonPanelButtonSize },
            };
            StyleButton(ToolbarMenu.MenuButton);

            //Dropdown = new DropdownMenu()
            //{
            //    Parent = this,
            //    Link = () => SKRect.Create(Size.Width - ButtonPanelButtonSize * 4, MainView.ButtonPadding, ButtonPanelButtonSize * 4, Size.Height - ButtonPanelButtonSize),
            //    ButtonSize = { Func = () => ButtonPanelButtonSize }
            //};

            //Dropdown.Items.AddRange(items);

            photoProvider.OnBackgroundWorker += Preload;
        }

        readonly Func<bool> IsCropDrag;

        private void CloseEditor(bool save)
        {

            if (save && _crop != null)
            {
                var file = CurrentFile;
                //var fname = file.Src.First().FN;

                //var ext = Path.GetExtension(fname);
                string picFolder = photoProvider.Indexer.EditorDirectory;

                Directory.CreateDirectory(picFolder);

                string newPath;
                int cntr = 0;
                var imgName = $"edited_{DateTime.Now:yyyyMMdd_HHmmss}";
                do
                {
                    //newPath = Path.Combine(picFolder, $"{fname[0..^ext.Length]}~{cntr}.jpg");
                    newPath = Path.Combine(picFolder, cntr == 0 ? $"{imgName}.jpg" : $"{imgName}~{cntr}.jpg");
                    cntr++;
                } while (File.Exists(newPath));

                using var imgLoadData = photoProvider.Indexer.GetFullImage(file);
                SKImage img = null;
                bool dispose = true;
                if (imgLoadData != null)
                {
                    ImageLoader.DecodeAndResizeImage(imgLoadData, (_, _) => 1);
                    if (imgLoadData.Bitmap != null)
                        img = SKImage.FromBitmap(imgLoadData.Bitmap);
                }

                if (img == null)
                {
                    img = photoProvider.GetFullThumbnail(file);
                    dispose = false;
                }

                using var bmp = ImageLoader.Crop(img, _crop.Value);
                if (dispose)
                    img.Dispose();

                using var data = bmp.Encode(SKEncodedImageFormat.Jpeg, 95);

                File.WriteAllBytes(newPath, data.ToArray());

                MainToast.Show($"Saved"); //  to '{newPath}'

                //photoProvider.SelectOnNextRefreshPath = newPath;
                Task.Run(() =>
                {
                    photoProvider.UpdateLibraryMaybe(new() { QuickScanOnly = true });
                    photoProvider.OpenFile(newPath);
                }).DieOnError();

            }

            _crop = null;
            ToolbarMenu.Close();
        }

        SKRect? _crop;
        private float? _aspectRatioConstraint;

        private void Crop(MenuDef menu)
        {
            var editMenuItems = new List<MenuItem>()
            {
                new MenuItem()
                {
                    Icon = IconStore.Check,
                    Text = "Save copy",
                    OnClick = () => CloseEditor(true),
                    ReturnLevel = menu.Parent
                },
                new MenuItem()
                {
                    Icon = IconStore.X,
                    Text = "Discard",
                    OnClick = () => CloseEditor(false),
                    ReturnLevel = menu.Parent
                },
                new MenuItem(),
                new MenuItem()
                {
                    Text = "Free",
                    Selected = () => !_aspectRatioConstraint.HasValue,
                    OnClick = () =>
                    {
                        _aspectRatioConstraint = null;
                    }
                },
                new MenuItem()
                {
                    Text = "Square (1:1)",
                    Selected = () => _aspectRatioConstraint.HasValue && Math.Abs(_aspectRatioConstraint.Value - 1f) < 0.01f,
                    OnClick = () => 
                    {
                        _aspectRatioConstraint = 1f;
                        UpdateCropToAspectRatio();
                    }
                },
                new MenuItem()
                {
                    Text = "4:3",
                    Selected = () => _aspectRatioConstraint.HasValue && Math.Abs(_aspectRatioConstraint.Value - 4f/3f) < 0.01f,
                    OnClick = () => 
                    {
                        _aspectRatioConstraint = 4f/3f;
                        UpdateCropToAspectRatio();
                    }
                }
            };

            foreach (var item in editMenuItems)
            {
                item.TextAlign = SKTextAlign.Right;
            }

            menu.Items.AddRange(editMenuItems);
            menu.FitToSize = true;

            if (_crop == null)
            {
                _aspectRatioConstraint = null;
                _crop = SKRect.Create(0.1f, 0.1f, 0.8f, 0.8f);
            }
        }

        private void UpdateCropToAspectRatio(SKPoint? dragHandle = null)
        {
            if (!_aspectRatioConstraint.HasValue || !_crop.HasValue)
                return;

            var currentCrop = _crop.Value;
            var imgWidth = (float)_lastRenderInfo.Width;
            var imgHeight = (float)_lastRenderInfo.Height;
            
            var currentAspect = currentCrop.Width / currentCrop.Height;
            // Adjust target aspect ratio based on image dimensions
            var targetAspect = _aspectRatioConstraint.Value * (imgHeight / imgWidth);

            //Console.WriteLine($"_crop: {_crop.Value}, currentAspect: {currentAspect}, targetAspect: {targetAspect}");

            if (Math.Abs(currentAspect - targetAspect) > 0.01f)
            {
                // Determine which handle is being dragged and adjust only that side
                if (!dragHandle.HasValue || 
                    dragHandle.Value.X == 1 && dragHandle.Value.Y == 1) // Bottom-right
                {
                    currentCrop.Right = Math.Clamp(currentCrop.Left + currentCrop.Height * targetAspect, 0, 1);
                    currentCrop.Bottom = Math.Clamp(currentCrop.Top + currentCrop.Width / targetAspect, 0, 1);
                }
                else if (dragHandle.Value.X == 0 && dragHandle.Value.Y == 0) // Top-left
                {
                    currentCrop.Left = Math.Clamp(currentCrop.Right - currentCrop.Height * targetAspect, 0, 1);
                    currentCrop.Top = Math.Clamp(currentCrop.Bottom - currentCrop.Width / targetAspect, 0, 1);
                }
                else if (dragHandle.Value.X == 1 && dragHandle.Value.Y == 0) // Top-right
                {
                    currentCrop.Right = Math.Clamp(currentCrop.Left + currentCrop.Height * targetAspect, 0, 1);
                    currentCrop.Top = Math.Clamp(currentCrop.Bottom - currentCrop.Width / targetAspect, 0, 1);
                }
                else if (dragHandle.Value.X == 0 && dragHandle.Value.Y == 1) // Bottom-left
                {
                    currentCrop.Left = Math.Clamp(currentCrop.Right - currentCrop.Height * targetAspect, 0, 1);
                    currentCrop.Bottom = Math.Clamp(currentCrop.Top + currentCrop.Width / targetAspect, 0, 1);
                }                    
            }

            _crop = currentCrop;
        }

        private void ToggleFavorite()
        {
            var f = CurrentFile;
            var dat = photoProvider.DB.GetAdditionalImageData(f);
            dat.IsFavorite = !dat.IsFavorite;
            photoProvider.DB.SetAdditionalImageData(f, dat);
        }

        void Preload()
        {
            if (Window == null || !Enabled)
                return;

            FileRecord fname = null;
            bool loadForZoom = false;

            if (_userTransform.ScaleX > 1 &&
                (_lastFile != _zoomImgFile || _zoomImage?.ErrorExpired == true))
            {
                loadForZoom = true;
                fname = _lastFile;
            }
            else
                fname = null;

          
            if (fname == null)
            {
                var dir = stepDirection == 0 ? 1 : stepDirection;
                var indexes = Enumerable.Range(0, winSize).Concat(Enumerable.Range(1, winSize - 1).Select(x => -x)).Select(x => x * dir + photoProvider.Idx);

                foreach (var candFile in photoProvider.GetFiles(indexes))
                    if (candFile.Src.Length > 0)
                    {
                        var candKey = GetKey(candFile);
                        var cached = bitmapData.Get(candKey);
                        if (cached == null)
                        {
                            fname = candFile;
                            break;
                        }
                        else if (cached.ErrorExpired)
                            bitmapData.Remove(candKey);
                    }
            }

            var size = Size;
            if (fname == null || size.Width == 0 || size.Height == 0)
                return;

            PhotoViewCacheEntry entry;
            var dt = DateTime.UtcNow;
            var startOrientation = photoProvider.DB.GetOrientation(fname);
            using var data = photoProvider.Indexer.GetFullImage(fname);
            dt.PrintUtcMs($"load {fname.Sig}", 500);

            if (data != null)
            {
                float zoomScaler(SKImageInfo x, bool exact)
                {
                    var hardLimit = (Window.MaxRenderTargetSize == 0 ? 4 << 10 : Window.MaxRenderTargetSize) - 64;
                    var fastLimit = hardLimit * 5 / 6;
                    float maxSide = Math.Max(x.Width, x.Height);
                    return Math.Max(1, maxSide / (exact ? hardLimit : fastLimit));
                }

                var minScreenSide = (int)Math.Min(size.Width, size.Height);
                ImageLoader.DecodeAndResizeImage(data, loadForZoom ? zoomScaler : ImageLoader.ScaleToMinSide(minScreenSide, minScreenSide));
                //x => (float)Math.Max(1, Math.Sqrt((double)x.Width * x.Height * x.BytesPerPixel / MaxImageSize)) : 

                if (data.Bitmap == null) // decode failed
                {
                    entry = new PhotoViewCacheEntry() { ErrorExpireTime = DateTime.MaxValue }; // do not retry
                    Utils.Trace($"no img {fname.Sig}");
                }
                else
                    entry = new PhotoViewCacheEntry() { Image = SKImage.FromBitmap(data.Bitmap), IsNew = true };
            }
            else // get data failed
            {
                Utils.Trace($"no img data {fname.Sig}");
                entry = new PhotoViewCacheEntry() { ErrorExpireTime = DateTime.UtcNow.AddSeconds(15) }; // retry
            }

            if (startOrientation == photoProvider.DB.GetOrientation(fname)) // could have been changed by user
            {
                if (loadForZoom)
                {
                    var tmp = _zoomImage;
                    _zoomImage = null;
                    tmp?.Image?.Dispose();

                    _zoomImgFile = fname;
                    _zoomImage = entry;
                }
                else
                    bitmapData.Put(GetKey(fname), entry);
            }

            dt.PrintUtcMs($"load decode {fname.Sig}", 500);
        }

        int stepDirection = 1;
        DateTime _lastKey;
        void KeyUp(libKeyMessage e)
        {
            var time = DateTime.UtcNow;
            if ((time - _lastKey).TotalMilliseconds < 30)
                return; // prevent jumping

            var dir = 0;
            if (e.Key == libKeys.Left)
                dir = -1;
            if (e.Key == libKeys.Right)
                dir = 1;

            if (dir != 0)
            {
                e.Consume();
                SwitchImage(dir, false);
                _lastKey = time;
            }
        }

        public void SwitchImage(int dir, bool checkZoom)
        {
            if (_userTransform.ScaleX == 1 || !checkZoom)
            {
                stepDirection = dir;
                SwitchImage();
            }
        }

        private bool SwitchImage(bool checkPosition = false)
        {
            ToolbarMenu.Close();

            if (stepDirection == 0)
                throw new Exception("0 direction");

            var curOffset = _userTransform.TransX - XTarget;
            if (Math.Sign(curOffset) == stepDirection)
            {
                if (checkPosition)
                    return false;
                else
                    curOffset = 0;
            }

            photoProvider.Idx += stepDirection;
            transitionOffset = curOffset + stepDirection * (OuterRect.Width + ImgMargin);
            animation.Stop();
            return true;
        }

        bool _isDragging = false;
        bool _scrollDetected;
        DateTime _lastScaleUtc;

        void OnMouse(libMouseEvent e)
        {
            if ((e.Kind == libMouseEventKind.Click || e.Kind == libMouseEventKind.Up && !_scrollDetected && Window.ManualClickDetection) && !IsEditMode)
            {
                _isDragging = false;
                var dir = Math.Sign(e.CurrentPoint.X - OuterRect.Width / 2);
                if (dir != 0)
                {
                    stepDirection = dir;
                    SwitchImage();
                }
            }
            else if (_userTransform != default)
            {
                if (e.Kind == libMouseEventKind.Wheel)
                {
                    var scale = (float)Math.Pow(1.1, e.Offset.Y);
                    ScaleDrawRect(scale);
                    Invalidate();
                }
                else if (e.Kind == libMouseEventKind.Scale)
                {
                    ScaleDrawRect(e.Offset.X);
                    Invalidate();
                }
                else if (e.Kind == libMouseEventKind.DragScroll /*&& !e.Rejected*/)
                {
                    _userTransform.TransX -= e.Offset.X;
                    _userTransform.TransY -= e.Offset.Y;
                    _isDragging = true;
                    FixTrans();
                    Invalidate();
                    _scrollDetected = true;
                }
                else if (e.Kind == libMouseEventKind.Up)
                {
                    _isDragging = false;
                    //if (_userTransform.ScaleX == 1)
                    //{
                    //    var speed = e.DragV.X;
                    //    //Debug.WriteLine(speed);
                    //    if (Math.Abs(speed) > 200)
                    //    {
                    //        stepDirection = -Math.Sign(speed);
                    //        SwitchImage(true);
                    //    }
                    //}
                    Invalidate();
                }
                else if (e.Kind == libMouseEventKind.Down)
                {
                    animation.Stop();
                    _isDragging = true;
                    _scrollDetected = false;
                }
            }

            void ScaleDrawRect(float scale)
            {
                var curScale = _userTransform.ScaleX;
                var targetScale = Math.Max(curScale * scale, 1f);
                scale = targetScale / curScale;

                _userTransform.ScaleX *= scale;
                _userTransform.ScaleY *= scale;
                _userTransform.TransX = e.InitialPoint.X - (scale * (e.InitialPoint.X - _userTransform.TransX));
                _userTransform.TransY = e.InitialPoint.Y - (scale * (e.InitialPoint.Y - _userTransform.TransY));
                FixTrans();
                _lastScaleUtc = DateTime.UtcNow;
            }

            void FixTrans()
            {
                var rect = OuterRect;
                var dY = rect.Height - _userTransform.ScaleY * _initRect.Height;
                if (dY > 0)
                    _userTransform.TransY = dY / 2;
                else
                    _userTransform.TransY = Math.Clamp(_userTransform.TransY, dY, 0);

                if (_userTransform.ScaleX != 1 || IsEditMode)
                {
                    var dX = rect.Width - _userTransform.ScaleX * _initRect.Width;
                    if (dX > 0)
                        _userTransform.TransX = dX / 2;
                    else
                        _userTransform.TransX = Math.Clamp(_userTransform.TransX, dX, 0);
                }
            }
        }
        void StartAnimation(float v)
        {
            animation.V = v;
            animation.X = _userTransform.TransX - XTarget;
            //Debug.WriteLine($" -------------{animation.X}-------------- ");

            //Task.Delay(100).ContinueWith(x=> animation.Start());
            animation.Start();
        }
        float XTarget => (OuterRect.Width - _initRect.Width) / 2;

        void Animate()
        {
            _userTransform.TransX = animation.X + XTarget;
            //Debug.WriteLine($"{DateTime.UtcNow.TimeOfDay.TotalSeconds}>{_userTransform.TransX}");
        }

        const float ImgMargin = 50;
        FileRecord _lastFile;
        SKSize _lastMaxBounds, _lastBounds;
        SKRect _initRect;
        float transitionOffset;
        SKImageInfo _lastRenderInfo;
        public static DateTime PerfTest;
        private static SKPaint disabledPaint = new () { Color = SKColors.Black.WithAlpha(0x80) };
        private static SKPaint cropBorderPaint = new() { Color = SKColors.Beige.WithAlpha(0xE0), StrokeWidth = 1, IsStroke = true };
        private SKMatrix _lastTransform;
        private SKMatrix _lastInverse;

        void RenderFrame(SKCanvas ctx)
        {
            photoProvider.MinPreloadImage = photoProvider.Idx - winSize;
            photoProvider.MaxPreloadImage = photoProvider.Idx + winSize;

            var bounds = OuterRect;

            RenderImage(ctx, photoProvider.Idx, bounds);

            if (_userTransform.ScaleX == 1 && !IsEditMode)
            {
                var center = (bounds.Width - _initRect.Width) / 2;
                var sign = Math.Sign(center - _userTransform.TransX);
                if (sign != 0)
                {
                    var next = photoProvider.Idx + sign;
                    var offset = _userTransform.TransX - center + sign * (bounds.Width + ImgMargin);
                    RenderImage(ctx, next, bounds, offset);

                    if (_userTransform.TransX != XTarget && animation.Stopped && !_isDragging)
                        StartAnimation(0);
                    else if (Math.Abs(center - _userTransform.TransX) > (bounds.Width + ImgMargin) / 2 && animation.Stopped && transitionOffset == 0)
                    {
                        photoProvider.Idx = next;
                        transitionOffset = offset;
                        Invalidate();
                    }
                }
            }
        }

        bool RenderImage(SKCanvas ctx, int idx, SKRect bounds, float offset = float.NaN)
        {
            photoProvider.ClampIdx(ref idx);
            var file = photoProvider.GetFile(idx);

            var cached = bitmapData.Get(GetKey(file));
            SKImage bitmap;
            if (_userTransform.ScaleX > 1 && file == _zoomImgFile && _zoomImage?.Image != null && (!_zoomImage.IsNew || (!_isDragging && animation.Stopped && (DateTime.UtcNow - _lastScaleUtc).TotalMilliseconds > 300)))
            {
                //if (_zoomImgNew)
                //    Window.GR?.PurgeResources();
                bitmap = _zoomImage.Image;
                _zoomImage.IsNew = false;
            }
            else if (cached?.Image != null && (!cached.IsNew || ((_userTransform.TransX == XTarget || _userTransform.ScaleX != 1) && animation.Stopped && !_isDragging && transitionOffset == 0)))
            {
                cached.IsNew = false;
                bitmap = cached.Image;
            }
            else
            {
                var dtF = DateTime.UtcNow;
                bitmap = photoProvider.GetFullThumbnail(file);
                dtF.PrintUtcMs($"thumb {file.Sig}", 30);
            }

            if (bitmap == null)
                return true;

            if (idx == photoProvider.Idx)
                _lastRenderInfo = bitmap.Info;

            var maxBounds = photoProvider.GetImageDimsAfterOrientation(file);
            if (maxBounds == default) // not yet indexed
                maxBounds = bitmap.Info.Rect.Size;

            var transform = UtilsUI.GetTransform(bitmap.Info.Rect, bounds, maxBounds);

            var isMainImage = float.IsNaN(offset);

            if (!isMainImage)
                transform.TransX += offset;
            else
            {
                _initRect = transform.MapRect(bitmap.Info.Rect);
                if (_lastFile != file || _lastMaxBounds != maxBounds || _lastBounds != bounds.Size)
                {
                    _userTransform = SKMatrix.CreateTranslation(transform.TransX, transform.TransY);
                    _lastFile = file;
                    _lastMaxBounds = maxBounds;
                    _lastBounds = bounds.Size;
                    //Debug.WriteLine(transitionOffset);
                    _userTransform.TransX += transitionOffset;
                    //if (transitionOffset != 0 && restartAnimation)
                    //    StartAnimation(0);
                    transitionOffset = 0;
                    InfoToast.Show(file.Src[0].FN, 5);
                }
                transform.TransX = transform.TransY = 0;

                transform = transform.PostConcat(_userTransform);
            }

            transform.TryInvert(out var inverse);
            if (isMainImage)
            {
                _lastTransform = transform;
                _lastInverse = inverse;
            }

            var drawTarget = transform.MapRect(bitmap.Info.Rect);
            drawTarget.Intersect(bounds);
            var srcRect = inverse.MapRect(drawTarget);

            var dt = DateTime.UtcNow;
            using SKPaint p = new() { IsAntialias = true };
            ctx.DrawImage(bitmap, srcRect, drawTarget, HighQualitySampling, p);
            if (_crop != null && isMainImage)
            {
                var isDragging = IsCropDrag();
                disabledPaint.Color = disabledPaint.Color.WithAlpha(isDragging ? (byte)0x90 : (byte)0xE0);

                ctx.DrawRect(drawTarget, disabledPaint);
                var fullCropTarget = transform.MapRect(((SKRect)bitmap.Info.Rect).Crop(_crop.Value));

                var cropTarget = fullCropTarget;
                cropTarget.Intersect(bounds);
                var cropSource = inverse.MapRect(cropTarget);

                ctx.DrawImage(bitmap, cropSource, cropTarget, p);


                cropBorderPaint.Color = cropBorderPaint.Color.WithAlpha(isDragging ? (byte)0x50 : (byte)0x20);

                ctx.DrawRect(fullCropTarget, cropBorderPaint);
                ctx.DrawRect(fullCropTarget.Crop(SKRect.Create(0, 1f / 3, 1, 1f / 3)), cropBorderPaint);
                ctx.DrawRect(fullCropTarget.Crop(SKRect.Create(1f / 3, 0, 1f / 3, 1)), cropBorderPaint);

                //_cropButtonCenter.Layout(SKRect.Create(fullCropTarget.MidX - ButtonPanelButtonSize / 2, fullCropTarget.MidY - ButtonPanelButtonSize / 2, ButtonPanelButtonSize, ButtonPanelButtonSize));
            }

            dt.PrintUtcMs($"DrawImage {file.Sig}", 30);

            if (PerfTest != default && cached?.Image == bitmap)
            {
                Utils.Log($"perfTest {AppContext.BaseDirectory} {PerfTest.GetUtcMs()}");
                Environment.Exit(0);
            }

            return true;
        }

        private void Trash()
        {
            FileRecord file = CurrentFile;
            if (!file.IsIndexed())
            {
                MainToast.Show("Please try again later");
                return;
            }

            var delMenu = new List<MenuItem>();
            foreach (var item in file.Src)
            {
                var dir = photoProvider.DB.Directories.GetValue(item.D);
                var fullPath = item.GetFullPath(photoProvider);
                if (dir.SourceId == 0 && File.Exists(fullPath.osPath))
                {
                    delMenu.Add(new MenuItem()
                    {
                        Text = $"Delete {fullPath.display}",
                        Submenu = m => Menu.Confirm(m, $"Yes, delete {fullPath.display}", () =>
                        {
                            photoProvider.DeleteSourceFile(fullPath.osPath, file).ContinueWith(x => Window.Run(() =>
                            {
                                MainToast.Show(x.Result == null ? "File deleted" : $"Delete failed: {x.Result}");

                                if (x.Result == null)
                                {
                                    photoProvider.DB.RemoveSingleSource(file, item);
                                    photoProvider.EnqueueRefreshDisplayOrder();
                                }
                            }));
                        }, okReturnLevel: Menu.CloseMenuLevel)
                    });
                }
            }

            MainMenu.Show(m =>
            {
                if (delMenu.Count == 1)
                    delMenu.Single().Submenu(m);
                else
                {
                    m.Title = "Choose a copy to delete";
                    m.Items.AddRange(delMenu);
                }
            });


        }

        private FileRecord CurrentFile => photoProvider.GetFile(photoProvider.Idx);

        private void Rotate()
        {
            var file = CurrentFile;
            if (!file.IsIndexed())
            {
                MainToast.Show("Please try again later");
                return;
            }

            var dat = photoProvider.DB.GetAdditionalImageData(file);
            var o = dat.Orientation == 0 ? SKEncodedOrigin.TopLeft : (SKEncodedOrigin)dat.Orientation;
            o = o switch
            {
                SKEncodedOrigin.TopLeft => SKEncodedOrigin.RightTop,
                SKEncodedOrigin.RightTop => SKEncodedOrigin.BottomRight,
                SKEncodedOrigin.BottomRight => SKEncodedOrigin.LeftBottom,
                SKEncodedOrigin.LeftBottom => SKEncodedOrigin.TopLeft,
                _ => SKEncodedOrigin.TopLeft
            };

            dat.Orientation = (int)o;

            photoProvider.DB.SetAdditionalImageData(file, dat);

            bitmapData.Remove(GetKey(file));
            _zoomImage = null;
            _zoomImgFile = null;
            photoProvider.InvalidateThumbnail(file);
            _lastFile = null;
        }

        void ShowImageInfoMenu(MenuDef m)
        {
            m.Title = "Image info";
            var file = photoProvider.GetFile(photoProvider.Idx);
            var thumb = photoProvider.GetFullThumbnail(file);
            m.Items.AddRange(new List<MenuItem>()
            {
                SetupItem(new() { Text = $"Timestamp: {FileRecord.TimeFrom(file.DT)}" }),
                SetupItem(new() { Text = $"Resolution: {file.W}x{file.H}" }),
                SetupItem(new() { Text = $"Size: {(double)file.S / 1024:0} KB" }),
                SetupItem(new() { Text = $"Thumbnail size: {(double)file.TS / 1024:0} KB" }),
            });

            if (thumb != null)
                m.Items.Add(SetupItem(new() { Text = $"Thumbnail resolution: {thumb.Width}x{thumb.Height}" }));

            foreach (var item in file.EnumOptionalData(stringsOnly: true))
            {
                var (key, text) = FileRecord.FormatOptional(photoProvider.DB, item.Key, item.Value);
                m.Items.Add(SetupItem(new() { Text = $"{key}: {text}" }));
            }

            m.Items.Add(SetupItem(new() { Text = $"Rendered as: {_lastRenderInfo.Width}x{_lastRenderInfo.Height} {_lastRenderInfo.BytesSize64 >> 20} MB" }));

            var lo = FileRecord.LoadDegree(file.GetOptional(OptionalKeys.Lon));
            var la = FileRecord.LoadDegree(file.GetOptional(OptionalKeys.Lat));
            if (la != 0 && lo != 0)
                m.Items.Add(new()
                {
                    Text = $"GPS Position: {la} {lo}",
                    OnClick = () => Window.OpenUrl(FormattableString.Invariant($"geo:{la},{lo}"))
                });

            m.Items.Add(SetupItem(new() { Text = $"File location:" }));
            var locItems = file.Src
                .Select(x => (path: x.GetFullPath(photoProvider), dir: photoProvider.DB.Directories.GetValue(x.D)))
                .Where(x => x.dir.IsInLib)
                .Select(x => (new MenuItem()
                {
                    Text = x.path.display,
                    UpperComment = photoProvider.DB.GetSourceName(x.dir.SourceId),
                    OnClick = OperatingSystem.IsWindows() && x.dir.SourceId == 0 && File.Exists(x.path.osPath) ? () => Process.Start("explorer.exe", "/select, \"" + x.path.osPath + "\"") : null
                })).ToList();

            foreach (var item in locItems.Where(x => x.OnClick == null))
                SetupItem(item);

            m.Items.AddRange(locItems);
        }

        MenuItem SetupItem(MenuItem item)
        {
            item.TextAlign = SKTextAlign.Left;
            item.TextScale = .7f;
            return item;
        }


    }
}
