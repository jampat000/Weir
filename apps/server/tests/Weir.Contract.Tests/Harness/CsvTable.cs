using System.Text;

namespace Weir.Contract.Tests.Harness;

/// <summary>Reads CSV text (RFC 4180 quoting) into rows keyed by the header line.</summary>
public static class CsvTable
{
    public static List<Dictionary<string, string>> Parse(string text)
    {
        var records = Records(text);
        if (records.Count == 0)
        {
            return [];
        }

        var header = records[0];
        return records.Skip(1)
            .Select(record => header.Zip(record).ToDictionary(pair => pair.First, pair => pair.Second))
            .ToList();
    }

    private static List<List<string>> Records(string text)
    {
        var records = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var position = 0; position < text.Length; position++)
        {
            var character = text[position];
            if (quoted)
            {
                if (character != '"')
                {
                    field.Append(character);
                }
                else if (position + 1 < text.Length && text[position + 1] == '"')
                {
                    field.Append('"');
                    position++;
                }
                else
                {
                    quoted = false;
                }

                continue;
            }

            switch (character)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    records.Add(record);
                    record = [];
                    break;
                default:
                    field.Append(character);
                    break;
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            records.Add(record);
        }

        return records;
    }
}
