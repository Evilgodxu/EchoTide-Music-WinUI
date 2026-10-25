using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using YiChao.Ui.Theme;

namespace YiChao.Screens.Home.Component.Dialog;

/// <summary>
/// 曲库目录设置对话框：首次启动没有可用媒体库、或用户想改目录时使用。
///
/// 只负责收集目录，不碰扫描与存储——选目录的动作由调用方注入（需要窗口句柄），
/// 确认后的目录列表原样交回，扫描由视图模型统一发起。
/// 行内容在代码里组装而不是 DataTemplate：文案要跟随语言、配色要跟随主题，
/// 模板里写死的两处标签改不动。
/// </summary>
public sealed partial class LibrarySetupDialog : ContentDialog
{
    private readonly Func<Task<string?>> _pickFolder;
    private readonly List<string> _folders = new();

    public LibrarySetupDialog(Func<Task<string?>> pickFolder, IEnumerable<string> existingFolders)
    {
        _pickFolder = pickFolder;
        InitializeComponent();

        foreach (var folder in existingFolders)
        {
            AddFolder(folder);
        }

        ApplyText();
        ApplyTheme();
        RebuildRows();
    }

    /// <summary>确认后的目录列表；取消时调用方不应读取它。</summary>
    public IReadOnlyList<string> Folders => _folders.ToList();

    private void ApplyText()
    {
        Title = Strings.DialogTitle;
        PrimaryButtonText = Strings.DialogConfirm;
        CloseButtonText = Strings.DialogCancel;
        Hint.Text = Strings.DialogBody;
        EmptyHint.Text = Strings.DialogEmpty;
        AddButton.Content = Strings.DialogAdd;
    }

    private void ApplyTheme()
    {
        RequestedTheme = AppTheme.IsDark ? ElementTheme.Dark : ElementTheme.Light;
        var primary = new SolidColorBrush(AppTheme.OnSurface);
        var secondary = new SolidColorBrush(AppTheme.OnSurfaceVariant);

        Hint.Foreground = secondary;
        EmptyHint.Foreground = secondary;
        foreach (var row in FolderList.Children.OfType<Grid>())
        {
            foreach (var text in row.Children.OfType<TextBlock>())
            {
                text.Foreground = primary;
            }
        }
    }

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var path = await _pickFolder();
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        AddFolder(path);
        RebuildRows();
    }

    private void AddFolder(string path)
    {
        // 同一目录只保留一次：重复加入没有意义
        if (!_folders.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            _folders.Add(path);
        }
    }

    private void RebuildRows()
    {
        FolderList.Children.Clear();
        foreach (var folder in _folders.ToList())
        {
            FolderList.Children.Add(CreateRow(folder));
        }

        EmptyHint.Visibility = _folders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        IsPrimaryButtonEnabled = _folders.Count > 0;
        ApplyTheme();
    }

    private Grid CreateRow(string folder)
    {
        var row = new Grid { Padding = new Thickness(0, 2, 0, 2) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new TextBlock
        {
            Text = folder,
            FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(text);

        var remove = new Button
        {
            Content = Strings.DialogRemove,
            Tag = folder,
            FontSize = 12,
            Padding = new Thickness(8, 2, 8, 2),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        remove.Click += (_, _) =>
        {
            _folders.Remove(folder);
            RebuildRows();
        };
        Grid.SetColumn(remove, 1);
        row.Children.Add(remove);

        return row;
    }
}
