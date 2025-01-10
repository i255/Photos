using MessagePack;
using Photos.Core.StoredTypes;
using SkiaSharp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO.Compression;

namespace Photos.Core
{
    public record struct FileRecordSourceKey
    {
        public string LowerFileName;
        public int DirId;
        public long LastWriteTime;
        public FileRecordSourceKey(SourceRecord rec)
        {
            DirId = rec.D;
            LowerFileName = rec.FN.ToLowerInvariant();
            LastWriteTime = rec.LastWriteTime;
        }

        public bool EqualsByPath(FileRecordSourceKey other) => LowerFileName == other.LowerFileName && DirId == other.DirId;
    }

    public class Storage : IDisposable
    {
        static MessagePackSerializerOptions SerializerOptions = MessagePack.Resolvers.StandardResolverAllowPrivate.Options;
        public bool Compress = false;

        readonly Dictionary<ulong, FileRecord> _files = new();
        readonly Dictionary<FileRecordSourceKey, FileRecord> _filesBySource = new();
        private readonly string _filesFileName, _settingsFileName, _additionalFileName;
        public BinFile Thumbnails, MicroThumbnails;
        private readonly string _path;
        public string MainPath => _path;
        public SyncSource[] SyncSources { get; internal set; }
        public FilesSettings FilesSettings;

        ImmutableDictionary<ulong, AdditionalImageData> AdditionalImageData = ImmutableDictionary<ulong, AdditionalImageData>.Empty;

        readonly public ArrayDict<string, string> Strings = new(x => x);
        readonly public ArrayDict<DirectoryRecord, (string, int)> Directories = new(x => (x.Directory.ToLowerInvariant(), x.SourceId));

        private SettingsRecord settings;

        Dictionary<int, SyncSource> srcLookup = new();
        object srcLookupSource;

        public void SetAdditionalImageData(FileRecord file, AdditionalImageData dat)
        {
            if (!file.IsIndexed())
                throw new ArgumentException();

            if ((dat.Orientation == (int)SKEncodedOrigin.TopLeft || dat.Orientation == 0) && !dat.IsFavorite)
                AdditionalImageData = AdditionalImageData.Remove(file.Sig);
            else
                AdditionalImageData = AdditionalImageData.SetItem(file.Sig, dat);
            SaveAdditionalData();

        }
        public AdditionalImageData GetAdditionalImageData(FileRecord file)
        {
            return AdditionalImageData.TryGetValue(file.Sig, out var data) ? data : default;
        }
        public string GetSourceName(int srcId)
        {
            if (SyncSources != null && !ReferenceEquals(srcLookupSource, SyncSources))
            {
                srcLookup = SyncSources.ToDictionary(x => x.Id);
                srcLookupSource = SyncSources;
            }

            return srcLookup.TryGetValue(srcId, out var src) ? src.DisplayName : "Local";
        }

        public event Action<SettingsRecord, SettingsRecord> OnSettingsUpdate;
        public SettingsRecord Settings
        {
            get => settings;
            set
            {
                var old = settings;
                settings = value;
                OnSettingsUpdate?.Invoke(old, settings);
                SaveSettings();
            }
        }

        public void SaveSettings()
        {
            Save(_settingsFileName, Settings);
        }

        internal void RemoveMissingSources(List<FileRecord> newFiles, bool protectUnreadableFolders, List<string> builtInFolders)
        {
            var completeSourceSet = newFiles.SelectMany(x => x.Src.Select(s => new FileRecordSourceKey(s))).ToHashSet();
            var availSources = newFiles.SelectMany(x => x.Src.Select(x => x.D)).Distinct().Select(x => Directories.GetValue(x).SourceId).ToHashSet();

            var availableDirs = Settings.Dirs.Where(x => Directory.Exists(x)).ToList(); // protect missing dirs or files outside of library
            var protectedDirs = Directories.ToArray().Select((x, i) => (x, i))
                .Where(x => x.x != null && (
                    x.x.SourceId == 0 && protectUnreadableFolders && !availableDirs.Any(u => x.x.IsSubdirectoryOf(u)) && !builtInFolders.Any(f => x.x.IsSubdirectoryOf(f)) // failed to list directory
                    || x.x.SourceId != 0 && !availSources.Contains(x.x.SourceId)) // failed to sync?
                ).Select(x => x.i).ToHashSet();

            var filter = new ArrayFilter<SourceRecord>(x => completeSourceSet.Contains(new FileRecordSourceKey(x)) || protectedDirs.Contains(x.D));
            lock (_files)
                foreach (var item in _files.Values)
                    FilterFileSources(filter, item);

            if (filter.HasChanges)
                IsDirty = true;
        }

        private void FilterFileSources(ArrayFilter<SourceRecord> filter, FileRecord item)
        {
            var newSrc = filter.Filter(item.Src);
            if (!ReferenceEquals(item.Src, newSrc))
                UpdateInternal(FileRecord.Clone(item, newSrc), item);
        }

        internal void RemoveSingleSource(FileRecord f, SourceRecord s)
        {
            if (!f.IsIndexed())
                throw new Exception("bad file");

            var filter = new ArrayFilter<SourceRecord>(x => x != s);
            lock (_files)
                FilterFileSources(filter, f);
            IsDirty = true;
            SaveFiles();
        }

        internal void RemoveSource(int srcId)
        {
            lock (_files)
            {
                var removedDirs = new HashSet<int>();
                for (int i = 0; i < Directories.Length; i++)
                {
                    if (Directories.GetValue(i)?.SourceId == srcId)
                    {
                        removedDirs.Add(i);
                        Directories.Remove(i);
                    }
                }

                var filter = new ArrayFilter<SourceRecord>(x => !removedDirs.Contains(x.D));
                foreach (var item in _files.Values)
                    FilterFileSources(filter, item);
                SyncSources = SyncSources.Where(x => x.Id != srcId).ToArray();
            }
        }

        public Storage(string path)
        {
            _filesFileName = Path.Combine(path, "index.bin");
            _settingsFileName = Path.Combine(path, "settings.bin");
            _additionalFileName = Path.Combine(path, "favorites.bin");
            _path = path;

            settings = Load<SettingsRecord>(_settingsFileName);

            if (Settings == null || Settings.ThumbnailDrawSize == 0 || Settings.Filters == null)
                settings = new SettingsRecord()
                {
                    ThumbnailSize = PhotoProvider.LargeThumbnailSize,
                    ThumbnailDrawSize = PhotoProvider.MediumThumbnailSize,
                    Dirs = new string[0],
                    CreationTimeUtc = FileRecord.TimeTo(DateTime.UtcNow),
                };
        }

        bool _initFinished;
        public void LoadFiles(List<FileRecord> folderViewList)
        {
            if (_initFinished)
                throw new Exception("init finished");

            var dt = DateTime.UtcNow;
            var additional = Load<ImmutableDictionary<ulong, AdditionalImageData>>(_additionalFileName);
            if (additional != null)
                AdditionalImageData = additional;

            var header = Load<FilesHeader>(_filesFileName) ?? new() { Files = new() };
            dt.PrintUtcMs("files", 100, true);

            lock (_files)
            {
                var folderPreloadDirs = Directories.ToArray();
                Directories.SetValues(header.Directories);
                Strings.SetValues(header.Strings);

                if (folderViewList != null) // set proper dir for preloaded files
                {
                    var newIdx = new int[folderPreloadDirs.Length];
                    Array.Fill(newIdx, -1);
                    for (int i = 0; i < folderPreloadDirs.Length; i++)
                        if (folderPreloadDirs[i] != null)
                            newIdx[i] = Directories.GetIndex(folderPreloadDirs[i]);

                    foreach (var item in folderViewList)
                        item.Src[0].D = newIdx[item.Src[0].D]; 
                }

                foreach (var item in header.Files)
                    AddInternal(item);
                //_files = header.Files.ToDictionary(x => x.GetIdentity());
                Thumbnails = new BinFile(_path, "large.bin", header.ThumbnailsPos);
                MicroThumbnails = new BinFile(_path, "tiny.bin", header.MicroThumbnailsPos);
                SyncSources = header.SyncSources ?? Array.Empty<SyncSource>();
                FilesSettings = header.Settings ?? new() { Version = FileRecord.CurrentHeaderVersion };

                if (FilesSettings.Version > FileRecord.CurrentHeaderVersion)
                    throw new Exception($"This version of {Utils.AppName} is incompatible with the photo index");

                if (FilesSettings.Version == 0)
                {
                    foreach (var item in _files)
                    {
                        item.Value.Flags = 0; // renamed from camera model
                        item.Value.SetFlag(FileRecordFlags.FlagMetadataRefreshRequired);
                    }

                    FilesSettings.Version = 1;
                }

                if (FilesSettings.Version != FileRecord.CurrentHeaderVersion)
                    throw new Exception("Index upgrade error");
            }
            _initFinished = true;
        }

        private void AddInternal(FileRecord item)
        {
            _files.Add(item.Sig, item);
            foreach (var src in item.Src)
                _filesBySource.Add(new FileRecordSourceKey(src), item);
        }
        private void UpdateInternal(FileRecord newItem, FileRecord orig)
        {
            _files[newItem.Sig] = newItem;
            foreach (var src in orig.Src)
                _filesBySource.Remove(new FileRecordSourceKey(src));
            foreach (var src in newItem.Src)
                _filesBySource.Add(new FileRecordSourceKey(src), newItem);
            IsDirty = true;
        }

        public void Dispose()
        {
            Thumbnails.Dispose();
            MicroThumbnails.Dispose();
        }
        internal void AddFile(FileRecord item)
        {
            if (!_initFinished)
                throw new Exception("not initialized");

            if (item.Sig == 0)
                throw new Exception($"missing sig");

            lock (_files)
            {
                if (_files.TryGetValue(item.Sig, out var existingFile))
                {
                    foreach (var src in item.Src)
                    {

                        var existingSrcIndex = Array.FindIndex(existingFile.Src, x => new FileRecordSourceKey(x) == new FileRecordSourceKey(src));
                        if (existingSrcIndex < 0) // not found => add
                        {
                            var srcArray = existingFile.Src;

                            var existingByPathSrcIndex = Array.FindIndex(existingFile.Src, x => new FileRecordSourceKey(x).EqualsByPath(new FileRecordSourceKey(src)));
                            if (existingByPathSrcIndex >= 0) // date changed, but not the content? replace, prevent duplicates
                            {
                                srcArray = srcArray.ToArray();
                                srcArray[existingByPathSrcIndex] = src;
                            }
                            else
                                srcArray = srcArray.AddToArr(src);

                            var newFile = FileRecord.Clone(existingFile, srcArray);
                            UpdateInternal(newFile, existingFile);
                            existingFile = newFile;
                        }                     
                    }

                    UpdateMetadata(item, existingFile);
                }
                else
                {
                    if (!item.IsIndexed())
                        throw new Exception($"missing key");

                    AddInternal(item);
                    IsDirty = true;
                }
            }

        }

        void UpdateMetadata(FileRecord newFile, FileRecord existingFile)
        {
            if (existingFile.DT != newFile.DT)
            {
                existingFile.DT = newFile.DT;
                IsDirty = true;
            }

            foreach (var item in newFile.EnumOptionalData(stringsOnly: false))
            {
                if (existingFile.GetOptional(item.Key) != item.Value)
                {
                    existingFile.SetOptional(item.Key, item.Value);
                    IsDirty = true;
                }
            }

            if (existingFile.GetFlag(FileRecordFlags.FlagMetadataRefreshRequired))
            {
                existingFile.ResetFlag(FileRecordFlags.FlagMetadataRefreshRequired);
                IsDirty = true;
            }
        }

        public List<FileRecord> ListFiles()
        {
            lock (_files)
                return _files.Values.ToList();
        }
        public FileRecord TryGetFileBySignature(ulong s)
        {
            lock (_files)
                return _files.TryGetValue(s, out var f) ? f : null;
        }

        //void RemoveSourceUnlocked(SourceRecord r) => _filesBySource.Remove(new FileRecordSourceKey(r));
        public FileRecord TryGetFileBySource(FileRecordSourceKey identity)
        {
            lock (_files)
                return _filesBySource.TryGetValue(identity, out var res) ? res : null;
        }

        public int Count => _files.Count;


        public bool IsDirty;
        public void SaveFiles()
        {
            if (!_initFinished)
                throw new Exception("not initialized");

            FilesHeader hdr;

            lock (_files)
            {
                IsDirty = false;
                MicroThumbnails.Flush();
                Thumbnails.Flush();

                hdr = new FilesHeader()
                {
                    Files = ListFiles(),
                    MicroThumbnailsPos = MicroThumbnails.CurrentPos,
                    ThumbnailsPos = Thumbnails.CurrentPos,
                    Directories = Directories.ToArray(),
                    Strings = Strings.ToArray(),
                    SyncSources = SyncSources,
                    Settings = FilesSettings
                };
            }
            
            Save(_filesFileName, hdr);
        }

        const string TmpSuffix = ".tmp";
        private void Save<T>(string path, T hdr)
        {
            FileStream baseStream;
            using (var file = CompressMaybe(baseStream = File.Create(path + TmpSuffix)))
            {
                Serialize(hdr, file);
                //file.Flush();
                if (baseStream.Length == 0)
                    throw new Exception("File is empty");
            }

            File.Delete(path);
            File.Move(path + TmpSuffix, path);
        }

        public static void Serialize<T>(T obj, Stream file)
        {
            MessagePackSerializer.Serialize(file, obj, options: SerializerOptions);
            //JsonSerializer.Serialize(file, obj, new JsonSerializerOptions()
            //{
            //    IncludeFields = true,
            //    DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
            //    IgnoreReadOnlyProperties = true
            //});
        }

        Stream CompressMaybe(FileStream fs)
        {
            //return fs.CanWrite ? new BrotliStream(fs, CompressionLevel.Optimal) : fs;

            if (Compress)
                return fs.CanWrite ? new BrotliStream(fs, CompressionLevel.Optimal) : new BrotliStream(fs, CompressionMode.Decompress);
            else
                return fs;
        }

        public T Load<T>(string path)
        {
            if (File.Exists(path))
            {
                FileStream baseStream;
                using var file = CompressMaybe(baseStream = File.OpenRead(path));
                if (baseStream.Length > 0)
                {
                    var res = Deserialize<T>(file);
                    File.Delete(path + TmpSuffix); // make sure nothing was left behind
                    return res;
                }
                else
                {
                    file.Dispose();
                    File.Delete(path); // delete zero length
                }
            }

            if (File.Exists(path + TmpSuffix))
            {
                File.Move(path + TmpSuffix, path);
                return Load<T>(path);
            }

            return default;
        }

        public static T Deserialize<T>(Stream file) => MessagePackSerializer.Deserialize<T>(file, options: SerializerOptions);
        public static T Deserialize<T>(ReadOnlyMemory<byte> data) => MessagePackSerializer.Deserialize<T>(data, SerializerOptions);
        public static byte[] Serialize<T>(T obj) => MessagePackSerializer.Serialize(obj, SerializerOptions);

        internal void ClearFiles()
        {
            lock (_files)
            {
                _files.Clear();
                _filesBySource.Clear();
                Directories.SetValues(null);
                Strings.SetValues(null);

                Thumbnails.Truncate();
                MicroThumbnails.Truncate();

                SaveFiles();
            }
        }

        void SaveAdditionalData() => Save(_additionalFileName, AdditionalImageData);
    }

    public class BinFile : IDisposable
    {
        FileStream fileStream;
        public long CurrentPos;
        public BinFile(string path, string name, long pos)
        {
            var fname = Path.Combine(path, name);
            fileStream = File.Open(fname, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            CurrentPos = pos;
        }

        public void Dispose()
        {
            lock (fileStream)
                fileStream.Dispose();
        }

        public void Write(Span<byte> bytes)
        {
            lock (fileStream)
            {
                fileStream.Position = CurrentPos;
                fileStream.Write(bytes);
                CurrentPos = fileStream.Position;
            }
        }

        public byte[] Read(long thumbnail, int thumbnailSize)
        {
            if (thumbnail >= fileStream.Length) // read past end
                return Array.Empty<byte>();

            var buf = new byte[thumbnailSize];
            lock (fileStream)
            {
                fileStream.Position = thumbnail;
                fileStream.ReadExactly(buf, 0, thumbnailSize);
                return buf;
            }
        }

        internal void Truncate()
        {
            lock (fileStream)
            {
                CurrentPos = 0;
                fileStream.SetLength(0);
            }
        }

        public void Flush()
        {
            lock (fileStream)
                fileStream.Flush();
        }
    }
}
