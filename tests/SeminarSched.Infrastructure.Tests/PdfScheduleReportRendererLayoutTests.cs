using MigraDoc.DocumentObjectModel;

namespace SeminarSched.Infrastructure.Tests;

public sealed class PdfScheduleReportRendererLayoutTests
{
    [Fact]
    public void WeeklyCalendarColumnWidth_FitsWithinA4LandscapeUsableWidth()
    {
        // PdfScheduleReportRenderer.AddCalendar lays out a fixed 7-column weekly table at 3.5cm/column
        // (24.5cm total) on an A4 landscape section with no explicit margin override, i.e. MigraDoc's
        // defaults apply. This pins those environment assumptions so a MigraDoc or page-format change
        // that would push the table into the margin (as 3.6cm/column once did, at 25.2cm > 24.7cm
        // usable width) is caught here instead of silently at print time.
        var defaults = PageSetup.DefaultPageSetup;
        Assert.Equal(PageFormat.A4, defaults.PageFormat);
        var landscapeUsableWidthCm = defaults.PageHeight.Centimeter - defaults.LeftMargin.Centimeter - defaults.RightMargin.Centimeter;
        const double calendarTableWidthCm = 7 * 3.5;
        Assert.True(calendarTableWidthCm <= landscapeUsableWidthCm,
            $"7-column weekly calendar ({calendarTableWidthCm}cm) must fit within the A4 landscape usable width ({landscapeUsableWidthCm}cm).");
    }
}
