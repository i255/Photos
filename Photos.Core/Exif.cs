using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Photos.Core
{
    public ref struct ExifReader
    {
        private static readonly Regex _nullDateTimeMatcher = new Regex(@"^[\s0]{4}[:\s][\s0]{2}[:\s][\s0]{5}[:\s][\s0]{2}[:\s][\s0]{2}$");
        private Dictionary<ushort, long> _ifd0Catalogue = null;

        static readonly byte[] ExifPattern = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00, 0x4d, 0x4d, 0x00, 0x2a];
        static readonly byte[] ExifPattern2 = [0x45, 0x78, 0x69, 0x66, 0x00, 0x00, 0x49, 0x49, 0x00, 0x2a];

        readonly ReadOnlySpan<byte> Data;
        public int Pos;

        public bool Found => _ifd0Catalogue != null;
        private bool _isLittleEndian = false;
        private int _tiffHeaderStart = 0;
        public int EndOfExif;
        int BytesLeft => Data.Length - Pos;

        public ExifReader(ReadOnlySpan<byte> data)
        {
            Data = data;
            try
            {
                JpegSearch();
                if (!Found)
                    BruteForce();
            }
            catch { _ifd0Catalogue = null; }

            if (!Found)
            {
                Pos = 0;
                EndOfExif = 0;
            }
        }

        void JpegSearch()
        {
            Pos = 0;

            if (BytesLeft < 8)
                return;

            if (ReadUShort() != 0xFFD8) // JPEG header
                return;

            while (Data[Pos] == 0xff && Data[Pos + 1] != 0xE1)
            {
                var len = Data[Pos + 2] * 256 + Data[Pos + 3];
                if (BytesLeft < len + 8)
                    return;
                Pos += (2 + len);
            }

            if (ReadUShort() != 0xffe1 || BytesLeft < 8)
                return;

            EndOfExif = Pos + ReadUShort();
            CreateTagIndex();
        }

        void BruteForce()
        {
            Pos = 0;
            var searchArea = Data.Slice(0, Math.Min(Data.Length, 128 << 10)); // only check the start of the file
            var idx = searchArea.IndexOf(ExifPattern);
            if (idx < 0)
                idx = searchArea.IndexOf(ExifPattern2);

            if (idx > 0)
            {
                Pos = idx;
                CreateTagIndex();

                if (Found && _ifd0Catalogue.Count > 0)
                    EndOfExif = (int)_ifd0Catalogue.Values.Max() + 12;
            }
        }

        private static byte GetTIFFFieldLength(ushort tiffDataType)
        {
            return tiffDataType switch
            {
                1 or 2 or 7 or 6 => 1,
                3 or 8 => 2,
                4 or 9 or 11 => 4,
                5 or 10 or 12 => 8,
                _ => throw new Exception(string.Format("Unknown TIFF datatype: {0}", tiffDataType)),
            };
        }

        private ushort ReadUShort() => ToUShort(ReadBytes(2));

        private uint ReadUint() => ToUint(ReadBytes(4));

        private string ReadString(int chars)
        {
            var bytes = ReadBytes(chars);
            return Encoding.UTF8.GetString(bytes);
        }

        private ReadOnlySpan<byte> ReadBytes(int byteCount)
        {
            Pos += byteCount;
            return Data[(Pos - byteCount).. Pos];
        }

        private readonly ushort ToUShort(ReadOnlySpan<byte> data)
        {
            Span<byte> buf = stackalloc byte[2];
            data[..buf.Length].CopyTo(buf);
            if (_isLittleEndian != BitConverter.IsLittleEndian)
                buf.Reverse();

            return BitConverter.ToUInt16(buf);
        }


        private double ToURational(ReadOnlySpan<byte> data)
        {
            var numerator = ToUint(data[0..4]);
            var denominator = ToUint(data[4..8]);
            return (double)numerator / denominator;
        }

        private readonly double ToRational(ReadOnlySpan<byte> data)
        {
            var numerator = ToInt(data[0..4]);
            var denominator = ToInt(data[4..8]);
            return (double)numerator / denominator;
        }

        private uint ToUint(ReadOnlySpan<byte> data)
        {
            Span<byte> buf = stackalloc byte[4];
            data[..buf.Length].CopyTo(buf);
            if (_isLittleEndian != BitConverter.IsLittleEndian)
                buf.Reverse();

            return BitConverter.ToUInt32(buf);
        }

        private readonly int ToInt(ReadOnlySpan<byte> data)
        {
            Span<byte> buf = stackalloc byte[4];
            data[..buf.Length].CopyTo(buf);
            if (_isLittleEndian != BitConverter.IsLittleEndian)
                buf.Reverse();

            return BitConverter.ToInt32(buf);
        }

        private readonly double ToDouble(ReadOnlySpan<byte> data)
        {
            Span<byte> buf = stackalloc byte[sizeof(double)];
            data[..buf.Length].CopyTo(buf);
            if (_isLittleEndian != BitConverter.IsLittleEndian)
                buf.Reverse();

            return BitConverter.ToDouble(buf);
        }

        private float ToSingle(ReadOnlySpan<byte> data)
        {
            Span<byte> buf = stackalloc byte[sizeof(float)];
            data[..buf.Length].CopyTo(buf);
            if (_isLittleEndian != BitConverter.IsLittleEndian)
                buf.Reverse();

            return BitConverter.ToSingle(buf);
        }

        private short ToShort(ReadOnlySpan<byte> data)
        {
            Span<byte> buf = stackalloc byte[sizeof(short)];
            data[..buf.Length].CopyTo(buf);
            if (_isLittleEndian != BitConverter.IsLittleEndian)
                buf.Reverse();

            return BitConverter.ToInt16(buf);
        }

        private sbyte ToSByte(ReadOnlySpan<byte> data) => (sbyte)(data[0] - byte.MaxValue);

        private void CreateTagIndex()
        {
            if (BytesLeft < 14)
                return;

            if (ReadString(4) != "Exif")
                return;

            if (ReadUShort() != 0)
                return;

            _tiffHeaderStart = Pos;

            var endiness = ReadString(2);
            if (endiness != "MM" && endiness != "II")
                return;

            _isLittleEndian = endiness == "II";

            if (ReadUShort() != 0x002A)
                return;

            var ifdOffset = (int)ReadUint();
            Pos = ifdOffset + _tiffHeaderStart;

            _ifd0Catalogue = [];
            CatalogueIFD(ref _ifd0Catalogue);

            if (BytesLeft < 4)
                return;
            
            // the thumbnail IFD
            ReadUint();

            if (!TryGetSingleValue((ExifTags)0x8769, out uint offset))
                return;

            Pos = (int)offset + _tiffHeaderStart;

            CatalogueIFD(ref _ifd0Catalogue);

            if (TryGetSingleValue((ExifTags)0x8825, out offset))
            {
                Pos = (int)offset + _tiffHeaderStart;
                CatalogueIFD(ref _ifd0Catalogue);
            }
        }
        public bool TryGetSingleValue<T>(ExifTags tag, out T v)
        {
            var res = GetTagValue(tag);
            if (res != null && res.Length == 1 && res[0] is T t)
            {
                v = t;
                return true;
            }

            v = default;
            return false;
        }

        public IEnumerable<ExifTags> GetKeys() => _ifd0Catalogue?.Keys.Select(x => (ExifTags)x);

        public string Dump
        {
            get
            {
                var keys = GetKeys();
                if (keys == null)
                    return "";
                var sb = new StringBuilder();
                sb.AppendLine("--");
                foreach (var item in keys)
                {
                    var val = GetTagValue(item);
                    sb.Append(item.ToString());
                    sb.Append(": ");
                    foreach (var v in val)
                    {
                        sb.Append(v is byte[] b ? "bytes:" + b.Length : v.ToString());
                        sb.Append("; ");
                    }
                    sb.Append(val.FirstOrDefault()?.GetType());
                    sb.AppendLine();
                }
                sb.AppendLine();
                return sb.ToString();
            }
        }

        public object[] GetTagValue(ExifTags tagId) => GetTagValue((ushort)tagId);

        public object[] GetTagValue(ushort tagId)
        {
            var tagDictionary = _ifd0Catalogue;
            var tagData = GetTagBytes(tagDictionary, tagId, out var tiffDataType, out var numberOfComponents);

            if (tagData.IsEmpty || numberOfComponents == 0)
                return [];

            var fieldLength = GetTIFFFieldLength(tiffDataType);
            if (fieldLength == 1)
                numberOfComponents = 1;

            var res = new object[numberOfComponents];

            for (int i = 0; i < numberOfComponents; i++)
            {
                object result = tiffDataType switch
                {
                    1 => tagData.ToArray(),
                    2 => ToStringOrDate(tagId, tagData),// ascii string
                    3 => ToUShort(tagData),// unsigned short
                    4 => ToUint(tagData),// unsigned long
                    5 => ToURational(tagData),
                    6 => ToSByte(tagData),// signed byte
                    7 => tagData.ToArray(),
                    8 => ToShort(tagData),// Signed short
                    9 => ToInt(tagData),// Signed long
                    10 => ToRational(tagData),// signed rational
                    11 => ToSingle(tagData),// single float
                    12 => ToDouble(tagData),// double float
                    _ => throw new Exception(string.Format("Unknown TIFF datatype: {0}", tiffDataType)),
                };
                tagData = tagData[fieldLength..];
                res[i] = result;
            }

            return res;
        }

        private static object ToStringOrDate(ushort tagId, ReadOnlySpan<byte> tagData)
        {
            object result;
            var str = Encoding.UTF8.GetString(tagData);

            var nullCharIndex = str.IndexOf('\0');
            if (nullCharIndex != -1)
                str = str.Substring(0, nullCharIndex);

            result = ToDateTime(tagId, str, out var dateResult) ? dateResult : str;
            return result;
        }

        static ushort[] dateTags = [(ushort)ExifTags.DateTime, (ushort)ExifTags.DateTimeDigitized, (ushort)ExifTags.DateTimeOriginal];

        private static bool ToDateTime(ushort tagId, string str, out DateTime result)
        {
            result = default;
            if (!dateTags.Contains(tagId))
                return false;

            if (string.IsNullOrEmpty(str) || _nullDateTimeMatcher.IsMatch(str))
                return false;

            str = str.Replace('/', ':');
            // There are 2 types of date - full date/time stamps, and plain dates. Dates are 10 characters long.
            if (str.Length == 10)
                return DateTime.TryParseExact(str, "yyyy:MM:dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);

            // "The format is "YYYY:MM:DD HH:MM:SS" with time shown in 24-hour format, and the date and time separated by one blank character [20.H].
            return DateTime.TryParseExact(str, "yyyy:MM:dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        private ReadOnlySpan<byte> GetTagBytes(IDictionary<ushort, long> tagDictionary, ushort tagId, out ushort tiffDataType, out uint numberOfComponents)
        {
            tiffDataType = 0;
            numberOfComponents = 0;

            if (tagDictionary == null || !tagDictionary.ContainsKey(tagId))
                return null;

            var tagOffset = tagDictionary[tagId];
            Pos = (int)tagOffset;

            var currenttagId = ReadUShort();

            if (currenttagId != tagId)
                return null;

            tiffDataType = ReadUShort();
            numberOfComponents = ReadUint();

            var dataSize = (int)(numberOfComponents * GetTIFFFieldLength(tiffDataType));
            var tagData = ReadBytes(4);
            if (dataSize > 4)
            {
                var offsetAddress = ToInt(tagData);
                if (offsetAddress + _tiffHeaderStart + dataSize > Data.Length)
                    return null;

                return Data.Slice(offsetAddress + _tiffHeaderStart, dataSize);
            }

            return tagData[..dataSize];
        }

        private void CatalogueIFD(ref Dictionary<ushort, long> tagOffsets)
        {
            if (BytesLeft < 2)
                return;

            var entryCount = ReadUShort();
            for (ushort currentEntry = 0; currentEntry < entryCount; currentEntry++)
            {
                if (BytesLeft < 12)
                    return;

                var currentTagNumber = ReadUShort();
                tagOffsets[currentTagNumber] = Pos - 2;

                // Go to the end of this item (10 bytes, as each entry is 12 bytes long)
                Pos += 10;
            }
        }
    }

    public enum ExifTags : ushort
    {
        ImageWidth = 0x100,
        ImageLength = 0x101,
        BitsPerSample = 0x102,
        Compression = 0x103,
        PhotometricInterpretation = 0x106,
        ImageDescription = 0x10E,
        Make = 0x10F,
        Model = 0x110,
        StripOffsets = 0x111,
        Orientation = 0x112,
        SamplesPerPixel = 0x115,
        RowsPerStrip = 0x116,
        StripByteCounts = 0x117,
        XResolution = 0x11A,
        YResolution = 0x11B,
        PlanarConfiguration = 0x11C,
        ResolutionUnit = 0x128,
        TransferFunction = 0x12D,
        Software = 0x131,
        DateTime = 0x132,
        Artist = 0x13B,
        WhitePoint = 0x13E,
        PrimaryChromaticities = 0x13F,
        JPEGInterchangeFormat = 0x201,
        JPEGInterchangeFormatLength = 0x202,
        YCbCrCoefficients = 0x211,
        YCbCrSubSampling = 0x212,
        YCbCrPositioning = 0x213,
        ReferenceBlackWhite = 0x214,
        Copyright = 0x8298,

        ExposureTime = 0x829A,
        FNumber = 0x829D,
        ExposureProgram = 0x8822,
        SpectralSensitivity = 0x8824,
        ISOSpeedRatings = 0x8827,
        OECF = 0x8828,
        ExifVersion = 0x9000,
        DateTimeOriginal = 0x9003,
        DateTimeDigitized = 0x9004,
        ComponentsConfiguration = 0x9101,
        CompressedBitsPerPixel = 0x9102,
        ShutterSpeedValue = 0x9201,
        ApertureValue = 0x9202,
        BrightnessValue = 0x9203,
        ExposureBiasValue = 0x9204,
        MaxApertureValue = 0x9205,
        SubjectDistance = 0x9206,
        MeteringMode = 0x9207,
        LightSource = 0x9208,
        Flash = 0x9209,
        FocalLength = 0x920A,
        SubjectArea = 0x9214,
        MakerNote = 0x927C,
        UserComment = 0x9286,
        SubsecTime = 0x9290,
        SubsecTimeOriginal = 0x9291,
        SubsecTimeDigitized = 0x9292,
        FlashpixVersion = 0xA000,
        ColorSpace = 0xA001,
        PixelXDimension = 0xA002,
        PixelYDimension = 0xA003,
        RelatedSoundFile = 0xA004,
        FlashEnergy = 0xA20B,
        SpatialFrequencyResponse = 0xA20C,
        FocalPlaneXResolution = 0xA20E,
        FocalPlaneYResolution = 0xA20F,
        FocalPlaneResolutionUnit = 0xA210,
        SubjectLocation = 0xA214,
        ExposureIndex = 0xA215,
        SensingMethod = 0xA217,
        FileSource = 0xA300,
        SceneType = 0xA301,
        CFAPattern = 0xA302,
        CustomRendered = 0xA401,
        ExposureMode = 0xA402,
        WhiteBalance = 0xA403,
        DigitalZoomRatio = 0xA404,
        FocalLengthIn35mmFilm = 0xA405,
        SceneCaptureType = 0xA406,
        GainControl = 0xA407,
        Contrast = 0xA408,
        Saturation = 0xA409,
        Sharpness = 0xA40A,
        DeviceSettingDescription = 0xA40B,
        SubjectDistanceRange = 0xA40C,
        ImageUniqueID = 0xA420,

        GPSVersionID = 0x0,
        GPSLatitudeRef = 0x1,
        GPSLatitude = 0x2,
        GPSLongitudeRef = 0x3,
        GPSLongitude = 0x4,
        GPSAltitudeRef = 0x5,
        GPSAltitude = 0x6,
        GPSTimestamp = 0x7,
        GPSSatellites = 0x8,
        GPSStatus = 0x9,
        GPSMeasureMode = 0xA,
        GPSDOP = 0xB,
        GPSSpeedRef = 0xC,
        GPSSpeed = 0xD,
        GPSTrackRef = 0xE,
        GPSTrack = 0xF,
        GPSImgDirectionRef = 0x10,
        GPSImgDirection = 0x11,
        GPSMapDatum = 0x12,
        GPSDestLatitudeRef = 0x13,
        GPSDestLatitude = 0x14,
        GPSDestLongitudeRef = 0x15,
        GPSDestLongitude = 0x16,
        GPSDestBearingRef = 0x17,
        GPSDestBearing = 0x18,
        GPSDestDistanceRef = 0x19,
        GPSDestDistance = 0x1A,
        GPSProcessingMethod = 0x1B,
        GPSAreaInformation = 0x1C,
        GPSDateStamp = 0x1D,
        GPSDifferential = 0x1E
    }
}
