using Microsoft.UI.Xaml;
namespace SeminarSched_WinUI.Pages;
public sealed partial class OutputPage : WorkflowPageBase { public OutputPage() => InitializeComponent(); private void Page_Loaded(object sender, RoutedEventArgs e) => EnsureProject(ProjectRequired); }
