using System.Security.Cryptography;
using DotKnow.Infrastructure;

namespace DotKnow.App.Windows;

internal sealed class DpapiProtector : IDataProtector
{
    private static readonly byte[] Entropy = "TypeLingo/user-data/v1"u8.ToArray();
    public byte[] Protect(byte[] value) => ProtectedData.Protect(value, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] value) => ProtectedData.Unprotect(value, Entropy, DataProtectionScope.CurrentUser);
}
