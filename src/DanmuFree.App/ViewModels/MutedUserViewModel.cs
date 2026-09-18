using CommunityToolkit.Mvvm.ComponentModel;

namespace DanmuFree.App.ViewModels;

/// <summary>
/// 「不念用户」黑名单的一行（朗读 TAB 可编辑列表）：只承载用户名，
/// 由 <see cref="DanmuViewModel"/> 订阅变更并重建匹配集合。
/// </summary>
public partial class MutedUserViewModel : ObservableObject
{
    [ObservableProperty] private string _userName = "";
}
