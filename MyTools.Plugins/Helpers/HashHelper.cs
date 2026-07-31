using System.Security.Cryptography;
using System.Text;

namespace MyTools.Plugins;

public class HashHelper
{
    public static string ComputeSha256Hash(string rawData)
    {
        if (string.IsNullOrEmpty(rawData))
        {
            return string.Empty;
        }

        var data = Encoding.UTF8.GetBytes(rawData);
        return ComputeSha256Hash(data);
    }

    public static string ComputeSha256Hash(byte[] rawData)
    {
        using (var sha256Hash = SHA256.Create())
        {
            byte[] bytes = sha256Hash.ComputeHash(rawData);
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < bytes.Length; i++)
            {
                builder.Append(bytes[i].ToString("x2"));
            }
            return builder.ToString();
        }
    }
}