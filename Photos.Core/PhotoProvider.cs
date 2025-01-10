using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Photos.Lib;
using System.Reflection;
using Photos.Core.StoredTypes;
using System.Threading.Tasks;

namespace Photos.Core
{
    public class IndexTaskConfig
    {
        public bool QuickScanOnly, ForceMetaRefresh;
        public Action RunAfterIndex;
        public string DoneMessage;
    }
    public class PhotoProvider : IDisposable
    {
        public event Action<bool> OnIndexing;

        public const int LargeThumbnailSize = 536;
        public const int MediumThumbnailSize = 352;
        bool _forceRefreshAfterIndex;

        public FilterService FilterService { get; private set; }

        int _idx;
        public int Idx
        {
            get => _idx;
            set
            {
                if (_idx != value)
                {
                    _idx = value;
                    ClampIdx(ref _idx);
                    IndexChanged?.Invoke();
                }
            }
        }

        public void ClampIdx(ref int idx)
        {
            var size = _displayOrder.Count;
            if (size == 0)
                idx = 0;
            else
            {
                while (idx < 0)
                    idx += size;
                idx %= size;
            }
        }

        public Task FirstIndexTask => _firstIndexFinished.Task;
        TaskCompletionSource _firstIndexFinished = new();
        public event Action IndexChanged;
        Task indexTask, initTask, backgroundLoaderTask;
        DateTime indexTaskStart;
        volatile bool _shutdown;
        public bool ShutdownRequested => _shutdown;

        readonly object _lock = new();
        public Storage DB { get; private set; }
        public bool FolderLibraryMode { get; private set; }
        public readonly bool ProtectUnreadableFolders;

        const int MinThumbnailsCacheSize = 5;
        Cache<ulong, SKImage> _scaledThumbnails = new(MinThumbnailsCacheSize) { OnDispose = x => x.Dispose() };
        Cache<ulong, SKImage> _fullSizeThumbnails = new(24) { OnDispose = x => x.Dispose() };
        Cache<ulong, SKImage> _microCache = new(MinThumbnailsCacheSize) { OnDispose = x => x.Dispose() };

        List<FileRecord> _displayOrder;
        public List<FileGroup> DispalyGroups { get; private set; }

        List<FileRecord> _folderViewList;
        public bool IsSingleFolderMode => _folderViewList != null;

        public void ClearIndex()
        {
            RestartBackgroundTasks(() =>
            {
                DB.ClearFiles();
                _fullSizeThumbnails.Clear();
                _scaledThumbnails.Clear();
                _microCache.Clear();
                RefreshDisplayOrder();
            });
        }

        void SettingsUpdate(SettingsRecord oldV, SettingsRecord newV)
        {
            if (oldV.ThumbnailDrawSize != newV.ThumbnailDrawSize)
            {
                _scaledThumbnails.Clear();
                _scaledThumbnails.Limit = MinThumbnailsCacheSize;
            }
        }

        public PhotoProvider(string dbPath, string editorDir, string fname = null, bool protectUnreadableFolders = true, bool folderLibraryMode = true, Action<PhotoProvider> setupEvents = null)
        {
            EditorDirectory = Path.Combine(editorDir, Utils.LatinAppName);
            FolderLibraryMode = folderLibraryMode;
            ProtectUnreadableFolders = protectUnreadableFolders;
            DB = new Storage(dbPath);
            Utils.TraceEnabled = () => DB.Settings.DevMode;
            Utils.ErrorReportOptOut = () => DB.Settings.ErrorOptOut;

            DB.OnSettingsUpdate += SettingsUpdate;
            FilterService = new(DB);
            FilterService.FilterUpdate += () => RefreshDisplayOrder(true);

            _errorImage = SKImage.FromBitmap(_errorThumbnail);

            SetFolderMode(fname);

            setupEvents?.Invoke(this);

            initTask = Task.Run(() =>
            {
                DB.LoadFiles(_folderViewList);
                RefreshFolderViewList();

                RunBackgroundTasks(new IndexTaskConfig());
                RefreshDisplayOrder();
            });

            initTask.DieOnError();
        }

        public async Task<string> DeleteSourceFile(string fullPath, FileRecord file)
        {
            string res;
            if (DeleteSourceFileImpl == null)
            {
                try
                {
                    File.Delete(fullPath);
                    res = null;
                }
                catch (Exception ex)
                {
                    res = ex.Message;
                }
            }
            else
                res = await DeleteSourceFileImpl(fullPath);

            if (res == null && IsSingleFolderMode)
                lock (_lock)
                    _folderViewList = _folderViewList.Where(x => x != file).ToList();

            return res;
        }

        public volatile int MinPreloadImage, MaxPreloadImage;
        public event Action OnBackgroundWorker;
        void BackgroundLoader()
        {
            try
            {
                while (!_shutdown)
                {
                    var dt = DateTime.UtcNow;
                    PreloadScaledImages();

                    OnBackgroundWorker?.Invoke();

                    if ((DateTime.UtcNow - dt).TotalMilliseconds < 3) // nothing was loaded
                        Thread.Sleep(20);
                }
            }
            catch (Exception ex)
            {
                Utils.LogError(ex);
                Environment.Exit(1);
            }
        }

        private void PreloadScaledImages()
        {
            var startDisplayOrder = _displayOrder;
            if (_displayOrder.Count > 0)
            {
                var maxImgIdx = Math.Clamp(MaxPreloadImage, 0, startDisplayOrder.Count - 1);
                var minImgIdx = Math.Clamp(MinPreloadImage, 0, startDisplayOrder.Count - 1);
                var winSize = maxImgIdx - minImgIdx + 1;

                var leftLimit = Math.Max(0, minImgIdx - winSize);
                var rightLimit = Math.Min(startDisplayOrder.Count - 1, maxImgIdx + winSize);

                if (leftLimit <= rightLimit && rightLimit - leftLimit < 1000 && winSize > 0)
                {
                    _scaledThumbnails.Limit = Math.Max(_scaledThumbnails.Limit, (rightLimit - leftLimit) * 2);
                    _microCache.Limit = _scaledThumbnails.Limit * 4;

                    for (int i = minImgIdx; i <= rightLimit && MaxPreloadImage == maxImgIdx && _displayOrder == startDisplayOrder; i++)
                        GetScaledThumbnail(startDisplayOrder[i], false);

                    for (int i = minImgIdx - 1; i >= leftLimit && MaxPreloadImage == maxImgIdx && _displayOrder == startDisplayOrder; i--)
                        GetScaledThumbnail(startDisplayOrder[i], false);
                }
            }
        }

        public void SaveFilesIfDirty()
        {
            lock (_lock)
                if (DB.IsDirty)
                    DB.SaveFiles();
        }

        private void RefreshFolderViewList()
        {
            lock (_lock)
                if (_folderViewList != null)
                    _folderViewList = _folderViewList.Select(x => x.IsIndexed() ? x : DB.TryGetFileBySource(new FileRecordSourceKey(x.Src.Single())) ?? x).ToList(); // use records with metadata from db
        }

        string _lastFolderViewDir;
        private void SetFolderMode(string fname, bool isDir = false)
        {
            _folderViewList = null;

            if (fname != null)
            {
                var tmp = new List<FileRecord>();
                _lastFolderViewDir = isDir ? fname : Path.GetDirectoryName(fname);
                ListDir(tmp, _lastFolderViewDir, false);
                _folderViewList = tmp.OrderBy(x => x.Src[0].FN).ToList();
                RefreshFolderViewList();

                if (_folderViewList.Count == 0) // no images in the directory, fallback to gallery
                    _folderViewList = null;
            }

            FilterService.PersistentFilters = _folderViewList == null;

            RefreshDisplayOrder();
            if (_folderViewList != null)
            {
                if (isDir)
                    Idx = 0;
                else
                {
                    var fn = Path.GetFileName(fname);
                    var idx = _displayOrder.FindIndex(x => x.Src.Any(x => x.FN.Equals(fn, StringComparison.InvariantCultureIgnoreCase)));
                    Idx = idx >= 0 ? idx : 0;
                }
            }
        }

        private void RunBackgroundTasks(IndexTaskConfig cfg)
        {
            indexTaskStart = DateTime.UtcNow;

            indexTask = PriorityScheduler.RunBackgroundThread(() => IndexDirs(cfg), ThreadPriority.Lowest, "index");
            backgroundLoaderTask = PriorityScheduler.RunBackgroundThread(BackgroundLoader, ThreadPriority.BelowNormal, "loader");
        }

        //const int WorkStatusDelay = 1000;
        public Action<Action> ApplyDisplayOrderUpdate = x => x();
        string _workStatus;
        public string WorkStatus => /*_workStatus == null || (DateTime.UtcNow - indexTaskStart).TotalMilliseconds < WorkStatusDelay ? null :*/ _workStatus;

        public void ListDir(IList<FileRecord> res, string dir, bool recurse)
        {
            try
            {
                var files = new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions() { RecurseSubdirectories = recurse });
                foreach (var x in files)
                {
                    if (ImageLoader.KnownEndings.Contains(x.Extension.ToLowerInvariant()))
                        FileRecord.AddFromInfo(res, x, DB);
                    if (_shutdown)
                        return;
                }

            }
            catch { }
        }

        public IReadOnlyList<string> GetDirs() => DB.Settings.Dirs;
        public void AddDir(string path)
        {
            if (DB.Settings.Dirs.Contains(path))
                return;

            DB.Settings.Dirs = DB.Settings.Dirs.AddToArr(path);
            DB.SaveSettings();

            _forceRefreshAfterIndex = true;
            RestartBackgroundTasks(null);
        }
        public void RemoveDir(string d)
        {
            DB.Settings.Dirs = DB.Settings.Dirs.Where(x => x != d).ToArray();
            DB.SaveSettings();

            RefreshDisplayOrder();
            RestartBackgroundTasks(null);
        }

        public void RemoveSrc(int id)
        {
            RestartBackgroundTasks(() =>
            {
                DB.RemoveSource(id);
                DB.SaveFiles();
                RefreshDisplayOrder();
            });

        }

        public void AddSrc(SyncSource src)
        {
            lock (_lock)
            {
                var id = DB.SyncSources.Select(x => x.Id).Concat(new[] { 0 }).Max() + 1;
                src.Id = id;
                DB.SyncSources = DB.SyncSources.AddToArr(src);
                DB.IsDirty = true;
            }

            SaveFilesIfDirty();
            UpdateLibraryMaybe();
        }

        public event Action<IList<FileRecord>, bool> FileRecordProvider;
        public Action<FullImageData> FileContentProvider;
        public Func<string, Task<string>> DeleteSourceFileImpl { private get; set; }

        public void UpdateLibraryMaybe(IndexTaskConfig cfg = null)//(bool quick, bool forceMetaRefresh = false)
        {
            cfg ??= new IndexTaskConfig();

            var indexRunning = indexTask?.IsCompleted == true;
            if (initTask.IsCompleted)
            {
                cfg.QuickScanOnly = cfg.QuickScanOnly && !indexRunning;
                RestartBackgroundTasks(() =>
                {
                    if (cfg.ForceMetaRefresh)
                        foreach (var item in DB.ListFiles())
                            item.SetFlag(FileRecordFlags.FlagMetadataRefreshRequired);
                }, cfg);
            }
        }

        private void IndexDirs(IndexTaskConfig cfg)
        {
            var startTime = DateTime.UtcNow;
            bool done = false;
            var onIndexingTask = Utils.WaitWhile(() => OnIndexing == null && !done).ContinueWith(x => OnIndexing?.Invoke(true));

            try
            {

                SetWorkStatus($"updating photo library");

                if (_folderViewList == null) // quick scan first (if available)
                {
                    var quickScan = new List<FileRecord>();
                    foreach (var item in GetBuiltInLibraryFolders())
                        ListDir(quickScan, item, true); // scan changes in editor dir
                    FileRecordProvider?.Invoke(quickScan, false);
                    if (quickScan.Count > 0)
                        IndexBatch(quickScan, startTime);
                }

                if (!cfg.QuickScanOnly)
                {
                    var newFiles = new List<FileRecord>();
                    if (_folderViewList != null)
                        newFiles.AddRange(_folderViewList.Where(x => !x.IsIndexed()));
                    else
                    {
                        var builtInFolders = GetBuiltInLibraryFolders();
                        foreach (var item in builtInFolders)
                            ListDir(newFiles, item, true); // prevent deletes from editor dir
                        foreach (var storeDir in DB.Settings.Dirs)
                            ListDir(newFiles, storeDir, true);

                        if (!_shutdown)
                            FileRecordProvider?.Invoke(newFiles, true);

                        if (!_shutdown)
                            DB.RemoveMissingSources(newFiles, ProtectUnreadableFolders, builtInFolders);
                    }

                    IndexBatch(newFiles, startTime);
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

            _forceRefreshAfterIndex = false;
        }

        public void SetWorkStatus(string s, bool done = false) => _workStatus = done ? s : $"...{s}...";

        private void IndexBatch(List<FileRecord> files, DateTime startTime) // TODO: DUPLICATES (inbox)!!!!
        {
            _indexCounter = 0;
            int totalCounter = 0;
            int totalCount = files.Count;
            var tsSave = DateTime.UtcNow;
            var refreshTime = 3_000;

            var sources = files.SelectMany(x => x.Src.Select(x => x.D)).Distinct().Select(x => DB.Directories.GetValue(x).SourceId).Distinct().ToList();

            files.Sort((x, y) => x.DT.CompareTo(y.DT));

            while (files.Count > 0 && !_shutdown)
            {
                var idx = files.Count - 1;
                var item = files[idx];
                files.RemoveAt(idx);
                
                IndexFile(item);
                if (_shutdown)
                    break;

                SetWorkStatus($"indexing: {totalCounter}/{totalCount} files");

                if (DB.IsDirty && (DateTime.UtcNow - tsSave).TotalMilliseconds > refreshTime)
                {
                    SaveFilesIfDirty();
                    RefreshFolderViewList();
                    RefreshDisplayOrder();
                    tsSave = DateTime.UtcNow;
                    refreshTime = Math.Min(refreshTime * 2, 60_000);
                }

                if (_checkPrio)
                {
                    _checkPrio = false;
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

            var refresh = _forceRefreshAfterIndex;
            if (DB.IsDirty)
            {
                if (!_shutdown)
                {
                    var ts = FileRecord.TimeTo(startTime);
                    foreach (var item in DB.SyncSources.Where(x => sources.Contains(x.Id)))
                        item.LastGoodSyncUTC = ts;
                    if (sources.Contains(0))
                        DB.FilesSettings.LastGoodLocalSyncUTC = ts;
                }

                SaveFilesIfDirty();
                refresh = true;
            }

            if (!_shutdown && refresh)
            {
                RefreshFolderViewList();
                RefreshDisplayOrder();
            }

            //SetWorkStatus();
            _firstIndexFinished.TrySetResult();
        }
       
        public void RefreshDisplayOrder(bool skipFilters = false)
        {
            lock (_lock)
            {
                Dictionary<int, int> dirsOrder = null;

                var files = GetLibraryFiles(out var dirs);
                TotalCount = files.Count;

                if (!skipFilters)
                    FilterService.RefreshFilters(files, dirs.Where(x => x != null), initTask != null);

                FilterService.FilterFiles(files);

                if (FilterService.CurrentSortMode == SortModeEnum.Filename)
                {
                    var dirArr = dirs.Select((x, i) => (x, i)).Where(x => x.x != null).OrderBy(x => x.x.Directory).Select(x => x.i).ToArray();
                    dirsOrder = new();
                    for (int i = 0; i < dirArr.Length; i++)
                        dirsOrder[dirArr[i]] = i;

                    foreach (var item in files)
                    {
                        item.SrcForSorting = item.Src.FirstOrDefault(x => dirsOrder.ContainsKey(x.D)); // TODO: causes errors where First finds no elements???!!?!?!?
                        if (item.SrcForSorting == default)
                            throw new Exception($"dir not found {item.Src.Length};{_folderViewList==null};{FolderLibraryMode};{dirsOrder.Count};{item.Src.FirstOrDefault().D}");
                    }
                }

                IEnumerable<FileRecord> ordered = FilterService.CurrentSortMode switch
                {
                    SortModeEnum.Date => files.OrderByDescending(x => x.DT),
                    SortModeEnum.Filename => _folderViewList != null ? files : files.OrderBy(x => dirsOrder[x.SrcForSorting.D]).ThenBy(x => x.SrcForSorting.FN),
                    _ => throw new NotImplementedException(),
                };
                var displayOrder = ordered.ToList();

                long lastKey = -1;
                var dispalyGroups = new List<FileGroup>();
                FileGroup group = null;
                for (int i = 0; i < displayOrder.Count; i++)
                {
                    var item = displayOrder[i];
                    long key = FilterService.CurrentSortMode switch
                    {
                        SortModeEnum.Date => item.DT / 1000000,
                        SortModeEnum.Filename => item.SrcForSorting.D, // TODO: show multiple times in each dir??!!!!
                        _ => throw new NotImplementedException(),
                    };

                    if (key != lastKey || group == null)
                    {
                        group = new()
                        {
                            StartIdx = i,
                            Key = FilterService.CurrentSortMode switch
                            {
                                SortModeEnum.Date => FileRecord.TimeFrom(key * 1000000).ToLongDateString(),
                                SortModeEnum.Filename => DB.Directories.GetValue((int)key).Directory, // TODO: show multiple times in each dir??!!!!
                                _ => throw new NotImplementedException(),
                            }
                        };
                        dispalyGroups.Add(group);
                        lastKey = key;
                    }

                    group.Count++;
                }

                ApplyDisplayOrderUpdate(() =>
                {

                    _displayOrder = displayOrder;
                    DispalyGroups = dispalyGroups;
                    ClampIdx(ref _idx);
                    DisplayedCount = _displayOrder.Count;
                });
            }
        }

        public List<FileRecord> GetLibraryFiles(out DirectoryRecord[] dirs, bool realLibOnly = false)
        {
            if (_folderViewList != null && !realLibOnly)
            {
                foreach (var item in DB.Directories.ToArray().Where(x => x != null))
                    item.IsInLib = false;
                
                var idx = DB.Directories.GetIndex(new DirectoryRecord() { Directory = _lastFolderViewDir });
                dirs = new DirectoryRecord[idx + 1];
                dirs[idx] = DB.Directories.GetValue(idx);
                dirs[idx].IsInLib = true;
                return _folderViewList.ToList();
            }

            IEnumerable<FileRecord> files = DB.ListFiles().Where(x => x.Src.Length > 0);
            dirs = DB.Directories.ToArray();

            if (FolderLibraryMode)
            {
                var allDirs = GetBuiltInLibraryFolders();
                allDirs.AddRange(GetDirs());

                for (int i = 0; i < dirs.Length; i++) // set IsInLib
                {
                    var item = dirs[i];
                    if (item != null)
                    {
                        item.IsInLib = item.SourceId != 0 || allDirs.Any(d => item.IsSubdirectoryOf(d));
                        if (!item.IsInLib)
                            dirs[i] = null;
                    }
                }

                files = files.Where(x => x.Src.Any(s => DB.Directories.GetValue(s.D).IsInLib));
            }
            else
                foreach (var item in dirs.Where(x => x != null))
                    item.IsInLib = true;

            return files.ToList();
        }

        volatile int _indexCounter;
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
                    file.W = skData.OriginalInfo.Width;
                    file.H = skData.OriginalInfo.Height;

                    using var data = skData.Bitmap.Encode(SKEncodedImageFormat.Jpeg, 85);
                    buf = data.ToArray();

                    using var tmpImg = SKImage.FromBitmap(skData.Bitmap);
                    using var micro = ScaleImage(tmpImg, MicroThumbnailSize, file);
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

        private void SaveThumbnails(FileRecord file, byte[] buf, byte[] microBuf)
        {
            if (microBuf == null || buf == null)
            {
                file.IndexFailed = true;
                return;
            }

            lock (_lock)
                if (!_shutdown)
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

        const int MicroThumbnailSize = 64;
        static SKImage _emptyThumbnail;
        private volatile bool _checkPrio;
        static SKBitmap ToThumbnail(SKPath p) => IconStore.ToBitmap(128, 128, 32, (p, null));
        public SKImage GetScaledThumbnail(FileRecord rec, bool fast = false)
        {
            SKImage res = _scaledThumbnails.Get(rec.Sig);

            if (res == null && !rec.IsIndexed() && !rec.IndexFailed) // still indexing
            {
                rec.PrioTicks = DateTime.UtcNow.Ticks;
                _checkPrio = true;
            }

            if (!rec.IsIndexed() || rec.IndexFailed) // will be fast
                return GetFullThumbnail(rec);

            if (res == null && !fast && initTask.IsCompleted)
            {
                var largeImage = GetFullThumbnail(rec);
                using var bmp = ScaleImage(largeImage, DB.Settings.ThumbnailDrawSize, rec);
                res = SKImage.FromBitmap(bmp);
                _scaledThumbnails.Put(rec.Sig, res);
            }

            return res;
        }

        private static SKBitmap ScaleImage(SKImage largeImage, int size, FileRecord orig)
        {
            if (orig.H == 0 || orig.W == 0)
                throw new Exception("bas size");

            var bmp = new SKBitmap(Math.Min(size, orig.W), Math.Min(size, orig.H), largeImage.ColorType, largeImage.AlphaType);
            using (var canvas = new SKCanvas(bmp))
            {
                var dstRect = bmp.Info.Rect;
                var imgMinSize = Math.Min(largeImage.Width, largeImage.Height);
                var srcSize = new SKSize(Math.Max(imgMinSize, dstRect.Width), Math.Max(imgMinSize, dstRect.Height));
                var srcRect = SKRect.Create((largeImage.Width - srcSize.Width) / 2, (largeImage.Height - srcSize.Height) / 2, srcSize.Width, srcSize.Height);
                using var paint = new SKPaint() { IsAntialias = true };
                canvas.DrawImage(largeImage, srcRect, dstRect, BaseView.HighQualitySampling, paint);
                canvas.Flush();
            }

            return bmp;
        }

        readonly SKBitmap _errorThumbnail = ToThumbnail(IconStore.EmojiDizzy);
        readonly SKImage _errorImage;
        public SKImage GetFullThumbnail(FileRecord file)
        {
            if (file.IndexFailed)
                return _errorImage;

            var res = _fullSizeThumbnails.Get(file.Sig);
            if (res == null && file.IsIndexed() && initTask.IsCompleted)
            {
                var buf = DB.Thumbnails.Read(file.T, file.TS);

                if (buf != null && buf.Length != 0)
                    using (var data = SKData.CreateCopy(buf))
                        res = ImageLoader.DecodeThumbnail(data, GetOrientation(file));

                res ??= SKImage.FromBitmap(_errorThumbnail);
                _fullSizeThumbnails.Put(file.Sig, res);
            }

            res ??= (_emptyThumbnail ??= SKImage.FromBitmap(ToThumbnail(IconStore.HourglassSplit)));
            return res;
        }

        public string GetLocalPath(FileRecord rec)
        {
            foreach (var src in rec.Src)
            {
                var dir = DB.Directories.GetValue(src.D);
                if (dir.IsLocal())
                {
                    var fullPath = dir.CombinePath(src);
                    if (fullPath != null && File.Exists(fullPath))
                        return fullPath;
                }
            }

            return null;
        }

        public FullImageData GetFullImage(FileRecord rec)
        {
            var res = new FullImageData() { File = rec, Orientation = GetOrientation(rec) };
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

        public (string name, string path, byte[] data) GetImageForSharing(int idx)
        {
            var file = GetFile(idx);
            using var data = GetFullImage(file);
            var fn = file.Src[0].FN;
            if (data != null && DB.Directories.GetValue(data.LoadedSource.D).IsLocal()) // local source
                return (fn, data.LoadedSource.GetFullPath(this).osPath, null);
            else if (data != null) // remote source
                return (fn, null, data.Data.ToArray());

            return (Path.ChangeExtension(fn, ".jpg"), null, DB.Thumbnails.Read(file.T, file.TS)); // failed to load => return thumbnail
        }

        public List<FileRecord> GetFiles(IEnumerable<int> idxs)
        {
            var res = new List<FileRecord>();

            if (_displayOrder.Count > 0)
                foreach (var idx in idxs)
                {
                    var candIdx = idx;
                    ClampIdx(ref candIdx);
                    res.Add(_displayOrder[candIdx]);
                }

            return res;
        }

        public FileRecord GetFile(int idx)
        {
            return _displayOrder[idx];
        }

        public void Dispose()
        {
            TerminateBackgroundTasks();

            SaveFilesIfDirty();
            DB.Dispose();
            _microCache.Clear();
        }        

        public int DisplayedCount, TotalCount;
        public bool IsInitRunning => !initTask.IsCompleted;
        internal bool IsIndexEmpty => DB.Count == 0 && initTask.IsCompleted;

        public event Action PhotoSelected;
        public void SelectPhoto() => PhotoSelected?.Invoke();
        public void OpenFile(string commFile)
        {
            var isDir = Directory.Exists(commFile);
            var isFile = File.Exists(commFile);
            if (!isFile && !isDir)
                return;

            RestartBackgroundTasks(() => SetFolderMode(commFile, isDir));

            if (!isDir)
                PhotoSelected?.Invoke();
        }

        internal void CloseSingleFolderMode()
        {
            RestartBackgroundTasks(() => SetFolderMode(null));
        }

        object _restartLock = new object();
        private void RestartBackgroundTasks(Action act, IndexTaskConfig cfg = null)
        {
            cfg ??= new IndexTaskConfig();
            lock (_restartLock)
            {
                TerminateBackgroundTasks(); // deadlock if we aquire _lock
                lock (_lock)
                {
                    try { act?.Invoke(); }
                    finally { RunBackgroundTasks(cfg); }
                }
            }
        }

        void TerminateBackgroundTasks()
        {
            initTask.Wait();
            _shutdown = true;
            indexTask?.Wait();
            backgroundLoaderTask?.Wait();
            _shutdown = false;
        }

        internal SKImage GetMicroThumbnail(FileRecord file)
        {
            if (!file.IsIndexed())
                return null;

            var img = _microCache.Get(file.Sig);

            if (img == null)
            {
                var buf = DB.MicroThumbnails.Read(file.MT, file.MTS);

                if (buf.Length != 0)
                {
                    img = SKImage.FromEncodedData(buf); // SKBitmap.Decode(buf) fails on android net9

                    var o = GetOrientation(file);
                    if (o != SKEncodedOrigin.TopLeft)
                    {
                        var bmp = SKBitmap.FromImage(img);
                        img.Dispose();
                        img = null;

                        try
                        {
                            ImageLoader.FixOrientation(ref bmp, o);
                            img = SKImage.FromBitmap(bmp);
                        }
                        finally { bmp?.Dispose(); }
                    }

                    if (img != null)
                        _microCache.Put(file.Sig, img);
                }
            }

            return img;
        }

        public SKEncodedOrigin GetOrientation(FileRecord file)
        {
            var o = DB.GetAdditionalImageData(file).Orientation;
            return o == 0 ? SKEncodedOrigin.TopLeft : (SKEncodedOrigin)o;
        }

        internal void InvalidateThumbnail(FileRecord file)
        {
            if (!file.IsIndexed())
                throw new ArgumentException();

            _microCache.Remove(file.Sig);
            _scaledThumbnails.Remove(file.Sig);
            _fullSizeThumbnails.Remove(file.Sig);
        }

        public readonly string EditorDirectory;
        public event Action<List<string>> RegisterBuiltInDirs;
        List<string> GetBuiltInLibraryFolders()
        {
            var res = new List<string> { EditorDirectory };
            RegisterBuiltInDirs?.Invoke(res);
            return res;
        }

    }

    public class FileGroup
    {
        public int StartIdx; public int Count; public string Key;
    }

    public class FullImageData : IDisposable
    {
        public FileRecord File;

        public SourceRecord LoadedSource;
        public DirectoryRecord Directory;
        public string FullPath;
        public SKData Data;

        public SKBitmap Bitmap;
        public SKImageInfo OriginalInfo;
        public SKEncodedOrigin Orientation;

        public void Dispose()
        {
            Data?.Dispose();
            Bitmap?.Dispose();
        }
    }

}
