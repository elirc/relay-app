using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Relay.Infrastructure.Persistence;

/// <summary>
/// SQLite cannot order or compare <see cref="DateTimeOffset"/> columns, so we
/// persist them as UTC ticks (a <see cref="long"/>). Ticks sort identically to
/// the original instants, keeping ORDER BY / range queries correct.
/// </summary>
public sealed class DateTimeOffsetToUtcTicksConverter : ValueConverter<DateTimeOffset, long>
{
    public DateTimeOffsetToUtcTicksConverter()
        : base(
            value => value.UtcTicks,
            ticks => new DateTimeOffset(ticks, TimeSpan.Zero))
    {
    }
}
