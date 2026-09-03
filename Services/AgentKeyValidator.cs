using System.Security.Cryptography;
using System.Text;

namespace Monitoring.Blazor.Services;

public static class AgentKeyValidator
{
    public static bool IsValid(string? configuredKey, string? suppliedKey)
    {
        if (string.IsNullOrWhiteSpace(configuredKey) || string.IsNullOrWhiteSpace(suppliedKey))
        {
            return false;
        }

        var expected = Encoding.UTF8.GetBytes(configuredKey.Trim());
        var actual = Encoding.UTF8.GetBytes(suppliedKey.Trim());
        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }
}
