using Microsoft.UI.Xaml;
namespace SeminarSched_WinUI.Pages;
public sealed partial class ImportPage : WorkflowPageBase { public ImportPage() => InitializeComponent(); private void Page_Loaded(object sender, RoutedEventArgs e) => EnsureProject(ProjectRequired); }
