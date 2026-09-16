using Microsoft.UI.Xaml.Controls;
using SeminarSched.Application;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SeminarSched_WinUI.Pages;

public sealed partial class AboutPage : Page
{
    public string VersionText { get; } = ApplicationVersion
        .FromAssembly(typeof(App).Assembly)
        .DisplayVersion;

    public AboutPage()
    {
        InitializeComponent();
    }
}
