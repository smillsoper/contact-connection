using ContactConnection.Domain.ValueObjects.Exports;
using Xunit;

namespace ContactConnection.Domain.Tests.Exports;

public class ExportScheduleTests
{
    private static readonly ExportSchedule Cannella = new()
    {
        Frequency = ExportFrequency.Daily, TimeOfDay = "21:30", TimeZone = "America/Los_Angeles", Window = ExportWindowKind.PreviousDay,
    };

    [Fact]
    public void Cannella_RunsAt930PmPacific_AndCoversThePreviousEasternDay()
    {
        // 2026-10-05 21:30 PDT = 2026-10-06 04:30Z = 00:30 EDT on the 6th — the Eastern day that just ended is the 5th.
        var runAt = ExportScheduleCalculator.Next(Cannella, new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero), 1).Single();
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 4, 30, 0, TimeSpan.Zero), runAt);
        var (start, end) = ExportScheduleCalculator.Window(Cannella, "America/New_York", runAt, null);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 4, 0, 0, TimeSpan.Zero), start);   // Oct 5 00:00 EDT
        Assert.Equal(new DateTimeOffset(2026, 10, 6, 4, 0, 0, TimeSpan.Zero), end);     // Oct 6 00:00 EDT
    }

    [Fact]
    public void RunTime_FollowsDaylightSaving()
    {
        // After the November change 21:30 PST is 05:30Z.
        var runAt = ExportScheduleCalculator.Next(Cannella, new DateTimeOffset(2026, 11, 10, 0, 0, 0, TimeSpan.Zero), 1).Single();
        Assert.Equal(new DateTimeOffset(2026, 11, 10, 5, 30, 0, TimeSpan.Zero), runAt);
    }

    [Fact]
    public void Occurrences_CatchUpEveryMissedRun_OldestFirst()
    {
        var after = new DateTimeOffset(2026, 10, 6, 4, 30, 0, TimeSpan.Zero);   // the 10/5 run already queued
        var until = new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
        var due = ExportScheduleCalculator.Occurrences(Cannella, after, until).ToList();
        Assert.Equal(3, due.Count);
        Assert.Equal(new DateTimeOffset(2026, 10, 7, 4, 30, 0, TimeSpan.Zero), due[0]);
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 4, 30, 0, TimeSpan.Zero), due[2]);
    }

    [Fact]
    public void Weekdays_Monthly_Week_Month_LastHours_SinceLastRun()
    {
        var weekdays = Cannella with { DaysOfWeek = [1, 2, 3, 4, 5] };
        var fri = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);   // Saturday UTC noon
        var next = ExportScheduleCalculator.Next(weekdays, fri, 1).Single();
        Assert.Equal(DayOfWeek.Monday, TimeZoneInfo.ConvertTime(next, TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles")).DayOfWeek);

        var monthly = Cannella with { Frequency = ExportFrequency.Monthly, DayOfMonth = 1, TimeOfDay = "03:00", Window = ExportWindowKind.PreviousMonth };
        var m = ExportScheduleCalculator.Next(monthly, new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero), 1).Single();
        var (ms, me) = ExportScheduleCalculator.Window(monthly, "America/New_York", m, null);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 4, 0, 0, TimeSpan.Zero), ms);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 4, 0, 0, TimeSpan.Zero), me);

        var weekly = Cannella with { Window = ExportWindowKind.PreviousWeek };
        var (ws, we) = ExportScheduleCalculator.Window(weekly, "America/New_York", new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero), null);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 4, 0, 0, TimeSpan.Zero), ws);   // Monday 9/28
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 4, 0, 0, TimeSpan.Zero), we);   // Monday 10/5

        var hours = Cannella with { Window = ExportWindowKind.LastHours, LastHours = 6 };
        var at = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal((at.AddHours(-6), at), ExportScheduleCalculator.Window(hours, "UTC", at, null));

        var since = Cannella with { Window = ExportWindowKind.SinceLastRun };
        var last = at.AddHours(-30);
        Assert.Equal((last, at), ExportScheduleCalculator.Window(since, "UTC", at, last));
        Assert.Equal((at.AddDays(-1), at), ExportScheduleCalculator.Window(since, "UTC", at, null));
    }

    [Fact]
    public void Validate_And_Describe()
    {
        Assert.Null(ExportScheduleCalculator.Validate(Cannella));
        Assert.NotNull(ExportScheduleCalculator.Validate(Cannella with { TimeOfDay = "9:30 PM" }));
        Assert.NotNull(ExportScheduleCalculator.Validate(Cannella with { Frequency = ExportFrequency.Monthly, DayOfMonth = 31 }));
        Assert.NotNull(ExportScheduleCalculator.Validate(Cannella with { TimeZone = "Mars/Olympus" }));
        Assert.Equal("Every day at 9:30 PM (America/Los_Angeles) — each file covers the previous day in America/New_York.",
            ExportScheduleCalculator.Describe(Cannella, "America/New_York"));
    }

    [Fact]
    public void DeliveryTarget_Validation()
    {
        var sftp = new ExportDeliveryTarget { Name = "Cannella", Type = ExportDeliveryType.Sftp, Host = "sftp.example.com", Username = "tems" };
        Assert.NotNull(sftp.Validate());                                   // no password / key
        Assert.Null((sftp with { PasswordCredential = "cannella-sftp" }).Validate());
        Assert.NotNull((sftp with { PasswordCredential = "x", Encryption = ExportEncryption.Pgp }).Validate());
        var email = new ExportDeliveryTarget { Name = "Ops", Type = ExportDeliveryType.Email, EmailTo = ["not-an-address"] };
        Assert.NotNull(email.Validate());
    }
}
