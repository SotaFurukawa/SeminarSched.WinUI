using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SeminarSched.Application.Scheduling;
namespace SeminarSched_WinUI.Pages;
public sealed partial class PreconfirmationPage : WorkflowPageBase
{
    public PreconfirmationPage()=>InitializeComponent();
    private async void Page_Loaded(object sender,RoutedEventArgs e){AddButton.IsEnabled=EnsureProject(ProjectRequired);if(AddButton.IsEnabled)await Reload();}
    private async Task Reload(){var p=App.ProjectService.Current!.Path;Requests.ItemsSource=await App.FixedLessons.GetRequestsAsync(p);Teachers.ItemsSource=await App.FixedLessons.GetTeachersAsync(p);Slots.ItemsSource=await App.FixedLessons.GetSlotsAsync(p);Fixed.ItemsSource=await App.FixedLessons.GetFixedLessonsAsync(p);}
    private async void Add_Click(object sender,RoutedEventArgs e){if(Requests.SelectedItem is not LessonRequestOption r||Teachers.SelectedItem is not TeacherOption t||Slots.SelectedItem is not ScheduleSlotOption s){Show(InfoBarSeverity.Warning,"すべて選択してください","");return;}try{IsEnabled=false;await App.FixedLessons.AddAsync(App.ProjectService.Current!.Path,r.Id,t.Id,s.OpenDateId,s.TimeSlotId);await Reload();Show(InfoBarSeverity.Success,"固定しました","");}catch(Exception ex)when(ex is InvalidOperationException or Microsoft.Data.Sqlite.SqliteException){Show(InfoBarSeverity.Error,"固定できませんでした",ex.Message);}finally{IsEnabled=true;}}
    private async void Remove_Click(object sender,RoutedEventArgs e){if(Fixed.SelectedItem is not FixedLesson f)return;try{IsEnabled=false;await App.FixedLessons.RemoveAsync(App.ProjectService.Current!.Path,f.Id);await Reload();Show(InfoBarSeverity.Success,"固定を解除しました","");}finally{IsEnabled=true;}}
    private void Show(InfoBarSeverity severity,string title,string message){Status.Severity=severity;Status.Title=title;Status.Message=message;Status.IsOpen=true;}
}
