using System.Text;

namespace Rinha.FraudDetection.Infrastructure.Index;

public static class IndexFileFormat
{
    public const string Magic = "RINHIDX1";
    public const int Scale = 10000;
    public const int Dims = 14;
    public const int PartitionCount = 256;
    public const int HeaderSize = 64;
    public const int PartitionEntrySize = 68;

    public static byte[] MagicBytes => Encoding.ASCII.GetBytes(Magic);
}
