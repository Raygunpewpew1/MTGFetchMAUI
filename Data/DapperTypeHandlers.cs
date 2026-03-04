using Dapper;
using System.Data;
using System.Text.Json;

namespace AetherVault.Data;

public class JsonArrayTypeHandler : SqlMapper.TypeHandler<string[]>
{
    public override string[] Parse(object value)
    {
        if (value is null || value is DBNull)
            return [];

        var strValue = value.ToString();
        if (string.IsNullOrWhiteSpace(strValue) || strValue == "[]")
            return [];

        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(strValue);
            return parsed ?? [];
        }
        catch
        {
            // If it's not JSON, try to fall back or return an empty array
            return [];
        }
    }

    public override void SetValue(IDbDataParameter parameter, string[]? value)
    {
        parameter.Value = value == null || value.Length == 0
            ? "[]"
            : JsonSerializer.Serialize(value);
    }
}
