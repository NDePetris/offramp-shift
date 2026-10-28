using System.Globalization;

namespace OfframpShift.App;

// Conversion only; the dialog captures now once at confirmation. No scheduler.
internal static class PauseSelection
{
    internal static DateTime NextQuarterHour(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        var quarter = TimeSpan.FromMinutes(15).Ticks;
        // Advance the instant to the next local quarter, so a DST gap is skipped
        // and a fall-back default still refers to an actual future instant.
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTime(now.AddTicks(quarter - local.Ticks % quarter), zone).DateTime, DateTimeKind.Unspecified);
    }

    internal static string ZoneLabel(TimeZoneInfo zone) => "Local time · " +
        zone.StandardName.Replace("Standard Time", "Time", StringComparison.Ordinal);
    internal static DateTimeOffset? Deadline(int choice, DateTime? date, string time, TimeZoneInfo zone,
        TimeSpan? occurrence, DateTimeOffset now)
    {
        if (choice is >= 0 and <= 3) return now.ToUniversalTime().AddMinutes(new[] { 15, 30, 60, 120 }[choice]);
        if (choice == 5) return null;
        if (choice != 4) throw new ArgumentException("Choose when Pause should end.");
        var local = LocalTime(date, time);
        if (zone.IsInvalidTime(local)) throw new ArgumentException("This clock time does not exist because the clocks move forward. Choose another time.");
        DateTimeOffset utc;
        if (zone.IsAmbiguousTime(local))
        {
            if (occurrence is not { } offset || !zone.GetAmbiguousTimeOffsets(local).Contains(offset))
                throw new ArgumentException("This clock time occurs twice. Choose the first or second occurrence.");
            utc = new DateTimeOffset(local, offset).ToUniversalTime();
        }
        else utc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
        if (utc <= now) throw new ArgumentException("Choose a date and time in the future. Past times are not moved to tomorrow.");
        return utc;
    }

    internal static DateTime LocalTime(DateTime? date, string time)
    {
        if (date is null || !TimeOnly.TryParse(time, CultureInfo.CurrentCulture, DateTimeStyles.None, out var clock))
            throw new ArgumentException("Select a date and enter a valid local clock time.");
        return DateTime.SpecifyKind(date.Value.Date + clock.ToTimeSpan(), DateTimeKind.Unspecified);
    }

    internal static string Status(DateTimeOffset? deadline, DateTimeOffset now, TimeZoneInfo zone)
    {
        if (deadline is null) return "Paused until resumed";
        var end = TimeZoneInfo.ConvertTime(deadline.Value, zone);
        var today = TimeZoneInfo.ConvertTime(now, zone);
        var label = end.Date == today.Date ? end.ToString("t", CultureInfo.CurrentCulture) :
            end.Year == today.Year ? end.ToString("ddd, MMM d", CultureInfo.CurrentCulture) + " at " + end.ToString("t", CultureInfo.CurrentCulture) :
            end.ToString("g", CultureInfo.CurrentCulture);
        return "Paused until " + label;
    }
}
