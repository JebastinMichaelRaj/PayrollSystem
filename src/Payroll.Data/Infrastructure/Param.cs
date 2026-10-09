using System.Data;
namespace Payroll.Data.Infrastructure;

/// <summary>Provider-neutral stored-procedure parameter. A fresh SqlParameter is created per execution (safe for retries); output values are copied back.</summary>
public sealed class Param
{
    public string Name { get; }
    public SqlDbType Type { get; }
    public object? Value { get; set; }
    public ParameterDirection Direction { get; init; } = ParameterDirection.Input;
    public string? TypeName { get; init; }
    public int Size { get; init; }
    public byte Scale { get; init; }
    private Param(string name, SqlDbType type, object? value) => (Name, Type, Value) = (name, type, value);

    public static Param In(string name, SqlDbType type, object? value, int size = 0) => new(name, type, value) { Size = size };
    public static Param Out(string name, SqlDbType type) => new(name, type, null) { Direction = ParameterDirection.Output };
    public static Param Tvp(string name, string typeName, DataTable table) => new(name, SqlDbType.Structured, table) { TypeName = typeName };
    public static Param Date(string name, DateOnly? v) => new(name, SqlDbType.Date, v is null ? null : v.Value.ToDateTime(TimeOnly.MinValue));
    public static Param Dec(string name, decimal? v, byte scale = 4) => new(name, SqlDbType.Decimal, v) { Scale = scale };
    public static Param Str(string name, string? v, int size) => In(name, SqlDbType.NVarChar, string.IsNullOrWhiteSpace(v) ? null : v.Trim(), size);

    /// <summary>Infers the SQL type from the CLR value (used by the generic report runner).</summary>
    public static Param Of(string name, object? v) => v switch
    {
        null => new(name, SqlDbType.NVarChar, null) { Size = 4000 },
        int i => new(name, SqlDbType.Int, i),
        long l => new(name, SqlDbType.BigInt, l),
        bool b => new(name, SqlDbType.Bit, b),
        decimal d => new(name, SqlDbType.Decimal, d) { Scale = 4 },
        DateOnly d => Date(name, d),
        DateTime dt => new(name, SqlDbType.DateTime2, dt),
        byte[] bytes => new(name, SqlDbType.VarBinary, bytes) { Size = 8 },
        string s => new(name, SqlDbType.NVarChar, s) { Size = 4000 },
        _ => throw new ArgumentException($"Unsupported parameter type {v.GetType().Name} for @{name}")
    };
}
