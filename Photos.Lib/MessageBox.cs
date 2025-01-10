using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Photos.Lib
{

    public class MessageBoxButton
    {
        public string Text;
        public Action Action;
    }
    public class MessageBox : Dialog
    {
        public Val<float> TextSize = new(-1);
        StackPanel _textPanel, _buttonPanel;

        public MessageBox()
        {
            _textPanel = new StackPanel()
            {
                Link = DialogContainer.Attach(() => DialogContainer.InnerRect)
            };

            _buttonPanel = new StackPanel()
            {
                Link = DialogContainer.Attach(() => DialogContainer.InnerRect with { Top = DialogContainer.InnerRect.Bottom - ButtonSize }),
                IsHorizontal = true,
                IsReversed = { Const = true }
            };

            DialogRect = () =>
            {
                DialogContainer.Layout(DialogContainer.Position); // temporary
                var buttonW = _buttonPanel.Children.Select(x => x.Size.Width + _buttonPanel.ItemMargin).Sum();
                var textW = _textPanel.Children.Select(c => ((TextLabel)c).MeasureText()).Max();
                var w = Math.Max(Math.Max(buttonW, textW) + 20, Parent.Size.Width / 6) + RenderPadding.TotalWidth;
                var h = (_textPanel.Children.Count * (TextSize + _textPanel.ItemMargin)) + ButtonSize + 100;

                var size = new SKSize(Math.Min(w, Size.Width), Math.Min(h, Size.Height));
                return CenterRect(size);
            };
        }

        public void Close()
        {
            Enabled.Const = false;
        }

        public void Show(string[] text, params MessageBoxButton[] options)
        {
            _textPanel.RemoveChildren(x => true);
            _buttonPanel.RemoveChildren(x => true);

            foreach (var item in text)
            {
                new TextLabel()
                {
                    Text = () => item,
                    DrawBorder = false,
                    Padding = { Const = 3 },
                    Link = _textPanel.Attach(() => TextSize)
                };
            }

            foreach (var item in options.Reverse())
            {
                var btn = new Button()
                {
                    Text = { Const = item.Text },
                    OnClick = () => { Enabled.Const = false; item.Action?.Invoke(); },
                    TextLabel = { TextAlign = { Const = SKTextAlign.Center } },
                };

                btn.Link = _buttonPanel.Attach(() => btn.MeasureWidth() + 3);
            }

            Enabled.Const = true;
            Invalidate();
        }
    }
}
