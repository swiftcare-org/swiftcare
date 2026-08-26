using System.Text.Json.Serialization;

namespace PatientService.Models.Enums;

// Member names are valid C# identifiers; the wire format (and the database column, via
// HasConversion<string>() in PatientDbContext) must be the literal ABO/Rh notation
// ("A+", "AB-", ...), which JsonStringEnumMemberName supplies per member.
public enum BloodGroup
{
    [JsonStringEnumMemberName("A+")]
    APositive,

    [JsonStringEnumMemberName("A-")]
    ANegative,

    [JsonStringEnumMemberName("B+")]
    BPositive,

    [JsonStringEnumMemberName("B-")]
    BNegative,

    [JsonStringEnumMemberName("O+")]
    OPositive,

    [JsonStringEnumMemberName("O-")]
    ONegative,

    [JsonStringEnumMemberName("AB+")]
    ABPositive,

    [JsonStringEnumMemberName("AB-")]
    ABNegative
}
