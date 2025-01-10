using Photos.Lib;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Core
{
    public class ImageLoader
    {
        public static readonly List<Action<FullImageData>> ImageLoaderList = new();
        public static readonly List<string> KnownEndings = [".jpg", ".jpeg", ".gif", ".webp", ".heic", ".png", ".bmp", ".dng"];


        public static void DecodeAndResizeImage(FullImageData imgData, Func<SKImageInfo, bool, float> scaler)
        {
            using SKCodec sKCodec = SKCodec.Create(imgData.Data);
            SKEncodedOrigin origin = SKEncodedOrigin.TopLeft;

            try
            {
                if (sKCodec != null)
                {
                    origin = sKCodec.EncodedOrigin;
                    imgData.OriginalInfo = sKCodec.Info;
                    var scale = scaler(imgData.OriginalInfo, false);
                    var preScale = sKCodec.GetScaledDimensions((1f / scale) + (1f / 16)); // they add 1/16 in onGetScaledDimensions see https://github.com/google/skia/blob/main/src/codec/SkJpegCodec.cpp
                    // GetScaledDimensions does not work in 2.88.6
                    // https://github.com/mono/SkiaSharp/issues/2645

                    //if (preScale.Height < shortSideSize || preScale.Width < shortSideSize)
                    //    if (preScale != info.Size)
                    //    {
                    //        preScale = info.Size;
                    //        Trace($"bad scale: {preScale} {info.Size}");
                    //    }
                    imgData.Bitmap = new SKBitmap(preScale.Width, preScale.Height, imgData.OriginalInfo.ColorType, imgData.OriginalInfo.AlphaType);
                    var codecRes = sKCodec.GetPixels(imgData.Bitmap.Info, imgData.Bitmap.GetPixels(out var length));
                    if (codecRes != SKCodecResult.Success && codecRes != SKCodecResult.IncompleteInput)
                    {
                        //Utils.Trace($"no pixels");
                        imgData.Bitmap.Dispose();
                        imgData.Bitmap = null;
                    }
                }
                //else
                //    Utils.Trace($"failed");


                if (imgData.Bitmap == null)
                    foreach (var item in ImageLoaderList)
                    {
                        item.Invoke(imgData);
                        if (imgData.Bitmap != null)
                            break;
                    }

                if (imgData.Bitmap != null) // exact scale
                {
                    var bmp = imgData.Bitmap;
                    var postScale = scaler(bmp.Info, true);

                    if (postScale != 1f)
                    {
                        var res = new SKBitmap((int)Math.Round(bmp.Width / postScale), (int)Math.Round(bmp.Height / postScale), bmp.Info.ColorType, bmp.Info.AlphaType);
                        if (!bmp.ScalePixels(res, BaseView.HighQualitySampling))
                        {
                            res.Dispose();
                            res = null;
                        }
                        else
                        {
                            imgData.Bitmap = res;
                            bmp.Dispose();
                        }
                    }

                    FixOrientation(ref imgData.Bitmap, origin);
                    FixOrientation(ref imgData.Bitmap, imgData.Orientation);
                }
            }
            catch (Exception ex)
            {
                imgData.Bitmap?.Dispose();
                imgData.Bitmap = null;
                Utils.Trace($"error loading {imgData.FullPath}, {imgData.Data.Size}");
                Utils.TraceError(ex);
            }
        }

       

        public static void FixOrientation(ref SKBitmap bmp, SKEncodedOrigin origin)
        {
            if (origin == SKEncodedOrigin.TopLeft)
                return;

            var m = origin switch
            {
                SKEncodedOrigin.TopRight => SKMatrix.CreateScale(-1, 1).PostConcat(SKMatrix.CreateTranslation(bmp.Width, 0)),
                SKEncodedOrigin.BottomRight => SKMatrix.CreateRotationDegrees(180).PostConcat(SKMatrix.CreateTranslation(bmp.Width, bmp.Height)),
                SKEncodedOrigin.BottomLeft => SKMatrix.CreateScale(1, -1).PostConcat(SKMatrix.CreateTranslation(0, bmp.Height)),
                SKEncodedOrigin.LeftTop => SKMatrix.CreateRotationDegrees(90).PostConcat(SKMatrix.CreateScale(-1, 1)),
                SKEncodedOrigin.RightTop => SKMatrix.CreateRotationDegrees(90).PostConcat(SKMatrix.CreateTranslation(bmp.Height, 0)),
                SKEncodedOrigin.RightBottom => SKMatrix.CreateRotationDegrees(-90)
                    .PostConcat(SKMatrix.CreateScale(-1, 1)).PostConcat(SKMatrix.CreateTranslation(bmp.Height, bmp.Width)),
                SKEncodedOrigin.LeftBottom => SKMatrix.CreateRotationDegrees(-90).PostConcat(SKMatrix.CreateTranslation(0, bmp.Width)),
                _ => throw new NotImplementedException(),
            };

            SKBitmap rotated;
            switch (origin)
            {
                case SKEncodedOrigin.TopRight:
                case SKEncodedOrigin.BottomRight:
                case SKEncodedOrigin.BottomLeft:
                    rotated = new SKBitmap(bmp.Width, bmp.Height);
                    break;
                case SKEncodedOrigin.LeftTop:
                case SKEncodedOrigin.RightTop:
                case SKEncodedOrigin.RightBottom:
                case SKEncodedOrigin.LeftBottom:
                    rotated = new SKBitmap(bmp.Height, bmp.Width);
                    break;
                default:
                    throw new NotImplementedException();
            }

            using var surface = new SKCanvas(rotated);
            //surface.DrawRect(SKRect.Create(2, 2, rotated.Width - 4, rotated.Height - 4), new SKPaint() { Color = SKColors.Red, StrokeWidth = 4 });
            surface.SetMatrix(m);
            surface.DrawBitmap(bmp, 0, 0);
            surface.Flush();

            bmp.Dispose();
            bmp = rotated;
        }

        public static SKImage DecodeThumbnail(SKData data, SKEncodedOrigin orientation)
        {
            //var img = SKImage.FromEncodedData(data);
            //var res = img.ToRasterImage();
            //if (res != img)
            //    img.Dispose();
            //return res;

            var bmp = SKBitmap.Decode(data);
            if (bmp == null)
                return null;

            try
            {
                FixOrientation(ref bmp, orientation);
                return SKImage.FromBitmap(bmp);
            }
            finally { bmp.Dispose(); }
        }

        public static Func<SKImageInfo, bool, float> ScaleToMinSide(int shortSideSize, int tolerance) => (info, exact) =>
        {
            var minSize = (float)Math.Min(info.Width, info.Height);
            if (minSize <= shortSideSize + tolerance)
                return 1f;
            return Math.Max(1, minSize / shortSideSize);
        };

        internal static SKBitmap Crop(SKImage image, SKRect crop)
        {
            var cropRect = SKRectI.Truncate(((SKRect)image.Info.Rect).Crop(crop));

            var bmp = new SKBitmap(cropRect.Width, cropRect.Height);
            using var surface = new SKCanvas(bmp);
            using var pt = new SKPaint() { IsAntialias = true };
            surface.DrawImage(image, cropRect, bmp.Info.Rect, BaseView.HighQualitySampling, pt);
            surface.Flush();

            return bmp;
        }
    }
}
