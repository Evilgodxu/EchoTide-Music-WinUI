using System;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace YiChao.Screens.Home.Component.Playlist;

/// <summary>
/// 列表行副标题：歌词搜索命中时以强调色加粗标出关键词，其余文本保持行内暗色。
///
/// 用内联文本而非纯文本属性，使命中位置可以单独着色；未命中关键词时整段同色，
/// 与默认的艺术家副标题表现一致。
/// </summary>
public sealed class PlaylistSubtitle : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text),
        typeof(string),
        typeof(PlaylistSubtitle),
        new PropertyMetadata(string.Empty, OnContentChanged));

    public static readonly DependencyProperty HighlightProperty = DependencyProperty.Register(
        nameof(Highlight),
        typeof(string),
        typeof(PlaylistSubtitle),
        new PropertyMetadata(null, OnContentChanged));

    private static readonly SolidColorBrush HighlightBrush =
        new(Windows.UI.Color.FromArgb(0xFF, 0x7C, 0xC4, 0xFF));

    private static readonly SolidColorBrush SubtitleBrush =
        new(Windows.UI.Color.FromArgb(0x8C, 0xFF, 0xFF, 0xFF));

    private readonly TextBlock _presenter = new()
    {
        FontSize = 12,
        Foreground = SubtitleBrush,
        MaxLines = 1,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public PlaylistSubtitle()
    {
        Content = _presenter;
        Render();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>需要强调的关键词；为空即整段同色。大小写不敏感，可多处命中。</summary>
    public string? Highlight
    {
        get => (string?)GetValue(HighlightProperty);
        set => SetValue(HighlightProperty, value);
    }

    private static void OnContentChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((PlaylistSubtitle)sender).Render();

    private void Render()
    {
        _presenter.Inlines.Clear();
        var text = Text;
        var keyword = Highlight;
        if (string.IsNullOrEmpty(keyword))
        {
            _presenter.Inlines.Add(new Run { Text = text });
            return;
        }

        var cursor = 0;
        while (cursor < text.Length)
        {
            var hit = text.IndexOf(keyword, cursor, StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                _presenter.Inlines.Add(new Run { Text = text[cursor..] });
                break;
            }

            if (hit > cursor)
            {
                _presenter.Inlines.Add(new Run { Text = text[cursor..hit] });
            }

            _presenter.Inlines.Add(new Run
            {
                Text = text.Substring(hit, keyword.Length),
                Foreground = HighlightBrush,
                FontWeight = FontWeights.SemiBold,
            });
            cursor = hit + keyword.Length;
        }
    }
}
