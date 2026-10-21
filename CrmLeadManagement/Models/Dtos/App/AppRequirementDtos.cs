using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrmLeadManagement.Models.Dtos.App;

/// <summary>Accepts both "1200" and 1200 from the mobile app and stores it as string.</summary>
public class FlexibleStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l)
                ? l.ToString()
                : reader.GetDecimal().ToString(System.Globalization.CultureInfo.InvariantCulture),
            JsonTokenType.True => "true",
            JsonTokenType.False => "false",
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token {reader.TokenType} for string value.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

/// <summary>One row of the "Item Specifications" table.</summary>
public class AppRequirementItemDto
{
    [JsonConverter(typeof(FlexibleStringConverter))] public string? Category { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? Item { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? Location { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))] public string? H1 { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? H2 { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? H3 { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))] public string? W1 { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? W2 { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? W3 { get; set; }

    [JsonConverter(typeof(FlexibleStringConverter))] public string? Types { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? Topology { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? Glass { get; set; }

    /// <summary>Photo as base64, with or without "data:image/jpeg;base64," prefix.</summary>
    public string? ImageBase64 { get; set; }
}

public class AppRequirementInputDto
{
    // Basic requirement details (same as web form header)
    [JsonConverter(typeof(FlexibleStringConverter))] public string? Title { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? Description { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? Priority { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? ExpectedDate { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? Budget { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? Status { get; set; }

    // Legacy single-item fields (old app versions keep working)
    [JsonConverter(typeof(FlexibleStringConverter))] public string? RequirementType { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? ItemName { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? Quantity { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? Details { get; set; }
    [JsonConverter(typeof(FlexibleStringConverter))] public string? Remarks { get; set; }

    /// <summary>New: item specification rows. Each row is saved as one RequirementEntry (same as web).</summary>
    public List<AppRequirementItemDto>? Items { get; set; }
}
