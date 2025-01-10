using Photos.Core.StoredTypes;
using Photos.Lib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Core
{
    public class FilterService
    {
        public const string FolderFilter = "folder";
        public const string FavFilter = "favorite";

        private readonly Storage DB;
        Dictionary<string, FileFilterGroup> _filterLookup;
        Dictionary<int, int> _prefixes;
        HashSet<int> _libDirsSet;

        SettingsRecord volatileSettingsRecord = CreateVolatileSettings();

        private static SettingsRecord CreateVolatileSettings() => new() { StoredSortMode = SortModeEnum.Filename };

        public string GetDisplayPath(int srcId, int dirId, string fullPath) => 
            _prefixes?.TryGetValue(srcId, out var prefix) == true && prefix <= fullPath.Length && _libDirsSet.Contains(dirId) ? fullPath[prefix..] : fullPath;

        public SortModeEnum CurrentSortMode => ActiveSettings.StoredSortMode;

        public void ToggleSortOrder()
        {
            var mode = CurrentSortMode;
            mode++;
            mode = (SortModeEnum)((int)mode % Enum.GetValues<SortModeEnum>().Length);

            ActiveSettings = ActiveSettings with { StoredSortMode = mode };
            FilterUpdate?.Invoke();
        }

        public IReadOnlyDictionary<string, StoredFilterInfo> StoredFilters => DB.Settings.Filters;
        SettingsRecord ActiveSettings
        {
            get => PersistentFilters ? DB.Settings : volatileSettingsRecord;
            set
            {
                if (PersistentFilters) 
                    DB.Settings = value;
                else
                    volatileSettingsRecord = value;
            }
        }

        bool _persistentFilters;
        internal event Action FilterUpdate;

        public bool PersistentFilters
        {
            get => _persistentFilters;
            set
            {
                if (value && !_persistentFilters)
                    volatileSettingsRecord = CreateVolatileSettings();

                _persistentFilters = value;
            }
        }
        public FilterService(Storage db)
        {
            DB = db;
        }

        public List<FileFilterGroup> FilterGroups => _filterLookup.Values.OrderBy(x => x.SortOrder).ToList();

        public bool FiltersReady => _filterLookup.Count > 0;

        public void FilterFiles(List<FileRecord> files, IReadOnlyDictionary<string, StoredFilterInfo> filterSet = null)
        {
            var filters = GetActiveFilters(filterSet).Where(x => x.items.Count > 0).ToList();

            if (filters.Count > 0)
                files.RemoveAll(file => !filters.All(fg =>
                {
                    if (fg.fg.SrcFilter)
                        return file.Src.Any(src => DB.Directories.GetValue(src.D).IsInLib &&
                            (!fg.fg.IsNegated && fg.items.Any(f => f.SrcFilter(src)) || fg.fg.IsNegated && fg.items.All(f => !f.SrcFilter(src))));

                    return !fg.fg.IsNegated && fg.items.Any(f => f.Filter(file)) || fg.fg.IsNegated && fg.items.All(f => !f.Filter(file));
                }));

        }

        List<(FileFilterGroup fg, List<FileFilter> items, List<FileFilter> recent)> GetActiveFilters(IReadOnlyDictionary<string, StoredFilterInfo> filterSet)
        {
            filterSet ??= ActiveSettings.Filters;
            var res = filterSet.Select(x => _filterLookup.TryGetValue(x.Key, out var fg)
                ? (
                    fg, 
                    x.Value.Selected.Select(fid => fg.Filters.TryGetValue(fid, out var f) ? f : null).Where(x => x != null).ToList(),
                    x.Value.Recent.Select(fid => fg.Filters.TryGetValue(fid, out var f) ? f : null).Where(x => x != null).ToList()
                    )
                : default).Where(x => x != default).ToList();

            return res;
        }

        public void RefreshFilters(List<FileRecord> files, IEnumerable<DirectoryRecord> dirs, bool purgeMissing)
        {
            var sep = new[] { '\\', '/' };
            //var files = GetLibraryFiles().ToList();

            int FindCommonPrefix(IEnumerable<string> strs)
            {
                var ordered = strs.ToList();
                //if (ordered.Count == 1)
                //    return ordered[0].Length + 1;
                ordered.Sort();
                var first = ordered.First();
                var last = ordered.Last();
                var i = 0;
                var res = 0;
                while (i < Math.Min(last.Length, first.Length) && first[i] == last[i])
                {
                    if (sep.Contains(first[i]))
                        res = i + 1;
                    i++;
                }
                return res;
            }

            if (files.Count == 0)
                _filterLookup = new();
            else
            {
                _prefixes = dirs.GroupBy(x => x.SourceId).ToDictionary(x => x.Key, x => FindCommonPrefix(x.Select(d => d.Directory)));

                var years = files.Select(x => FileRecord.TimeFrom(x.DT).Year).GroupBy(x => x).Select(x => (cnt: x.Count(), year: x.Key)).Select(x => new FileFilter()
                {
                    Id = x.year.ToString(),
                    Text = x.year.ToString(),
                    Filter = f => FileRecord.TimeFrom(f.DT).Year == x.year,
                    Count = x.cnt
                }).ToList();

                var existingDirs = files.SelectMany(x => x.Src.Select(s => (s.D, x))).GroupBy(x => x.D)
                    .Select(x => (dir: DB.Directories.GetValue(x.Key), cnt: x.Count(), d: x.Key))
                    .ToList();

                var allDirs = dirs.SelectMany(x =>
                {
                    var str = x.Directory;
                    var res = new List<string>();

                    do
                    {
                        res.Add(str);
                        var idx = str.LastIndexOfAny(sep);
                        str = idx > 0 ? str[0..idx] : null;
                    } while (str != null && str.Length >= _prefixes[x.SourceId]);

                    return res.Select(dir => (x.SourceId, dir));
                }).Distinct().OrderBy(x => x.SourceId).ThenBy(x => x.dir.ToLowerInvariant()).ToList(); // order important for next step
                allDirs.AddRange(allDirs.Select(x => x.SourceId).Distinct().Select(x => (x, default(string))).ToList());

                var libDirsSet = new HashSet<int>();
                var folders = allDirs.Select(x =>
                {
                    var subDirs = existingDirs.Where(e => e.dir.SourceId == x.SourceId && (x.dir == null || e.dir.IsSubdirectoryOf(x.dir))).ToList();
                    var count = subDirs.Sum(x => x.cnt);
                    var set = new HashSet<int>(subDirs.Select(x => x.d));
                    if (x.dir != null)
                        libDirsSet.UnionWith(set);

                    return new FileFilter()
                    {
                        Id = $"{x.SourceId}/{x.dir?.ToLowerInvariant()}",
                        Text = x.dir?[_prefixes[x.SourceId]..] ?? " <root> ",
                        PreText = DB.GetSourceName(x.SourceId),
                        //Filter = f => f.Src.Any(s => set.Contains(s.D)),
                        SrcFilter = s => set.Contains(s.D),
                        Count = count
                    };
                }).Where(x => x.Count > 0).ToList();
                _libDirsSet = libDirsSet;

                for (int i = 1; i < folders.Count; i++) // remove empty path parts
                {
                    if (folders[i - 1] != null && folders[i].Id.StartsWith(folders[i - 1].Id) && folders[i].Count == folders[i - 1].Count)
                        folders[i] = null;
                }
                folders.RemoveAll(x => x == null);

                var res = new[] {
                    new FileFilterGroup() { GroupId = "year", SortOrder = 20, FilterList = years, SortByAlpha = true, AlphaSorter = x => x.OrderByDescending(f => f.Text) },
                    new FileFilterGroup() { GroupId = FolderFilter, HideInFilterList = true, SortOrder = 10,
                        FilterList = folders,
                        SortByAlpha = false,
                        SrcFilter = true,
                        AlphaSorter = x => x.OrderBy(f => f.PreText).ThenBy(f => f.Text) 
                    },
                    new FileFilterGroup() { GroupId = "camera model", SortOrder = 30, FilterList = GetStringFilter(x => x.GetOptional(OptionalKeys.CameraModel)), SortByAlpha = false },
                    new FileFilterGroup() { GroupId = "city", SortOrder = 40, FilterList = GetStringFilter(x=>x.GetOptional(OptionalKeys.City)), SortByAlpha = false },
                    new FileFilterGroup() { GroupId = "country", SortOrder = 40, FilterList = GetStringFilter(x=>x.GetOptional(OptionalKeys.Country)), SortByAlpha = false },
                }.ToList();

                foreach (var item in res)
                    item.Text = $"Select {item.GroupId}";

                res.AddRange(res.Select(gr =>
                {
                    var res = gr.Clone();
                    res.HideInFilterList = false;
                    res.Text = $"Exclude {gr.GroupId}";
                    res.GroupId = $"!{gr.GroupId}";
                    res.IsNegated = true;
                    res.SrcFilter = gr.SrcFilter;
                    res.SortOrder = gr.SortOrder + 1000;
                    return res;
                }).ToList());

                res.Add(new FileFilterGroup()
                {
                    GroupId = FavFilter,
                    HideInFilterList = true,
                    Text = "Favorites",
                    SortOrder = 5,
                    FilterList = new[] { 
                        new FileFilter
                        {
                            Count= 1,
                            Id = "fav",
                            Text = "Favorite",
                            Filter = x => DB.GetAdditionalImageData(x).IsFavorite
                        }
                    }
                });

                _filterLookup = res.ToDictionary(x => x.GroupId);
            }

            if (purgeMissing)
            {
                var setting = ActiveSettings;

                foreach (var fg in ActiveSettings.Filters)
                    foreach (var item in fg.Value.Selected)
                        if (!_filterLookup.TryGetValue(fg.Key, out var fgDict) || !fgDict.Filters.ContainsKey(item))
                            setting = ToggleFilter(fg.Key, item, setting);

                if (setting != ActiveSettings)
                    ActiveSettings = setting;
            }

            List<FileFilter> GetStringFilter(Func<FileRecord, int> stringSelector) => files.Select(stringSelector).GroupBy(x => x)
                                   .Select(x => new FileFilter()
                                   {
                                       Id = x.Key.ToString(),
                                       Text = DB.Strings.GetValue(x.Key) ?? "<unknown>",
                                       Filter = f => stringSelector(f) == x.Key,
                                       Count = x.Count()
                                   }).ToList();
        }
       
        public void ToggleFilters(FileFilterGroup gr, params FileFilter[] filters)
        {
            var settings = ActiveSettings;

            foreach (var item in filters)
                settings = ToggleFilter(gr.GroupId, item.Id, settings);
            ActiveSettings = settings;
            FilterUpdate?.Invoke();
        }

        static SettingsRecord ToggleFilter(string filterField, string filterId, SettingsRecord settingsRecord)
        {
            var newFilters = new StoredFilterInfo();
            if (settingsRecord.Filters.TryGetValue(filterField, out var oldFilters))
            {
                foreach (var item in oldFilters.Selected)
                    newFilters.Selected.Add(item);
                newFilters.Recent = oldFilters.Recent;
            }

            if (newFilters.Selected.Contains(filterId))
                newFilters.Selected.Remove(filterId);
            else
                newFilters.Selected.Add(filterId);

            newFilters.Recent = filterId.Yield().Concat(newFilters.Recent.Where(x => x != filterId)).Where(x => !newFilters.Selected.Contains(x)).Take(10).ToArray();

            var dict = new Dictionary<string, StoredFilterInfo>(settingsRecord.Filters);
            if (newFilters.Selected.Count > 0 || newFilters.Recent.Length > 0)
                dict[filterField] = newFilters;
            else
                dict.Remove(filterField);

            return settingsRecord with { Filters = dict };
        }
        public bool IsFilterActive(FileFilterGroup gr, FileFilter f) => ActiveSettings.Filters.TryGetValue(gr.GroupId, out var ids) && ids.Selected.Contains(f.Id);
        public bool IsFilterGroupActive(string grId) => ActiveSettings.Filters.TryGetValue(grId, out var gr) && gr.Selected.Count > 0;
        public FileFilterGroup GetFilterGroup(string grId) => _filterLookup[grId];
        internal IEnumerable<FileFilterGroup> GetActiveFilterTypes() => ActiveSettings.Filters?.Where(x=>x.Value.Selected.Count > 0)
            .Select(x => _filterLookup.TryGetValue(x.Key, out var f) ? f : null)
            .Where(x => x != null) ?? Array.Empty<FileFilterGroup>();

        // menu ---------------
        public void ActiveFiltersMenu(MenuDef m, bool isForeignFilterSet = false, IReadOnlyDictionary<string, StoredFilterInfo> filterSet = null, bool withRecent = false)
        {
            //var isForeignFilterSet = filterSet != null;
            var filters = GetActiveFilters(filterSet);

            m.Items.Add(new MenuItem()
            {
                Text = $"- {filters.Sum(x => x.items.Count)} item(s) selected -",
                Submenu = Submenu,
                TextAlign = SkiaSharp.SKTextAlign.Center,
                TextScale = 0.8f,
            });

            void Submenu(MenuDef m)
            {
                m.Title = filters.Count == 1 ? $"{filters[0].fg.Text} (active)" : "Active filters";

                foreach (var (fg, items, recent) in filters)
                {
                    if (items.Count + (withRecent ? recent.Count : 0) == 0)
                        continue;

                    if (filters.Count > 1)
                        m.Items.Add(new MenuItem
                        {
                            Text = fg.Text,
                            TextAlign = SkiaSharp.SKTextAlign.Center,
                            TextScale = 0.8f,
                        });

                    m.Items.AddRange(items.Select(x => ToMenuItem(fg, x, isForeignFilterSet)));

                    if (withRecent && recent.Count > 0)
                    {
                        m.Items.Add(new MenuItem
                        {
                            Text = "Recently used:",
                            TextAlign = SkiaSharp.SKTextAlign.Left,
                            TextScale = 0.7f,
                        });

                        m.Items.AddRange(recent.Select(x => ToMenuItem(fg, x, isForeignFilterSet)));
                    }
                }
            }
        }

        public MenuItem ToMenuItem(FileFilterGroup fg) => new()
        {
            Text = fg.Text,
            Selected = () => GetActiveFilterTypes().Contains(fg),
            Submenu = m => FilterMenu(m, fg),
            LowerComment = $"{fg.Filters.Count}",
        };

        public void FilterMenu(MenuDef m, FileFilterGroup fg)
        {
            IEnumerable<FileFilter> tmp = fg.Filters.Values;
            tmp = fg.SortByAlpha ? (fg.AlphaSorter?.Invoke(tmp) ?? tmp.OrderBy(x => x.Text)) : tmp.OrderByDescending(x => x.Count);
            var activeFilters = fg.Filters.Values.Where(x => IsFilterActive(fg, x)).ToArray();
            var activeCount = activeFilters.Length;

            //m.Items.Add(new MenuItem()
            //{
            //    Text = activeCount == 0 ? "- no items selected -" : $"- clear {activeCount} / {fg.Filters.Count} items -",
            //    TextAlign = SkiaSharp.SKTextAlign.Center,
            //    OnClick = () =>
            //    {
            //        if (activeCount == 0)
            //            return;
            //        ToggleFilters(fg, activeFilters);
            //    }
            //});

            ActiveFiltersMenu(m, false, ActiveSettings.Filters.Where(x => x.Key == fg.GroupId).ToDictionary(x => x.Key, x => x.Value), withRecent: true);

            m.Items.AddRange(tmp.Select(x => ToMenuItem(fg, x, false)));

            m.Title = fg.Text;
            m.LargeButtons = fg.GroupId.EndsWith("folder");
            m.HeaderItems = new() { new MenuItem() { Icon = IconStore.SortUp, OnClick = () => fg.SortByAlpha = !fg.SortByAlpha } };
        }

        private MenuItem ToMenuItem(FileFilterGroup fg, FileFilter x, bool isForeignFilterSet)
        {
            return new MenuItem()
            {
                Text = x.Text,
                UpperComment = x.PreText,
                LowerComment = x.Count.ToString(),
                OnClick = () =>
                {
                    if (!isForeignFilterSet)
                        ToggleFilters(fg, x);
                },
                Selected = isForeignFilterSet ? null : () => IsFilterActive(fg, x)
            };
        }
    }

    public class FileFilterGroup
    {
        public string GroupId, Text;
        public Dictionary<string, FileFilter> Filters;
        public IEnumerable<FileFilter> FilterList { set { Filters = value.ToDictionary(x => x.Id); } }
        public bool SortByAlpha, IsNegated, HideInFilterList, SrcFilter;
        public Func<IEnumerable<FileFilter>, IEnumerable<FileFilter>> AlphaSorter;
        public int SortOrder;

        public FileFilterGroup Clone() => (FileFilterGroup)MemberwiseClone();
    }

    public class FileFilter
    {
        public required string Text, Id;
        public string PreText;
        public Func<FileRecord, bool> Filter;
        public Func<SourceRecord, bool> SrcFilter;
        public required int Count;
    }
}
