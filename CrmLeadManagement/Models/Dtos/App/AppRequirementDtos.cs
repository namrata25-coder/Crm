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

/// <summary>Response DTO returning item specifications as clean individual properties rather than squashed in Description.</summary>
public class AppRequirementResponseDto
{
    public int Id { get; set; }
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Priority { get; set; }
    public string? ExpectedDate { get; set; }
    public string? Budget { get; set; }
    public string? Status { get; set; }
    public string? ImagePath { get; set; }

    // Same params as web panel
    public string? Category { get; set; }
    public string? Item { get; set; }
    public string? Location { get; set; }
    public string? H1 { get; set; }
    public string? H2 { get; set; }
    public string? H3 { get; set; }
    public string? W1 { get; set; }
    public string? W2 { get; set; }
    public string? W3 { get; set; }
    public string? Types { get; set; }
    public string? Topology { get; set; }
    public string? Glass { get; set; }

    // Legacy fields
    public string? RequirementType { get; set; }
    public string? ItemName { get; set; }
    public string? Quantity { get; set; }
    public string? Details { get; set; }
    public string? Remarks { get; set; }

    public static AppRequirementResponseDto FromEntity(RequirementEntry entity)
    {
        var rawDesc = entity.Description ?? "";
        var looksLikeSpecs = rawDesc.Contains("Location:", StringComparison.OrdinalIgnoreCase) ||
                             rawDesc.Contains("Height:", StringComparison.OrdinalIgnoreCase) ||
                             rawDesc.Contains("Width:", StringComparison.OrdinalIgnoreCase);

        // If Description was legacy-squashed, restore original notes from Remarks
        var effectiveTitle = !string.IsNullOrWhiteSpace(entity.Title) ? entity.Title : (entity.ItemName ?? "");
        var effectiveDesc = looksLikeSpecs && !string.IsNullOrWhiteSpace(entity.Remarks)
            ? entity.Remarks
            : entity.Description;

        var dto = new AppRequirementResponseDto
        {
            Id = entity.Id,
            Title = effectiveTitle,
            Description = effectiveDesc,
            Priority = entity.Priority,
            ExpectedDate = entity.ExpectedDate,
            Budget = entity.Budget,
            Status = entity.Status,
            ImagePath = entity.ImagePath,

            Category = !string.IsNullOrWhiteSpace(entity.Category) ? entity.Category : entity.RequirementType,
            Item = !string.IsNullOrWhiteSpace(entity.Item) ? entity.Item : entity.ItemName,
            Location = entity.Location,
            H1 = entity.H1,
            H2 = entity.H2,
            H3 = entity.H3,
            W1 = entity.W1,
            W2 = entity.W2,
            W3 = entity.W3,
            Types = entity.Types,
            Topology = entity.Topology,
            Glass = entity.Glass,

            RequirementType = entity.RequirementType,
            ItemName = entity.ItemName,
            Quantity = entity.Quantity,
            Details = entity.Details,
            Remarks = entity.Remarks
        };

        // Fallback-parse legacy specs if columns were empty
        if (looksLikeSpecs && string.IsNullOrWhiteSpace(dto.Location) && string.IsNullOrWhiteSpace(dto.H1))
        {
            ParseLegacyDescription(dto, rawDesc);
        }

        return dto;
    }

    private static void ParseLegacyDescription(AppRequirementResponseDto dto, string raw)
    {
        var parts = raw.Split('|', StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var kv = part.Split(':', 2, StringSplitOptions.TrimEntries);
            if (kv.Length < 2) continue;
            var key = kv[0].ToLowerInvariant();
            var val = kv[1];

            if (key == "location" && string.IsNullOrWhiteSpace(dto.Location)) dto.Location = val;
            else if (key == "types" && string.IsNullOrWhiteSpace(dto.Types)) dto.Types = val;
            else if (key == "glass" && string.IsNullOrWhiteSpace(dto.Glass)) dto.Glass = val;
            else if (key == "topology" && string.IsNullOrWhiteSpace(dto.Topology)) dto.Topology = val;
            else if (key == "height")
            {
                var hSubs = val.Split(',', StringSplitOptions.TrimEntries);
                foreach (var hs in hSubs)
                {
                    var hkv = hs.Split(':', 2, StringSplitOptions.TrimEntries);
                    if (hkv.Length < 2) continue;
                    if (hkv[0].Equals("H1", StringComparison.OrdinalIgnoreCase)) dto.H1 = hkv[1];
                    else if (hkv[0].Equals("H2", StringComparison.OrdinalIgnoreCase)) dto.H2 = hkv[1];
                    else if (hkv[0].Equals("H3", StringComparison.OrdinalIgnoreCase)) dto.H3 = hkv[1];
                }
            }
            else if (key == "width")
            {
                var wSubs = val.Split(',', StringSplitOptions.TrimEntries);
                foreach (var ws in wSubs)
                {
                    var wkv = ws.Split(':', 2, StringSplitOptions.TrimEntries);
                    if (wkv.Length < 2) continue;
                    if (wkv[0].Equals("W1", StringComparison.OrdinalIgnoreCase)) dto.W1 = wkv[1];
                    else if (wkv[0].Equals("W2", StringComparison.OrdinalIgnoreCase)) dto.W2 = wkv[1];
                    else if (wkv[0].Equals("W3", StringComparison.OrdinalIgnoreCase)) dto.W3 = wkv[1];
                }
            }
        }
    }
}
