using CommunityToolkit.Mvvm.ComponentModel;

namespace TensaZangetsu.App.ViewModels;

public partial class OtaPhoneModelCard(string name, string code, string versions, string brand, string badgeColor) : ObservableObject
{
    public string Name { get; } = name;
    public string Code { get; } = code;
    public string Versions { get; } = versions;
    public string Brand { get; } = brand;
    public string BadgeColor { get; } = badgeColor;
}

public partial class NavItem(string id, string title, string icon, string category) : ObservableObject
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public string Icon { get; } = icon;
    public string Category { get; } = category;
}
