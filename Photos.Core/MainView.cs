using Photos.Lib;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Photos.Core
{
    public partial class MainView : FreePanel
    {
        public static string Version = typeof(MainView).Assembly.GetName().Version.ToString();
        public static string Copyright = "Copyright (c) 2022-2025 Vadim Lapiner";

        PhotoView _photoView;
        public readonly ThumbnailView ThumbnailView;
        Menu mainMenu;
        public readonly Toast MainToast;
        readonly MessageBox _mbox;
        public MessageBox MessageBox => _mbox;
        public Menu MainMenu => mainMenu;

        public readonly Val<float> MenuButtonSize = new(-1);
        public readonly Val<float> ButtonSize = new(-1);
        public readonly Val<float> TextLabelSize = new(-1);
        public static int ButtonPadding = 2;
        public float BigButtonSize => (ButtonSize * 3 + ButtonPadding) / 2;
        public Val<float> WorkStatusLabelHeight = new(-1);
        private readonly PhotoProvider photoProvider;
        public readonly StackPanel ButtonPanel;
        readonly StackPanel WindowControlPanel;

        public MainView(PhotoProvider photoProvider)
        {
            MenuButtonSize.Func = () => 38 * Window.ScaleFactor;
            ButtonSize.Func = () => 30 * Window.ScaleFactor;
            TextLabelSize.Func = () => 32 * Window.ScaleFactor;
            WorkStatusLabelHeight.Func = () => 28 * Window.ScaleFactor;

            //var asm = GetType().Assembly;
            //DefaultTypeface = SKTypeface.FromStream(asm.GetManifestResourceStream(asm.GetManifestResourceNames().First(x => x.EndsWith(".ttf"))));

            OnDispose += () => { photoProvider.Dispose(); Utils.Trace("Dispose"); };

            Padding.Const = 0;
            //Link = Attach(() => SKRect.Create(Window.ClientSize());


            _photoView = new(photoProvider)
            {
                Link = Attach(() => OuterRect),
                Padding = { Func = () => Padding },
                ButtonPanelButtonSize = { Func = () => BigButtonSize }
            };
            _photoView.OnBackPressed += SwitchView;
            _photoView.Enabled.Func = () => !ThumbnailView.Enabled;

            ThumbnailView = new(photoProvider, ButtonSize)
            {
                Link = Attach(() => InnerRect),
                RightImageMargin = { Func = () => (BigButtonSize + ButtonPadding) },
                ScrollContainer = { Padding = { Func = () => ButtonPanel.Position.Bottom + ButtonPadding - ThumbnailView.Position.Top } }
            };

            photoProvider.RequestUILock = wait =>
            {
                var lockAquired = new TaskCompletionSource();

                Task.Factory.StartNew(() => Window.Run(() =>
                {
                    lockAquired.SetResult();
                    wait.Wait();
                }), TaskCreationOptions.LongRunning);

                return lockAquired.Task;
            };

            photoProvider.PhotoSelected += () => ThumbnailView.Enabled.Const = false;
            _photoView.MenuButton.AdjustRect(x => x.OffsetRect(y: (WindowControlPanel.Enabled ? WindowControlPanel.Position.Bottom + ButtonPadding : 0)));

            new TextLabel() // selectedItems
            {
                Link = Attach(() => SKRect.Create(0, Padding.Value.Top + ButtonSize + (BigButtonSize - TextLabelSize) / 2 + ButtonPadding, 
                    InnerRect.Width - ButtonSize * 3 - ButtonPadding * 3, TextLabelSize)),
                Padding = { Const = 3 },
                Text = () => $"{ThumbnailView.SelectedItems.Count} items selected ",
                TextAlign = { Const = SKTextAlign.Right },
                Background = { Color = SKColors.Black },
                Enabled = { Func = () => ThumbnailView.Enabled && ThumbnailView.SelectedItems.Count > 0 },
            };

            ButtonPanel = new StackPanel()
            {
                MeasureChild = x => BigButtonSize,
                ItemMargin = { Func = () => ButtonPadding },
                Link = Attach(() => SKRect.Create(InnerRect.Right - BigButtonSize, (WindowControlPanel.Enabled ? WindowControlPanel.Position.Bottom : InnerRect.Top) + ButtonPadding,
                    BigButtonSize, (BigButtonSize + ButtonPadding) * 4 - ButtonPadding)),
                Enabled = { Func = () => !photoProvider.IsInitRunning && ThumbnailView.Enabled },
            };

            new Button() // clear selection
            {
                Link = ButtonPanel.Attach(),
                Icon = { Const = IconStore.X },
                OnClick = () => ThumbnailView.SelectedItems.Clear(),
                Enabled = { Func = () => ThumbnailView.SelectionActive }
            };

            new Button() // return to gallery
            {
                Link = ButtonPanel.Attach(),
                Icon = { Const = IconStore.ArrowLeft },
                Enabled = { Func = () => photoProvider.IsSingleFolderMode && !ThumbnailView.SelectionActive },
                OnClick = () => photoProvider.CloseSingleFolderMode(),
            };

            new Button() // menu
            {
                Link = ButtonPanel.Attach(),
                Icon = { Const = IconStore.List },
                OnClick = () => mainMenu.Show(MainMenuFunc),
                Enabled = { Func = () => !ThumbnailView.SelectionActive }
            };

            new Button()
            {
                Link = ButtonPanel.Attach(),
                Icon = { Const = IconStore.Folder },
                OnClick = () => mainMenu.Show(m => photoProvider.FilterService.FilterMenu(m, photoProvider.FilterService.GetFilterGroup(FilterService.FolderFilter))),
                Enabled = { Func = () => photoProvider.FilterService.FiltersReady && !photoProvider.IsSingleFolderMode && !ThumbnailView.SelectionActive },
                IsHighlighted = { Func = () => photoProvider.FilterService.IsFilterGroupActive(FilterService.FolderFilter) }
            };

            new Button()
            {
                Link = ButtonPanel.Attach(),
                Icon = { Const = IconStore.Funnel },
                OnClick = () => ShowFilterMenu(photoProvider.FilterService.FilterGroups.Where(x => !x.HideInFilterList).ToArray()),
                Enabled = { Func = () => photoProvider.FilterService.FiltersReady && !ThumbnailView.SelectionActive },
                IsHighlighted = { Func = () => photoProvider.FilterService.GetActiveFilterTypes().Any(x => !x.HideInFilterList) }
            };

            new Button()
            {
                Link = ButtonPanel.Attach(),
                Icon = { Const = IconStore.Star },
                OnClick = () =>
                {
                    var fgr = photoProvider.FilterService.GetFilterGroup(FilterService.FavFilter);
                    photoProvider.FilterService.ToggleFilters(fgr, fgr.Filters.Values.Single());
                },
                Enabled = { Func = () => photoProvider.FilterService.FiltersReady && !ThumbnailView.SelectionActive },
                IsHighlighted = { Func = () => photoProvider.FilterService.IsFilterGroupActive(FilterService.FavFilter) }
            };

            new TextLabel()
            {
                Text = getPlaceholderText,
                Link = Attach(() => CenterRect(new SKSize(0, 40))),
                Padding = { Const = 3 },
                TextAlign = { Const = SKTextAlign.Center },
                Enabled = { Func = () => !_mbox.Enabled && !mainMenu.Enabled && getPlaceholderText() != null },
                DrawBorder = false,
            };

            string getPlaceholderText()
            {
                if (photoProvider.IsInitRunning)
                    return "... loading ...";
                else if (EmptyLibrary)
                    return "Click Menu -> 'Photo library settings' to add pictures";
                return null;
            }

            mainMenu = new()
            {
                Link = Attach(() => OuterRect),
                DialogRect = () => CenterRect(new SKSize((int)Math.Min(Size.Width, Size.Height * 1.1), Size.Height * 3 / 4)),
                ButtonSize = { Func = () => MenuButtonSize }
            };

            mainMenu.SectionClick += MainMenu_SectionClick;
            _photoView.MainMenu = mainMenu;

            var statusToast = new Toast()
            {
                Text = () => photoProvider.Indexer.WorkStatus,
                Link = ThumbnailView.Attach(() => ThumbnailView.Position.Select(x => SKRect.Create(0, x.Height - WorkStatusLabelHeight * 1.3f, x.Width, WorkStatusLabelHeight))),
                Padding = { Const = 1 },
                TextAlign = { Const = SKTextAlign.Center },
                //Enabled = { Func = () => photoProvider.WorkStatus != null && ThumbnailView.Enabled}
            };
            statusToast.Enabled.Adjust(x => x && photoProvider.Indexer.WorkStatus != null);
            photoProvider.Indexer.OnIndexing += indexing => Window?.Run(() => statusToast.Show(indexing ? float.PositiveInfinity : 5f));

            MainToast = new Toast()
            {
                Link = Attach(() => ThumbnailView.Position.Select(x => SKRect.Create(0, 30, x.Width, WorkStatusLabelHeight * 1.2f))),
            };

            new TextLabel()
            {
                Text = () => Window.GetRenderStats(),
                Link = Attach(() => InnerRect.Select(x => SKRect.Create(0, x.Bottom - 35, x.Width - 10, 25))),
                Padding = { Const = 3 },
                TextAlign = { Const = SKTextAlign.Right },
                Enabled = { Func = () => photoProvider.DB.Settings.DevMode }
            };


            _mbox = new()
            {
                Link = Attach(() => OuterRect),
                ButtonSize = { Func = () => MenuButtonSize },
                TextSize = { Func = () => ButtonSize }
            };

            WindowControlPanel = new StackPanel()
            {
                MeasureChild = x => ButtonSize,
                ItemMargin = { Func = () => ButtonPadding },
                Link = Attach(() => InnerRect.OffsetEdge(bottom: ButtonSize -InnerRect.Height)), //SKRect.Create(0, ButtonPadding, Size.Width, ButtonSize)),
                Enabled = { Func = () => Window.IsBorderless() },
                IsHorizontal = true,
                IsReversed = { Const = true },
            };

            _photoView.StyleButton(new Button() // close
            {
                Link = WindowControlPanel.Attach(),
                Icon = { Const = IconStore.X },
                OnClick = () => Window.Close(),
                Padding = { Const = 1 },
            });

            _photoView.StyleButton(new Button() // set normal
            {
                Link = WindowControlPanel.Attach(),
                Icon = { Const = IconStore.MyArrowDownLeft },
                OnClick = () => Window.Minimize(false),
                Padding = { Const = 1 },
            });

            _photoView.StyleButton(new Button() // minimize
            {
                Link = WindowControlPanel.Attach(),
                Icon = { Const = IconStore.Dash },
                OnClick = () => Window.Minimize(true),
                Padding = { Const = 1 },
            });

            this.photoProvider = photoProvider;
            KeyUpHandler = KeyUp;
        }

        public override void Init()
        {
            photoProvider.Indexer.FirstIndexTask.ContinueWith(x => Window.Run(() => ShowWelcomeBox())); // wait for init to finish

            photoProvider.OnDisplayOrderUpdate += () => Window.Run(() =>
            {
                ThumbnailView.SelectedItems.Clear(); 
                Window.Invalidate();
            });

            if (photoProvider.IsSingleFolderMode)
                SwitchView(); // showselected pic

            //MessageBox.Show(new[] { "test", "test test test test test test test test test test test test test test test test test ", "test" }, new () { Text = "test" }, new () { Text = "test testtest" });
            Window.RenderFinished += x => x.PrintUtcMs("render", 50, true);

            if (Window.TotalMemoryHintKB == 0)
            {
                try
                {
                    var memFile = "/proc/meminfo";
                    if (File.Exists(memFile))
                    {
                        var match = Regex.Match(File.ReadAllLines(memFile).First(x => x.StartsWith("MemTotal:")), @"\d+");
                        if (match.Success)
                            Window.TotalMemoryHintKB = long.Parse(match.Value);
                    }
                }
                catch { }

                if (Window.TotalMemoryHintKB == 0)
                    Window.TotalMemoryHintKB = 2L << 20; // assume at least 2GB
            }

            RenderFrameHandler = ctx =>
            {
                if (!_limitSet)
                {
                    _limitSet = true;
                    Window.GR.SetResourceCacheLimit(Math.Clamp(Window.TotalMemoryHintKB * 1024 / 4, 1L << 29, 4L << 30)); // 1/4 if mem between 512mb and 4096mb
                }

                ctx.Clear(BackgroundColor());
            };

            UpdatesCheck();
        }

        public Func<SKColor> BackgroundColor = () => SKColors.Black;

        bool _limitSet;

        async void UpdatesCheck()
        {
            if (Debugger.IsAttached)
                return;

            try
            {
                var thisVer = Version;
                using var httpClient = new HttpClient();
                var srvVer = await httpClient.GetStringAsync($"https://www.bytificial.com/version.txt?app_ver={thisVer}&db_ver={photoProvider.DB.Settings.CreationTimeUtc / 1000000}");
                long toInt(string s) => s.Split(".").Select(long.Parse).Aggregate((x, y) => x * 1000 + y);

                if (toInt(srvVer) > toInt(thisVer) && !photoProvider.DB.Settings.UpdatesNotificationOptOut)
                    Window.Run(() => MainToast.Show($"A newer version of {Utils.AppName} is available", 5));
            }
            catch (Exception)
            {
                //Utils.LogError(e);
            }
        }

        bool EmptyLibrary => photoProvider.FolderLibraryMode && photoProvider.GetDirs().Count == 0 && photoProvider.TotalCount == 0;

        private void ShowWelcomeBox()
        {
            if (EmptyLibrary)
                _mbox.Show(new[] {
                    "Your photo library is empty",
                    //"Please add a folder with photos to the library"
                    "Would you like to add your user directory to the library?"
                    },
                new MessageBoxButton()
                {
                    Text = "Yes",
                    Action = () => photoProvider.AddDir(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
                },
                new MessageBoxButton() { Text = "Choose another folder", Action = () => mainMenu.Show(PhotoLibMenu) },
                new MessageBoxButton() { Text = "Cancel", Action = () => { } }
                );
        }

        void KeyUp(libKeyMessage e)
        {
            if (e.Key == libKeys.Escape)
            {
                e.Consume();
                GoBack();
            }
        }

        private void GoBack()
        {
            //if (_mbox.Enabled)
            //{ 
            //    // TODO: cancel?
            //}
            //else if (mainMenu.Enabled)
            //    mainMenu.StepBack();
            //else 
            if (ThumbnailView.Enabled)
                Window.Close();
            else if (_photoView.Enabled)
                _photoView.GoBack();
        }

        private void SwitchView()
        {
            ThumbnailView.Enabled.Const = !ThumbnailView.Enabled;
        }

    }
}
