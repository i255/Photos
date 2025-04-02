using Photos.Core.StoredTypes;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Core
{
    public class IndexTaskConfig
    {
        public bool QuickScanOnly, ForceMetaRefresh, ForceRefreshDisplayAfterIndex;
        public Action RunAfterIndex;
        public string DoneMessage;
    }

    public class IndexUpdateInfo
    {
        public readonly List<FileRecord> FileList = new();
        public bool IsFull { get; init; }
        public IReadOnlyDictionary<ulong, AdditionalImageData> AdditionalImageDataOut { get; init; }

        internal readonly IReadOnlyDictionary<ulong, AdditionalImageData> AdditionalImageDataIn = new Dictionary<ulong, AdditionalImageData>();
        public readonly List<(string, string, AdditionalImageData)> AdditionalImageDataInString = new();
        public void AddImageData(ulong key, AdditionalImageData val)
        {
            if (AdditionalImageDataIn.TryGetValue(key, out var ex) && ex.TimeStamp > val.TimeStamp)
                return;

            ((IDictionary<ulong, AdditionalImageData>)AdditionalImageDataIn)[key] = val;
        }
    }

    public class PhotoIndexer
    {
        internal volatile bool CheckFilesPrio;

        private readonly Storage DB;
        internal volatile bool CancelIndex;
        public readonly bool ProtectUnreadableFolders;

        public event Action<bool> OnIndexing;
        public void SetWorkStatus(string s, bool done = false) => _workStatus = done ? s : $"...{s}...";
        string _workStatus;
        public string WorkStatus => _workStatus;
        public event Action<IndexUpdateInfo> FileRecordProvider;
        internal event Action IndexUpdateRequest;

        public readonly string EditorDirectory;
        public event Action<List<string>> RegisterBuiltInDirs;

        volatile int _indexCounter;
        public Task FirstIndexTask => _firstIndexFinished.Task;
        TaskCompletionSource _firstIndexFinished = new();

        public Action<FullImageData> FileContentProvider;

        internal List<string> GetBuiltInLibraryFolders()
        {
            var res = new List<string> { EditorDirectory };
            RegisterBuiltInDirs?.Invoke(res);
            return res;
        }

        public PhotoIndexer(Storage db, bool protectUnreadableFolders, string editorDirectory)
        {
            DB = db;
            ProtectUnreadableFolders = protectUnreadableFolders;
            EditorDirectory = editorDirectory;
        }

        internal void ListDir(Func<bool> isCanceled, IndexUpdateInfo indexInfo, string dir, bool recurse, bool checkAdditionalInfo = false)
        {
            try
            {
                var files = new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions() { RecurseSubdirectories = recurse, AttributesToSkip = FileAttributes.None });
                foreach (var x in files)
                {
                    if (ImageLoader.KnownEndings.Contains(x.Extension.ToLowerInvariant()))
                        FileRecord.AddFromInfo(indexInfo.FileList, x, DB);
                    if (checkAdditionalInfo && x.Name.EndsWith("picasa.ini", StringComparison.OrdinalIgnoreCase))
                    {
                        string fname = null;
                        foreach (var item in File.ReadLines(x.FullName))
                        {
                            if (item.StartsWith('[') && item.EndsWith(']') && item.Contains('.'))
                                fname = item[1..^1];
                            else if (item == "star=yes" && fname != null)
                            {
                                indexInfo.AdditionalImageDataInString.Add((x.DirectoryName, fname,
                                    new AdditionalImageData() { IsFavorite = true, TimeStamp = FileRecord.TimeTo(x.LastWriteTime) }));
                                fname = null;
                            }
                        }
                    }

                    if (isCanceled != null && isCanceled())
                        return;
                }

            }
            catch { }
        }


        internal void IndexDirs(IndexTaskConfig cfg, IReadOnlyList<FileRecord> folderViewList)
        {
            var startTime = DateTime.UtcNow;
            bool done = false;
            var onIndexingTask = Utils.WaitWhile(() => OnIndexing == null && !done).ContinueWith(x => OnIndexing?.Invoke(true));

            try
            {

                SetWorkStatus($"updating photo library");

                if (folderViewList == null) // quick scan first (if available)
                {
                    var quickScan = new IndexUpdateInfo() { IsFull = false };
                    foreach (var item in GetBuiltInLibraryFolders())
                        ListDir(() => CancelIndex, quickScan, item, true); // scan changes in editor dir
                    FileRecordProvider?.Invoke(quickScan);
                    if (quickScan.FileList.Count > 0)
                        IndexBatch(quickScan, startTime, cfg.ForceRefreshDisplayAfterIndex);
                }

                if (!cfg.QuickScanOnly)
                {
                    var newFiles = new IndexUpdateInfo() { IsFull = true, AdditionalImageDataOut = DB.ExportAdditionalImageData() };
                    if (folderViewList != null)
                        newFiles.FileList.AddRange(folderViewList.Where(x => !x.IsIndexed()));
                    else
                    {
                        var builtInFolders = GetBuiltInLibraryFolders();
                        foreach (var item in builtInFolders)
                            ListDir(() => CancelIndex, newFiles, item, true); // prevent deletes from editor dir
                        foreach (var storeDir in DB.Settings.Dirs)
                            ListDir(() => CancelIndex, newFiles, storeDir, true, true);

                        if (!CancelIndex)
                            FileRecordProvider?.Invoke(newFiles);

                        if (!CancelIndex)
                            DB.RemoveMissingSources(newFiles.FileList, ProtectUnreadableFolders, builtInFolders);
                    }

                    IndexBatch(newFiles, startTime, cfg.ForceRefreshDisplayAfterIndex);
                }

                cfg.RunAfterIndex?.Invoke();

                SetWorkStatus(cfg.DoneMessage, true);
            }
            catch (Exception e) { Utils.LogError(e); }
            finally
            {
                done = true;
                Task.Run(() =>
                {
                    onIndexingTask.Wait();
                    OnIndexing?.Invoke(false);
                }).DieOnError();
            }

            GC.Collect();
        }

        private void IndexBatch(IndexUpdateInfo indexInfo, DateTime startTime, bool forceRefresh) // TODO: DUPLICATES (inbox)!!!!
        {
            var files = indexInfo.FileList;
            _indexCounter = 0;
            int totalCounter = 0;
            int totalCount = files.Count;
            var tsSave = DateTime.UtcNow;
            var refreshTime = 3_000;

            var sources = files.SelectMany(x => x.Src.Select(x => x.D)).Distinct().Select(x => DB.Directories.GetValue(x).SourceId).Distinct().ToList();

            files.Sort((x, y) => x.DT.CompareTo(y.DT));

            while (files.Count > 0 && !CancelIndex)
            {
                var idx = files.Count - 1;
                var item = files[idx];
                files.RemoveAt(idx);

                IndexFile(item);
                if (CancelIndex)
                    break;

                SetWorkStatus($"indexing: {totalCounter}/{totalCount} files");

                if (DB.IsDirty && (DateTime.UtcNow - tsSave).TotalMilliseconds > refreshTime)
                {
                    DB.SaveFilesIfDirty();
                    IndexUpdateRequest?.Invoke();
                    tsSave = DateTime.UtcNow;
                    refreshTime = Math.Min(refreshTime * 2, 60_000);
                }

                if (CheckFilesPrio)
                {
                    CheckFilesPrio = false;
                    try
                    {
                        files.Sort((x, y) => x.PrioTicks.CompareTo(y.PrioTicks));
                    }
                    catch (ArgumentException) { } // fails sometimes because prios are changed
                }

                totalCounter++;
            }

            while (_indexCounter > 0)
                Thread.Sleep(1);

            if (DB.IsDirty)
            {
                if (!CancelIndex)
                {
                    var ts = FileRecord.TimeTo(startTime);
                    foreach (var item in DB.SyncSources.Where(x => sources.Contains(x.Id)))
                        item.LastGoodSyncUTC = ts;
                    if (sources.Contains(0))
                        DB.FilesSettings.LastGoodLocalSyncUTC = ts;
                }

                DB.SaveFilesIfDirty();
                forceRefresh = true;
            }

            if (indexInfo.AdditionalImageDataInString.Count > 0)
            {
                var srcLookup = DB.ListFileSources();
                foreach (var item in indexInfo.AdditionalImageDataInString)
                    if (srcLookup.TryGetValue((DB.Directories.GetIndex(new DirectoryRecord() { Directory = item.Item1 }, noAdd: true), item.Item2.ToLowerInvariant()), out var sig))
                        indexInfo.AddImageData(sig, item.Item3);
            }

            if (indexInfo.AdditionalImageDataIn.Count > 0)
                forceRefresh |= DB.ImportAdditionalImageData(indexInfo.AdditionalImageDataIn);

            if (!CancelIndex && forceRefresh)
            {
                IndexUpdateRequest?.Invoke();
            }

            //SetWorkStatus();
            _firstIndexFinished.TrySetResult();
        }

        private void IndexFile(FileRecord file)
        {
            if (file.Sig != 0 && DB.TryGetFileBySignature(file.Sig) != null) // check exists by signature
            {
                DB.AddFile(file);
                return;
            }

            if (file.Src.All(s => DB.TryGetFileBySource(new FileRecordSourceKey(s))?.GetFlag(FileRecordFlags.FlagMetadataRefreshRequired) == false)) // check exists by path
                return;

            if (file.Src.Any(x => DB.Directories.GetValue(x.D).SourceId == 0)) // local
            {
                var fileData = GetFullImage(file);
                if (fileData == null)
                    return; // not found

                file.Sig = ComputeSignature(fileData.Data.AsSpan());
                file.FillExifFields(fileData.Data, DB);

                if (DB.TryGetFileBySignature(file.Sig) != null) // check if already there
                {
                    DB.AddFile(file);
                    return;
                }

                Interlocked.Increment(ref _indexCounter);
                PriorityScheduler.RunLowPrioTask(() =>
                {
                    try
                    {
                        IndexFileCpu(file, fileData);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _indexCounter);
                    }
                });
                return;
            }
            else
            {
                var (micro, thumb) = IndexProvider(file);
                SaveThumbnails(file, thumb, micro);
                return;
            }
        }

        public FullImageData GetFullImage(FileRecord rec)
        {
            var res = new FullImageData() { File = rec, Orientation = DB.GetOrientation(rec) };
            foreach (var src in rec.Src)
            {
                var dir = DB.Directories.GetValue(src.D);
                if (dir.SourceId == 0)
                {
                    var dt = DateTime.UtcNow;
                    res.FullPath = dir.CombinePath(src);
                    res.Directory = dir;
                    res.LoadedSource = src;

                    res.Data = SKData.Create(res.FullPath);
                    dt.PrintUtcMs($"skdata {rec.Sig}", 100);
                    if (res.Data != null)
                        break;
                }
            }

            if (res.Data == null)
            {
                var dt = DateTime.UtcNow;
                FileContentProvider?.Invoke(res);
                dt.PrintUtcMs($"custom {rec.Sig}", 1500);
            }

            if (res.Data != null)
            {
                var span = res.Data.Span;
                long t = 0;
                for (int i = 0; i < span.Length; i += 128)
                    t += span[i];
            }
            else
                return null;

            return res;
        }

        public static ulong ComputeSignature(ReadOnlySpan<byte> readOnlySpan)
        {
            var exif = new ExifReader(readOnlySpan); //skip exif (changed on android without location rights)
            if (exif.Found && exif.EndOfExif > 0 && exif.EndOfExif < readOnlySpan.Length / 2) // sanity
                readOnlySpan = readOnlySpan[exif.EndOfExif..];

            using var md5 = MD5.Create();
            UInt128 res = 0;
            var dest = MemoryMarshal.Cast<UInt128, byte>(MemoryMarshal.CreateSpan(ref res, 1));
            md5.TryComputeHash(readOnlySpan, dest, out var bw);
            if (bw != 16)
                throw new Exception("hash failed");

            return (ulong)res;
        }

        public Func<FileRecord, (byte[] micro, byte[] thumb)> IndexProvider;

        const int MicroThumbnailSize = 64;
        void IndexFileCpu(FileRecord file, FullImageData skData)
        {
            byte[] buf, microBuf;

            try
            {
                ImageLoader.DecodeAndResizeImage(skData, ImageLoader.ScaleToMinSide(DB.Settings.ThumbnailSize, 0));

                if (skData.Bitmap == null)
                    buf = microBuf = null;
                else
                {
                    file.W = skData.Width;
                    file.H = skData.Height;

                    using var data = skData.Bitmap.Encode(SKEncodedImageFormat.Jpeg, 85);
                    buf = data.ToArray();

                    using var tmpImg = SKImage.FromBitmap(skData.Bitmap);
                    using var micro = ScaleImage(tmpImg, MicroThumbnailSize, new SKSizeI(file.W, file.H));
                    using var data2 = micro.Encode(SKEncodedImageFormat.Jpeg, 85);
                    microBuf = data2.ToArray();
                }
            }
            finally
            {
                skData.Dispose();
            }

            SaveThumbnails(file, buf, microBuf);
        }

        internal static SKBitmap ScaleImage(SKImage largeImage, int size, SKSizeI origSize)
        {
            if (origSize.Height == 0 || origSize.Width == 0)
                throw new Exception("bas size");

            var bmp = new SKBitmap(Math.Min(size, origSize.Width), Math.Min(size, origSize.Height), largeImage.ColorType, largeImage.AlphaType);
            using (var canvas = new SKCanvas(bmp))
            {
                var dstRect = bmp.Info.Rect;
                var imgMinSize = Math.Min(largeImage.Width, largeImage.Height);
                var srcSize = size > imgMinSize ? new SKSize(imgMinSize, imgMinSize)
                    : new SKSize(Math.Max(imgMinSize, dstRect.Width), Math.Max(imgMinSize, dstRect.Height));
                var srcRect = SKRect.Create((largeImage.Width - srcSize.Width) / 2, (largeImage.Height - srcSize.Height) / 2, srcSize.Width, srcSize.Height);
                using var paint = new SKPaint() { IsAntialias = true };
                canvas.DrawImage(largeImage, srcRect, dstRect, Lib.BaseView.HighQualitySampling, paint);
                canvas.Flush();
            }

            return bmp;
        }

        object _saveLock = new object();

        private void SaveThumbnails(FileRecord file, byte[] buf, byte[] microBuf)
        {
            if (microBuf == null || buf == null)
            {
                file.IndexFailed = true;
                return;
            }

            if (!CancelIndex)
                lock (_saveLock)
                {
                    file.T = DB.Thumbnails.CurrentPos;
                    DB.Thumbnails.Write(buf);
                    file.TS = buf.Length;

                    file.MT = DB.MicroThumbnails.CurrentPos;
                    DB.MicroThumbnails.Write(microBuf);
                    file.MTS = microBuf.Length;

                    DB.AddFile(file);
                }
        }
    }
}
