using System.IO.Compression;
using System.Text.Json;

// Simple builder: reads resources/references.json.gz and writes references.bin
// Format: int32 count, int16 vectors[count * stride], byte labels[count]

const int Scale = 10000;

var resources = args.Length > 0 ? args[0] : "resources";
var input = Path.Combine(resources, "references.json.gz");
var output = Path.Combine(resources, "references.bin");
var stride = 16; // 14 dims + 2 pad

using var fs = File.OpenRead(input);
using var gz = new GZipStream(fs, CompressionMode.Decompress);
using var doc = JsonDocument.Parse(gz);
var arr = doc.RootElement.EnumerateArray();

var list = new List<short>();
var labels = new List<byte>();

foreach (var item in arr)
{
    var vecProp = item.GetProperty("vector");
    if (vecProp.GetArrayLength() != 14) continue;
    for (int i = 0; i < 14; i++) list.Add(QuantizeFloat((float)vecProp[i].GetDouble()));
    list.Add(0); list.Add(0); // pad
    var lab = item.GetProperty("label").GetString();
    labels.Add(string.Equals(lab, "fraud", StringComparison.OrdinalIgnoreCase) ? (byte)1 : (byte)0);
}

var count = labels.Count;
using var outfs = File.Create(output);
using var bw = new BinaryWriter(outfs);

bw.Write(count);
// quantize to short with scale 8192
for (int i = 0; i < count * stride; i++)
{
    bw.Write(list[i]);
}

bw.Write(labels.ToArray());

Console.WriteLine($"Wrote {count} refs to {output}");

static short QuantizeFloat(float value)
{
    if (value <= -1f)
    {
        return (short)-Scale;
    }

    if (value <= 0f)
    {
        return 0;
    }

    if (value >= 1f)
    {
        return Scale;
    }

    return (short)Math.Round(value * Scale);
}
