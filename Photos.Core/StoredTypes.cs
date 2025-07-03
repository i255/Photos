using MessagePack;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Photos.Core.StoredTypes
{
    public enum OptionalKeys
    {
        Lat = 1,
        Lon = 2,
        City = 3,
        Country = 4,
        CameraModel = 5
    }

    [MessagePackObject]
    public class DirectoryRecord
    {
        [Key(0)]
        public string Directory;
        [Key(1)]
        public int SourceId;

        [IgnoreMember]
        public bool IsInLib;

        public bool IsSubdirectoryOf(string dir) => Directory.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
        public string CombinePath(SourceRecord src) => Path.Combine(Directory, src.FN);

        public bool IsLocal() => SourceId == 0;

    }
    [MessagePackObject]
    public record struct SourceRecord
    {
        /// <summary>
        /// Dir ID
        /// </summary>
        [Key(0)]
        public int D;

        /// <summary>
        /// File Name
        /// </summary>
        [Key(1)]
        public required string FN;

        [Key(3)]
        public required long LastWriteTime;

        public (string display, string osPath) GetFullPath(PhotoProvider p)
        {
            if (this == default)
                return default;
            var dir = p.DB.Directories.GetValue(D);
            var path = dir.CombinePath(this);
            return (p.FilterService.GetDisplayPath(dir.SourceId, D, path), path);
        }
    }

    public enum FileRecordFlags
    {
        FlagMetadataRefreshRequired = 1
    }

    [MessagePackObject(AllowPrivate = true)]
    public partial class FileRecord
    {
        public bool GetFlag(FileRecordFlags flag) => (Flags & (int)flag) != 0;
        public void SetFlag(FileRecordFlags flag) => Flags |= (int)flag;
        public void ResetFlag(FileRecordFlags flag) => Flags &= ~(int)flag;

        [Key(0)]
        public int Flags; // NOT USED ACTUALLY. CAN BE REUSED

        /// <summary>
        /// Size
        /// </summary>
        [Key(1)]
        public int S;

        /// <summary>
        /// Main date time
        /// </summary>
        [Key(2)]
        public long DT; //{ get => LocalTimeToString(DateTime); set => DateTime = LocalTimeFromString(value); }
        /// <summary>
        /// Width
        /// </summary>
        [Key(3)]
        public int W;
        /// <summary>
        /// Height
        /// </summary>
        [Key(4)]
        public int H;

        /// <summary>
        /// Thumbnail
        /// </summary>
        [Key(5)]
        public long T;
        /// <summary>
        /// MicroThumbnail
        /// </summary>
        [Key(6)]
        public long MT;
        /// <summary>
        /// ThumbnailSize
        /// </summary>
        [Key(7)]
        public int TS;
        /// <summary>
        /// MicroThumbnailSize
        /// </summary>
        [Key(8)]
        public int MTS;

        [Key(9)]
        public SourceRecord[] Src;

        /// <summary>
        /// Signature
        /// </summary>
        [Key(10)]
        public ulong Sig;

        [Key(11)]
        private Dictionary<int, int> OptionalData; // TODO: test!!!

        [IgnoreMember]
        public long PrioTicks;

        [IgnoreMember]
        public SourceRecord SrcForSorting;
        
        [IgnoreMember]
        internal bool IndexFailed;

        public int GetOptional(OptionalKeys key) => OptionalData?.TryGetValue((int)key, out var res) == true ? res : 0;
        public void SetOptional(OptionalKeys key, int val)
        {
            OptionalData ??= new ();

            OptionalData[(int)key] = val;
        }

        static Dictionary<OptionalKeys, string> _optNames = new()
        {
            { OptionalKeys.Lat, "GPS Latitude" },
            { OptionalKeys.Lon, "GPS Longitude" },
            { OptionalKeys.Country, "GPS Country" },
            { OptionalKeys.City, "Nearest populated place" },
            { OptionalKeys.CameraModel, "Camera model" },
        };

        static readonly OptionalKeys[] StringOptions = new[] { OptionalKeys.Country, OptionalKeys.City, OptionalKeys.CameraModel };
        public static (string key, string text) FormatOptional(Storage storage, OptionalKeys key, int val)
        {
            var desc = _optNames.TryGetValue(key, out var des) ? des : "<unknown>";

            var text = key switch
            {
                OptionalKeys.Lat or OptionalKeys.Lon => LoadDegree(val).ToString("0.#####"),
                _ => StringOptions.Contains(key) ? storage.Strings.GetValue(val) :  throw new NotImplementedException()
            };

            return (desc, text);
        }

        public IEnumerable<(OptionalKeys Key, int Value)> EnumOptionalData(bool stringsOnly)
        {
            if (OptionalData != null)
                foreach (var item in OptionalData)
                    if (!stringsOnly || StringOptions.Contains((OptionalKeys)item.Key))
                        yield return ((OptionalKeys)item.Key, item.Value);
        }

        public static DateTime TimeFrom(long time) => new DateTime((int)(time / 10000_000000L), (int)(time / 100_000000L % 100), (int)(time / 1_000000L % 100),
            (int)(time / 1_0000 % 100), (int)(time / 1_00 % 100), (int)(time % 100));
        public static long TimeTo(DateTime time) => time.Year * 10000_000000L + time.Month * 100_000000L + time.Day * 1_000000L + time.Hour * 1_0000L + +time.Minute * 100 + +time.Second;


        [GeneratedRegex(@"\D(\d{8}(_|-)\d{6})\D")]
        private static partial Regex FullDateRegex();

        [GeneratedRegex(@"\D(\d{8})\D")]
        private static partial Regex ShortDateRegex();

        public static void AddFromInfo(IList<FileRecord> list, FileInfo x, Storage storage)
        {
            try
            {
                var rec = new FileRecord()
                {
                    S = (int)x.Length,
                    DT = TimeTo(x.LastWriteTime),
                    Src = new[] 
                    { 
                        new SourceRecord()
                        {
                            D = storage.Directories.GetIndex(new DirectoryRecord() { Directory = x.DirectoryName }),
                            FN = x.Name,
                            LastWriteTime = TimeTo(x.LastWriteTime)
                        }
                    }
                };

                bool TestTime(Regex r, string pattern, double maxHours, out DateTime dt)
                {
                    dt = default;
                    var m = r.Match(x.Name);
                    return m != null && DateTime.TryParseExact(m.Groups[1].Value, pattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt) 
                        && dt.Year > 1900 && dt.Year < 2100 && Math.Abs((x.LastWriteTime - dt).TotalHours) > maxHours;
                }

                if (TestTime(FullDateRegex(), "yyyyMMdd_HHmmss", 0.2, out var dt) || TestTime(ShortDateRegex(), "yyyyMMdd", 25, out dt))
                    rec.DT = TimeTo(dt);

                list.Add(rec);
            }
            catch (Exception e) { Utils.TraceError(e); }
        }

        public static FileRecord Clone(FileRecord proto, SourceRecord[] src)
        {
            var res = (FileRecord)proto.MemberwiseClone();
            res.Src = src;
            return res;
        }

        public bool IsIndexed() => Sig != 0 && TS != 0 && MTS != 0;

        public const int CurrentHeaderVersion = 1;
        const int DegreeMultiply = 1_000_000;
        static List<(GeoCoordinate coord, string country, string title, int radius)> _cities;

        public void FillExifFields(SKData skData, Storage db)
        {
            var exif = new ExifReader(skData.AsSpan());

            if (exif.TryGetSingleValue(ExifTags.Make, out string make))
            {
                make = make.Trim();
                if (exif.TryGetSingleValue(ExifTags.Model, out string model))
                    make = $"{make} - {model.Trim()}";

                SetOptional(OptionalKeys.CameraModel, db.Strings.GetIndex(make));
            }

            if (exif.TryGetSingleValue(ExifTags.DateTimeOriginal, out DateTime dateTime) || exif.TryGetSingleValue(ExifTags.DateTime, out dateTime))
                DT = TimeTo(dateTime);

            var lat = exif.GetTagValue(ExifTags.GPSLatitude);
            var lon = exif.GetTagValue(ExifTags.GPSLongitude);
            if (lat?.Length == 3 && lon?.Length == 3 && lat.Concat(lon).All(x => x is double))
            {
                var la = (double)lat[0] + (double)lat[1] / 60 + (double)lat[2] / 3600;
                var lo = (double)lon[0] + (double)lon[1] / 60 + (double)lon[2] / 3600;

                if (exif.TryGetSingleValue(ExifTags.GPSLatitudeRef, out string lRef) && lRef == "S")
                    la = -la;
                if (exif.TryGetSingleValue(ExifTags.GPSLongitudeRef, out lRef) && lRef == "W")
                    lo = -lo;


                if (GeoCoordinate.Validate(la) && GeoCoordinate.Validate(lo))
                {
                    SetOptional(OptionalKeys.Lat, SaveDegree(la));
                    SetOptional(OptionalKeys.Lon, SaveDegree(lo));

                    var coord = new GeoCoordinate() { Latitude = la, Longitude = lo };

                    _cities ??= Utils.ReadAllLines(Utils.GetResource<FileRecord>("cities.csv")).Select(x => x.Split('\t'))
                        .Select(x => (new GeoCoordinate() { Latitude = double.Parse(x[0], CultureInfo.InvariantCulture), Longitude = double.Parse(x[1], CultureInfo.InvariantCulture) }, 
                        x[2], x[3], int.Parse(x[4]))).ToList();

                    var city = _cities.MinBy(x => coord.GetDistanceTo(x.coord) - x.radius);

                    SetOptional(OptionalKeys.City, db.Strings.GetIndex(city.title));
                    SetOptional(OptionalKeys.Country, db.Strings.GetIndex(city.country));
                }
            }
        }

        private static int SaveDegree(double d) => (int)(d * DegreeMultiply);
        public static double LoadDegree(int v) => (double)v / DegreeMultiply;

    }
    [MessagePackObject]
    public class FilesSettings
    {
        [Key("lgls")]
        public long LastGoodLocalSyncUTC;
        [Key("v")]
        public int Version;
    }
    [MessagePackObject]
    public class SyncSource
    {
        [Key("i")]
        public int Id;
        [Key("dn")]
        public string DisplayName;

        [Key("lgs")]
        public long LastGoodSyncUTC;
        [Key("da")]
        public byte[] Data;
    }

    [MessagePackObject]
    public class StoredFilterInfo
    {
        [Key("se")]
        public HashSet<string> Selected = new();
        [Key("re")]
        public string[] Recent = Array.Empty<string>();
    }

    [MessagePackObject(AllowPrivate = true)]
    public sealed record class SettingsRecord
    {
        [Key("ts")]
        public int ThumbnailSize;
        [Key("da")]
        public byte[] Data;
        [Key("tds")]
        public int ThumbnailDrawSize;
        [Key("sm")]
        public SortModeEnum StoredSortMode;
        [Key("di")]
        public string[] Dirs;
        [Key("fi2")]
        public IReadOnlyDictionary<string, StoredFilterInfo> Filters = new Dictionary<string, StoredFilterInfo>();
        [Key("dev")]
        public bool DevMode;
        [Key("ct")]
        public long CreationTimeUtc;
        [Key("err")]
        public bool ErrorOptOut;
        [Key("obg")]
        public bool IsOpaqueBackground;
        [Key("notif")]
        public bool UpdatesNotificationOptOut;

        [IgnoreMember]
        Type EqualityContract => null;
    }
    [MessagePackObject]
    public class FilesHeader
    {
        [Key("fi")]
        public List<FileRecord> Files;
        [Key("tp")]
        public long ThumbnailsPos;
        [Key("mtp")]
        public long MicroThumbnailsPos;
        [Key("di")]
        public DirectoryRecord[] Directories;
        [Key("sy")]
        public SyncSource[] SyncSources;
        [Key("se")]
        public FilesSettings Settings;
        [Key("st")]
        public string[] Strings;
    }

    public enum SortModeEnum
    {
        Date = 0, Filename = 1
    }

    [MessagePackObject]
    public record struct AdditionalImageData
    {
        [Key("or")]
        public int Orientation;
        [Key("if")]
        public bool IsFavorite;
        [Key("ts")]
        public long TimeStamp;
    }

}
