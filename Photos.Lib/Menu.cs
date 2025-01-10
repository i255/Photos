using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace Photos.Lib
{
    public class MenuItem
    {
        public string Text, UpperComment, LowerComment;
        public SKPath Icon;
        public Action OnClick;
        public Func<bool> Selected;
        public float? TextScale;
        public SKTextAlign? TextAlign;
        
        public MenuDef ReturnLevel;
        public Action<MenuDef> Submenu;

        public bool IsLabel => Submenu == null && OnClick == null && ReturnLevel == null;
    }

    public class MenuDef
    {
        internal MenuDef(MenuDef parent)
        {
            Parent = parent;
            Depth = parent == null ? -1 : parent.Depth + 1;
        }

        public string Title;
        public List<MenuItem> Items;
        public List<MenuItem> HeaderItems;

        public float SavedScrollPosition;

        public BaseView CustomContent;

        public bool LargeButtons, FitToSize;

        internal Action<MenuDef> Generator;

        public readonly MenuDef Parent;
        public readonly int Depth;
    }

    public class Menu : Dialog
    {
        public readonly TextLabel TitleLabel;
        TextLabel SectionLabel;
        public readonly Button MenuButton;

        //public float TextScale = 1f;
        public Val<int> ItemMargin => _replicator.ItemMargin;
        public Val<float> RightPadding = new (0);
        Replicator _replicator;
        ScrollContainer _scroll;
        TextLabel CommentLabel, PreTextLabel;

        public MenuDef ActiveMenu;
        public event Action<MenuItem> SectionClick;

        public bool ShowHeader = true;

        public Menu()
        {
            RightPadding.Func = () => _scroll.ScrollerWidth + RenderPadding.Right;

            var orig = KeyUpHandler;
            KeyUpHandler = e =>
            {
                if (e.Key == libKeys.Escape && ShowHeader)
                {
                    e.Consume();
                    StepBack();
                }
                else
                    orig?.Invoke(e);
            };

            _scroll = new ScrollContainer()
            {
                ScrollStep = { Func = () => ButtonSize },
                Padding = { Const = 0 },
                Link = DialogContainer.Attach(() =>
                {
                    var r = DialogContainer.InnerRect;
                    if (ShowHeader)
                        r.Top += (ItemMargin + ButtonSize);
                    if (_menuWidth != 0)
                        r.Left = r.Right - _menuWidth;
                    return r;
                }),
            };

            _replicator = new()
            {
                Link = _scroll.Attach(() => { var r = _scroll.OuterRect; r.Right -= RightPadding; return r; }),
                TileSize = { Func = () => new SKSize(_scroll.OuterRect.Width - RightPadding, ButtonSize * (ActiveMenu.LargeButtons ? 1.2f : 1f)) },
                ItemMargin = { Const = 6 },
                ItemsCount = { Func = () => ActiveMenu?.Items?.Count ?? 0 },
                Enabled = { Func = () => ActiveMenu?.CustomContent == null },
            };

            new Button()
            {
                Icon = { Const = IconStore.ArrowLeft },
                OnClick = StepBack,
                Link = DialogContainer.Attach(() => SKRect.Create(RenderPadding.Left, RenderPadding.Top, ButtonSize, ButtonSize)),
                Enabled = { Func = () => ShowHeader }
            };

            new Button() // TODO: support list
            {
                Icon = { Func = () => ActiveMenu?.HeaderItems[0].Icon },
                OnClick = () =>
                {
                    ActiveMenu.HeaderItems[0].OnClick(); 
                    ShowMenuInternal(ActiveMenu.Generator, ActiveMenu.Parent, ActiveMenu.Title, _scroll.ScrollOffsetY);
                },
                Link = DialogContainer.Attach(() => SKRect.Create(DialogContainer.Size.Width - ButtonSize - RenderPadding.Right, RenderPadding.Top, ButtonSize, ButtonSize)),
                Enabled = { Func = () => ShowHeader && ActiveMenu?.HeaderItems?.Count > 0 }
            };

            //new TextLabel() // # selected
            //{
            //    Parent = DialogContainer,
            //    Text = () => $"{ActiveMenu.Items.Where(x => x.Selected()).Count()}/{ActiveMenu.Items.Count}",
            //    Paint = { Color = Color.WithAlpha(0xA0), TextAlign = SKTextAlign.Right },
            //    Rect = () => DialogContainer.OuterRect.Select(x => new SKRect(x.MidX, Padding, x.Right - ButtonSize - Padding * 2, Padding + ButtonSize * 0.6f)),
            //    DrawBorder = false,
            //    Enabled = { Func = () => ActiveMenu.Items?.All(x => x.Selected != null) == true },
            //    Padding = { Const = 3 },
            //};

            TitleLabel = new TextLabel()
            {
                Text = () => " ".PadLeft(ActiveMenu.Depth + 1, '>') + ActiveMenu?.Title,
                Link = DialogContainer.Attach(() => SKRect.Create(ButtonSize + RenderPadding.Left * 2, RenderPadding.Top, DialogContainer.Size.Width - ButtonSize - RenderPadding.Right, ButtonSize)),
                DrawBorder = false,
                Padding = { Const = 3 },
                Enabled = { Func = () => ShowHeader }
            };

            var panel = new FreePanel()
            {
                Link = _replicator.Attach(),
            };

            MenuButton = new Button()
            {
                Link = panel.Attach(() => panel.OuterRect),
                Enabled = { Func = () => !CurItem.IsLabel },
                Text = { Func = () => CurItem.Text },
                OnClick = () => Click(CurItem),
                IsHighlighted = { Func = () => CurItem.Selected?.Invoke() == true },
                Padding = { Const = 5 },
                TextLabel = { TextAlign = { Func = () => CurItem.TextAlign ?? SKTextAlign.Left } },
                Icon = { Func = () => CurItem.Icon },
            };

            //MenuButton.TextSizeRatio.Adjust(x => ActiveMenu.LargeButtons ? x - 0.08f : x);
            MenuButton.TextLabel.TextScale.Adjust(GetTextScale);

            PreTextLabel = new TextLabel()
            {
                Enabled = { Func = () => CurItem.UpperComment != null },
                Text = () => CurItem.UpperComment,
                Link = panel.Attach(() => panel.OuterRect.Select(x => new SKRect(x.MidX, 0, x.Right - MenuButton.RenderPadding.Right, x.Bottom * 0.5f))),
                DrawBorder = false,
                TextAlign = { Const = SKTextAlign.Right },
            };
            PreTextLabel.Foreground.Color = PreTextLabel.Foreground.Color.WithAlpha(0xA0);

            CommentLabel = new TextLabel()
            {
                Enabled = { Func = () => CurItem.LowerComment != null },
                Text = () => CurItem.LowerComment,
                Link = panel.Attach(() => panel.OuterRect.Select(x => new SKRect(x.MidX, x.Bottom * 0.45f, x.Right - MenuButton.RenderPadding.Right, x.Bottom * 0.95f))),
                DrawBorder = false,
                TextAlign = { Const = SKTextAlign.Right },
            };
            CommentLabel.Foreground.Color = CommentLabel.Foreground.Color.WithAlpha(0x60);

            SectionLabel = new TextLabel()
            {
                Enabled = { Func = () => CurItem.IsLabel },
                Text = () => CurItem.Text,
                Link = panel.Attach(() => panel.OuterRect),
                DrawBorder = false,
                TextAlign = { Func = () => CurItem.TextAlign ?? SKTextAlign.Center }
            };

            SectionLabel.TextScale.Adjust(GetTextScale);

            SectionLabel.MouseHandler = e => SectionLabel.DetectClick(e, () => SectionClick?.Invoke(CurItem));

            float GetTextScale(float x) => x * (ActiveMenu.LargeButtons ? 0.8f : 1f) * (CurItem.TextScale ?? 1f);

            _scroll.OnAfterLayout += () =>
            {
                if (ActiveMenu != null && ActiveMenu.SavedScrollPosition != 0)
                {
                    _scroll.ScrollOffsetY = ActiveMenu.SavedScrollPosition;
                    ActiveMenu.SavedScrollPosition = 0;
                    _scroll.Layout(_scroll.Position);
                }
            };
        }

        MenuItem CurItem => ActiveMenu.Items[_replicator.CurrentIndex];
        float _menuWidth = 0;

        public void Show(Action<MenuDef> menuFunc) => ShowMenuInternal(menuFunc, CloseMenuLevel, null, 0);

        public readonly static MenuDef CloseMenuLevel = new (null); // not in stack
        void ShowMenuInternal(Action<MenuDef> menuFunc, MenuDef parentMenu, string title, float scrollPos)
        {
            var def = new MenuDef(parentMenu)
            {
                Items = new(),
                Generator = menuFunc,
                Title = title,
                SavedScrollPosition = scrollPos
            };
            menuFunc(def);

            if (ActiveMenu != null)
                ActiveMenu.SavedScrollPosition = _scroll.ScrollOffsetY;
            ActiveMenu = def;

            if (def.CustomContent != null)
                def.CustomContent.Link = _scroll.Attach(() => _scroll.InnerRect);

            _menuWidth = 0;
            if (ActiveMenu.FitToSize)
            {
                Action onLayout = () => _menuWidth = Math.Max(_menuWidth, MenuButton.MeasureWidth());
                MenuButton.OnAfterLayout += onLayout;
                try { _replicator.ForceFullLayout(); }
                finally { MenuButton.OnAfterLayout -= onLayout; }
            }

            _scroll.ScrollOffsetY = 0;

            Enabled.Const = true;
            Invalidate();
        }

        void Click(MenuItem item)
        {
            if (item.IsLabel)
                return;    

            item.OnClick?.Invoke();
            if (item.ReturnLevel != null)
            {
                if (item.Submenu != null)
                    throw new InvalidOperationException();

                while (true)
                {
                    if (ActiveMenu == null || ActiveMenu.Parent == item.ReturnLevel || ActiveMenu.Parent == CloseMenuLevel)
                    {
                        StepBack();
                        break;
                    }
                    else
                        ActiveMenu = ActiveMenu.Parent;
                }
            }
            else if (item.Submenu != null)
                ShowMenuInternal(item.Submenu, ActiveMenu, item.Text, 0);
            else
                ShowMenuInternal(ActiveMenu.Generator, ActiveMenu.Parent, ActiveMenu.Title, _scroll.ScrollOffsetY);
        }

        void StepBack()
        {
            _scroll.RemoveChildren(x => x == ActiveMenu?.CustomContent);
            
            if (ActiveMenu?.Parent != null && ActiveMenu.Parent != CloseMenuLevel)
            {
                var parent = ActiveMenu.Parent;
                ShowMenuInternal(parent.Generator, parent.Parent, parent.Title, parent.SavedScrollPosition);
            }
            else
                Close();
        }

        public void Close()
        {
            _scroll.RemoveChildren(x => x == ActiveMenu?.CustomContent);
            ActiveMenu = null;
            Enabled.Const = false;
        }

        public static void Confirm(MenuDef menu, string okText, Action ok, string[] expl = null, MenuDef okReturnLevel = null)
        {
            okReturnLevel ??= menu.Parent;
            expl ??= Array.Empty<string>();

            menu.Items.AddRange(expl.Select(x => new MenuItem() { Text = x, TextAlign = SKTextAlign.Left, TextScale = 0.7f }));

            if (menu.Items.Count > 0)
                menu.Items.Add(new MenuItem());

            menu.Items.AddRange(new[] {
                new MenuItem() { Text = okText, OnClick = ok, ReturnLevel = okReturnLevel },
                new MenuItem() { Text = $"Cancel", ReturnLevel = menu.Parent }
            });
        }

    }
}
