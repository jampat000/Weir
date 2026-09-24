using System.Globalization;
using Weir.Core.Json;
using Weir.Core.Time;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Settings;

/// <summary>
/// The generic column and row IO both the export and the restore use: reading a table's schema, converting a bundle
/// row into typed values for it, and reading a row back for comparison. Nothing here knows about a specific table.
/// </summary>
public static partial class ConfigurationBundleStore
{
    private static IEnumerable<PyJson> Iterate(PyJson value) => value switch
    {
        PyList list => list.Items,
        PyDict dict => dict.Keys.Select(key => (PyJson)new PyStr(key)),
        PyStr text => text.Value.Select(c => (PyJson)new PyStr(c.ToString())),
        _ => throw new PyTypeErrorException("A section of the backup that holds rows must be a list."),
    };

    private static PyJson Required(PyDict dict, string key) =>
        dict.Get(key) ?? throw new PyTypeErrorException($"The backup is missing {key}.");

    private static long Saturate(System.Numerics.BigInteger value) =>
        value > long.MaxValue ? long.MaxValue : value < long.MinValue ? long.MinValue : (long)value;

    /// <summary>The section's values for the table's own columns (other keys are ignored), with timestamps parsed as ISO 8601.</summary>
    private static List<KeyValuePair<string, PyJson>> ToKwargsList(Dictionary<string, Column> columns, PyDict data)
    {
        var output = new List<KeyValuePair<string, PyJson>>();
        foreach (var (key, raw) in data.Items)
        {
            if (!columns.TryGetValue(key, out var column))
            {
                continue;
            }

            output.Add(new(key, column.Kind == ColumnKind.DateTime ? ParsePyDateTimeValue(raw) : raw));
        }

        return output;
    }

    private static Dictionary<string, PyJson> ToKwargs(Dictionary<string, Column> columns, PyDict data)
    {
        var dict = new Dictionary<string, PyJson>(StringComparer.Ordinal);
        foreach (var (key, value) in ToKwargsList(columns, data))
        {
            dict[key] = value;
        }

        return dict;
    }

    /// <summary>A parsed timestamp is carried as a <see cref="PyDateTimeValue"/>.</summary>
    private static PyJson ParsePyDateTimeValue(PyJson raw)
    {
        if (raw is not PyStr text)
        {
            return PyNull.Instance;
        }

        var replaced = text.Value.Replace("Z", "+00:00", StringComparison.Ordinal);
        return PyDateTime.TryFromIsoFormat(replaced, out var parsed)
            ? new PyDateTimeValue(parsed)
            : throw new PyValueErrorException($"Invalid isoformat string: {PyStrings.Repr(replaced)}");
    }

    private static async Task<long> InsertAsync(UnitOfWork uow, string table, Dictionary<string, Column> columns, Dictionary<string, PyJson> kwargs)
    {
        if (kwargs.Count == 0)
        {
            var emptyId = await uow.ExecuteScalarWriteAsync($"INSERT INTO {table} DEFAULT VALUES RETURNING id").ConfigureAwait(false);
            return Convert.ToInt64(emptyId, CultureInfo.InvariantCulture);
        }

        var names = kwargs.Keys.ToList();
        var parameters = names.Select(name => ($"${name}", Bind(columns[name], kwargs[name]))).ToArray();
        var id = await uow.ExecuteScalarWriteAsync(
            $"INSERT INTO {table} ({string.Join(", ", names.Select(n => $"\"{n}\""))}) VALUES ({string.Join(", ", names.Select(n => "$" + n))}) RETURNING id",
            parameters).ConfigureAwait(false);
        return Convert.ToInt64(id, CultureInfo.InvariantCulture);
    }

    private static object? Bind(Column column, PyJson value)
    {
        switch (column.Kind)
        {
            case ColumnKind.DateTime:
                return value is PyDateTimeValue dt ? dt.Value.ToSqlite() : DBNull.Value;
            case ColumnKind.Boolean:
                return value switch
                {
                    PyNull => DBNull.Value,
                    PyBool b => b.Value ? 1L : 0L,
                    PyInt i when i.Value.IsZero || i.Value.IsOne => (long)i.Value,
                    PyFloat f when f.Value is 0.0 or 1.0 => (long)f.Value,
                    _ => throw new PyTypeErrorException($"Not a boolean value: {PyConvert.Repr(value)}"),
                };
            default:
                return PyConvert.ToDatabase(value);
        }
    }

    private static bool PythonEquals(ColumnKind kind, PyJson? loaded, PyJson assigned)
    {
        loaded ??= PyNull.Instance;
        if (kind == ColumnKind.DateTime || loaded is PyDateTimeValue || assigned is PyDateTimeValue)
        {
            return (loaded, assigned) switch
            {
                (PyNull, PyNull) => true,
                (PyDateTimeValue a, PyDateTimeValue b) => a.Value.IsAware == b.Value.IsAware &&
                    (a.Value.IsAware ? a.Value.AsUtc == b.Value.AsUtc : a.Value.Clock == b.Value.Clock),
                _ => false,
            };
        }

        return (loaded, assigned) switch
        {
            (PyNull, PyNull) => true,
            (PyStr a, PyStr b) => a.Value == b.Value,
            _ when Numeric(loaded) is { } x && Numeric(assigned) is { } y => x == y,
            _ => false,
        };
    }

    private static double? Numeric(PyJson value) => value switch
    {
        PyBool b => b.Value ? 1 : 0,
        PyInt i => (double)i.Value,
        PyFloat f => f.Value,
        _ => null,
    };

    private static async Task<Dictionary<string, Column>> ColumnsAsync(UnitOfWork uow, string table)
    {
        var columns = await uow.QueryAsync(
            $"PRAGMA table_info({table})",
            reader => new Column(
                SqliteValues.GetString(reader, 1),
                SqliteValues.GetString(reader, 2).ToUpperInvariant() switch
                {
                    "BOOLEAN" => ColumnKind.Boolean,
                    "DATETIME" => ColumnKind.DateTime,
                    _ => ColumnKind.Raw,
                })).ConfigureAwait(false);
        var ordered = new Dictionary<string, Column>(StringComparer.Ordinal);
        foreach (var column in columns)
        {
            ordered[column.Name] = column;
        }

        return ordered;
    }

    /// <summary>Every matching row as a dictionary, columns in table order.</summary>
    private static async Task<List<PyJson>> ReadRowsAsync(UnitOfWork uow, string table, string clause)
    {
        var columns = await ColumnsAsync(uow, table).ConfigureAwait(false);
        var names = columns.Keys.ToList();
        return await uow.QueryAsync<PyJson>(
            $"SELECT {string.Join(", ", names.Select(n => $"\"{n}\""))} FROM {table} {clause}",
            reader =>
            {
                var row = new PyDict();
                for (var i = 0; i < names.Count; i++)
                {
                    row.Set(names[i], SerializeStored(columns[names[i]], reader.GetValue(i)));
                }

                return row;
            }).ConfigureAwait(false);
    }

    private static async Task<Dictionary<string, PyJson>?> ReadTypedRowAsync(UnitOfWork uow, string table, Dictionary<string, Column> columns, PyJson pk)
    {
        var names = columns.Keys.ToList();
        var rows = await uow.QueryAsync(
            $"SELECT {string.Join(", ", names.Select(n => $"\"{n}\""))} FROM {table} WHERE id = $pk",
            reader =>
            {
                var row = new Dictionary<string, PyJson>(StringComparer.Ordinal);
                for (var i = 0; i < names.Count; i++)
                {
                    row[names[i]] = LoadStored(columns[names[i]], reader.GetValue(i));
                }

                return row;
            },
            ("$pk", PyConvert.ToDatabase(pk))).ConfigureAwait(false);
        return rows.Count > 0 ? rows[0] : null;
    }

    private static PyJson LoadStored(Column column, object value)
    {
        if (value is DBNull)
        {
            return PyNull.Instance;
        }

        return column.Kind switch
        {
            ColumnKind.Boolean => PyJson.Of(PyConvert.FromDatabase(value).IsTruthy),
            ColumnKind.DateTime => PyDateTime.TryFromIsoFormat(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, out var parsed)
                ? new PyDateTimeValue(parsed)
                : throw new FormatException("Invalid isoformat string in the database."),
            _ => PyConvert.FromDatabase(value),
        };
    }

    private static PyJson SerializeStored(Column column, object value)
    {
        var loaded = LoadStored(column, value);
        return loaded is PyDateTimeValue dt ? PyJson.Of(dt.Value.AstimezoneUtc().IsoFormat()) : loaded;
    }
}
