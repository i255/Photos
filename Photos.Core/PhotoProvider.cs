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
using System.Collections.Concurrent;

namespace Photos.Core
{
    class UpdateOp
    {
        public Action UILockAction, PreLockAction;
    }

    public class PhotoProvider : IDisposable
    {
        public PhotoIndexer Indexer;

        public const int LargeThumbnailSize = 536;
        public const int MediumThumbnailSize = 352;

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

        ConcurrentQueue<UpdateOp> SystemUpdates = new();
        public event Action IndexChanged;
        Task indexTask, initTask, backgroundLoaderTask;
        volatile bool _shutdown;
        public bool ShutdownRequested => _shutdown;

        readonly object _lock = new();
        public Storage DB { get; private set; }
        public bool FolderLibraryMode { get; private set; }

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
                RefreshDisplayOrderBackgroundOnly(false);
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
            
            FolderLibraryMode = folderLibraryMode;
            
            DB = new Storage(dbPath);
            Utils.TraceEnabled = () => DB.Settings.DevMode;
            Utils.ErrorReportOptOut = () => DB.Settings.ErrorOptOut;

            DB.OnSettingsUpdate += SettingsUpdate;
            Indexer = new PhotoIndexer(DB, protectUnreadableFolders, Path.Combine(editorDir, Utils.LatinAppName));

            FilterService = new(DB);
            FilterService.FilterUpdate += () => EnqueueRefreshDisplayOrder(true);

            Indexer.IndexUpdateRequest += () => {
                RefreshFolderViewList();
                EnqueueRefreshDisplayOrder();
            };

            _errorImage = SKImage.FromBitmap(_errorThumbnail);

            SetFolderModeBackgroundOnly(fname);

            setupEvents?.Invoke(this);

            initTask = Task.Run(() =>
            {
                DB.LoadFiles(_folderViewList);
                RefreshFolderViewList();
                RefreshDisplayOrderBackgroundOnly(false);

                RunIndexerTask(new IndexTaskConfig());
                backgroundLoaderTask = PriorityScheduler.RunBackgroundThread(BackgroundLoader, ThreadPriority.BelowNormal, "loader");
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
                    UpdateOp task = null;
                    while (SystemUpdates.TryDequeue(out task))
                    {
                        var finished = new TaskCompletionSource();
                        try
                        {
                            task.PreLockAction?.Invoke();
                            if (task.UILockAction != null)
                            {
                                RequestUILock(finished.Task).Wait();
                                task.UILockAction();
                            }
                        }
                        finally
                        {
                            finished.SetResult();
                        }
                    }

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

        private void RefreshFolderViewList()
        {
            lock (_lock)
                if (_folderViewList != null)
                    _folderViewList = _folderViewList.Select(x => x.IsIndexed() ? x : DB.TryGetFileBySource(new FileRecordSourceKey(x.Src.Single())) ?? x).ToList(); // use records with metadata from db
        }

        string _lastFolderViewDir;
        private void SetFolderModeBackgroundOnly(string fname, bool isDir = false)
        {
            _folderViewList = null;

            if (fname != null)
            {
                var tmp = new IndexUpdateInfo();
                _lastFolderViewDir = isDir ? fname : Path.GetDirectoryName(fname);
                Indexer.ListDir(null, tmp, _lastFolderViewDir, false);
                _folderViewList = tmp.FileList.OrderBy(x => x.Src[0].FN).ToList();
                RefreshFolderViewList();

                if (_folderViewList.Count == 0) // no images in the directory, fallback to gallery
                    _folderViewList = null;
            }

            FilterService.PersistentFilters = _folderViewList == null;

            RefreshDisplayOrderBackgroundOnly(false);
            if (_folderViewList != null)
            {
                if (isDir)
                    Idx = 0;
                else
                {
                    var fn = Path.GetFileName(fname);
                    var idx = _displayOrder.FindIndex(x => x.Src.Any(x => x.FN.Equals(fn, StringComparison.InvariantCultureIgnoreCase)));
                    Idx = idx >= 0 ? idx : 0;

                    PhotoSelected?.Invoke();
                }
            }
        }

        private void RunIndexerTask(IndexTaskConfig cfg)
        {
            if (indexTask != null && !indexTask.IsCompleted)
                throw new Exception("already running");
            indexTask = PriorityScheduler.RunBackgroundThread(() => Indexer.IndexDirs(cfg, _folderViewList), ThreadPriority.Lowest, "index");
        }

        //const int WorkStatusDelay = 1000;
        public event Action OnDisplayOrderUpdate;

        public IReadOnlyList<string> GetDirs() => DB.Settings.Dirs;
        public void AddDir(string path)
        {
            if (DB.Settings.Dirs.Contains(path))
                return;

            DB.Settings.Dirs = DB.Settings.Dirs.AddToArr(path);
            DB.SaveSettings();

            RestartBackgroundTasks(null, new IndexTaskConfig() { ForceRefreshDisplayAfterIndex = true });
        }
        public void RemoveDir(string d)
        {
            DB.Settings.Dirs = DB.Settings.Dirs.Where(x => x != d).ToArray();
            DB.SaveSettings();

            EnqueueRefreshDisplayOrder();
            RestartBackgroundTasks(null);
        }

        public void RemoveSrc(int id)
        {
            RestartBackgroundTasks(() =>
            {
                DB.RemoveSource(id);
                DB.SaveFiles();
                EnqueueRefreshDisplayOrder();
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

            DB.SaveFilesIfDirty();
            UpdateLibraryMaybe();
        }

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

        public void EnqueueRefreshDisplayOrder(bool skipFilters = false)
        {
            var box = new UpdateOp[1];
            box[0] = new UpdateOp()
            {
                PreLockAction = () => box[0].UILockAction = RefreshDisplayOrderBackgroundOnly(skipFilters),
            };

            SystemUpdates.Enqueue(box[0]);
        }

        Action RefreshDisplayOrderBackgroundOnly(bool skipFilters, bool postponeUIUpdate = false)
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

                var postUpdate = () =>
                {
                    _displayOrder = displayOrder;
                    DispalyGroups = dispalyGroups;
                    ClampIdx(ref _idx);
                    DisplayedCount = _displayOrder.Count;

                    SystemUpdates.Enqueue(new UpdateOp() { PreLockAction = () => OnDisplayOrderUpdate?.Invoke() });
                };

                if (postponeUIUpdate)
                    return postUpdate;

                postUpdate();
                return null;
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
                var allDirs = Indexer.GetBuiltInLibraryFolders();
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



        static SKImage _emptyThumbnail;
        static SKBitmap ToThumbnail(SKPath p) => IconStore.ToBitmap(128, 128, 32, (p, null));
        public SKImage GetScaledThumbnail(FileRecord rec, bool fast = false)
        {
            SKImage res = _scaledThumbnails.Get(rec.Sig);

            if (res == null && !rec.IsIndexed() && !rec.IndexFailed) // still indexing
            {
                rec.PrioTicks = DateTime.UtcNow.Ticks;
                Indexer.CheckFilesPrio = true;
            }

            if (!rec.IsIndexed() || rec.IndexFailed || IsFullScreenDrawMode) // will be fast
                return GetFullThumbnail(rec);

            if (res == null && !fast && initTask.IsCompleted)
            {
                var largeImage = GetFullThumbnail(rec);
                using var bmp = PhotoIndexer.ScaleImage(largeImage, DB.Settings.ThumbnailDrawSize, GetImageDimsAfterOrientation(rec));
                res = SKImage.FromBitmap(bmp);
                _scaledThumbnails.Put(rec.Sig, res);
            }

            return res;
        }

        public bool IsFullScreenDrawMode => DB.Settings.ThumbnailDrawSize < 0;
        public SKSizeI GetImageDimsAfterOrientation(FileRecord rec)
        {
            var o = DB.GetOrientation(rec);
            SKSizeI res = new(rec.W, rec.H);
            switch (o)
            {
                case SKEncodedOrigin.LeftTop:
                case SKEncodedOrigin.RightTop:
                case SKEncodedOrigin.RightBottom:
                case SKEncodedOrigin.LeftBottom:
                    res = new SKSizeI(rec.H, rec.W);
                    break;
                default:
                    break;
            }

            return res;
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
                        res = ImageLoader.DecodeThumbnail(data, DB.GetOrientation(file));

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

        

        public (string name, string path, byte[] data) GetImageForSharing(int idx)
        {
            var file = GetFile(idx);
            using var data = Indexer.GetFullImage(file);
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
            TerminateIndexerTask();
            _shutdown = true;
            backgroundLoaderTask?.Wait();

            DB.SaveFilesIfDirty();
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

            RestartBackgroundTasks(() => SetFolderModeBackgroundOnly(commFile, isDir));
        }

        internal void CloseSingleFolderMode()
        {
            RestartBackgroundTasks(() => SetFolderModeBackgroundOnly(null));
        }

        private void RestartBackgroundTasks(Action act, IndexTaskConfig cfg = null)
        {
            cfg ??= new IndexTaskConfig();
            SystemUpdates.Enqueue(new UpdateOp()
            {
                PreLockAction = TerminateIndexerTask,
                UILockAction = () =>
                {
                    lock (_lock)
                    {
                        try { act?.Invoke(); }
                        finally { RunIndexerTask(cfg); }
                    }
                }
            });
        }

        void TerminateIndexerTask()
        {
            initTask.Wait();
            Indexer.CancelIndex = true;
            indexTask?.Wait();
            Indexer.CancelIndex = false;
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

                    var o = DB.GetOrientation(file);
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

        public Func<Task, Task> RequestUILock;

        public void EnqueueInvalidateThumbnailCache()
        {
            SystemUpdates.Enqueue(new UpdateOp() 
            {
                UILockAction = () =>
                    {
                        _microCache.Clear();
                        _scaledThumbnails.Clear();
                        _fullSizeThumbnails.Clear();
                    }
            });
        }

        internal void InvalidateThumbnail(FileRecord file)
        {
            if (!file.IsIndexed())
                throw new ArgumentException();

            _microCache.Remove(file.Sig);
            _scaledThumbnails.Remove(file.Sig);
            _fullSizeThumbnails.Remove(file.Sig);
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
        public SKEncodedOrigin Orientation;
        public int Width, Height;

        public void Dispose()
        {
            Data?.Dispose();
            Bitmap?.Dispose();
        }
    }

}
