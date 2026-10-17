using System.Text.Json;
using System.Text.Json.Serialization;

namespace FinanceApi.Mapping;

/// <summary>
/// Reads a date with an offset as the client wrote it: "2026-03-01T23:00-05:00" is March 1st. The default turns it into
/// the server's local time (March 2nd, 04:00 in UTC), and the inputs keep only the day. Written as by default.
/// </summary>
public sealed class CalendarDateConverter : JsonConverter<DateTime>
{
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (!reader.TryGetDateTime(out var value))
        {
            throw new JsonException();
        }
        // Local: it had an offset other than "Z".
        return value.Kind == DateTimeKind.Local ? reader.GetDateTimeOffset().DateTime : value;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
