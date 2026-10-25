using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace YiChao.Screens.Home;

/// <summary>
/// 首页可变状态：曲库扫描进度与空库提示。曲目与播放状态不在此处，它们由播放服务持有。
/// </summary>
public sealed class HomeUiState : INotifyPropertyChanged
{
    private bool _isScanning;
    private string _statusText = string.Empty;
    private bool _isEmpty = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsScanning
    {
        get => _isScanning;
        set => Set(ref _isScanning, value);
    }

    /// <summary>扫描进度或空库指引；为空表示无需提示。</summary>
    public string StatusText
    {
        get => _statusText;
        set => Set(ref _statusText, value);
    }

    /// <summary>无任何曲目：此时显示选择文件夹的入口。</summary>
    public bool IsEmpty
    {
        get => _isEmpty;
        set => Set(ref _isEmpty, value);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
