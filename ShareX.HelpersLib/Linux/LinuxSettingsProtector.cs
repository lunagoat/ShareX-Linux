#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ShareX.HelpersLib;

internal static class LinuxSettingsProtector
{
    private static readonly object Sync = new();
    private static string cachedProfile;
    private static byte[] cachedKey;
    private static byte cachedSource;
    private static readonly byte[] ephemeralKey = RandomNumberGenerator.GetBytes(32);
    private static string Profile => Path.GetFullPath(LinuxPaths.CredentialDirectory ?? LinuxPaths.ConfigDirectory);
    private static string KeyPath => Path.Combine(Profile, "settings.key");

    public static string Encrypt(byte[] data, byte[] entropy)
    {
        lock (Sync)
        {
            (byte[] key, byte source) = EncryptionKey();
            byte[] result = new byte[1 + 12 + 16 + data.Length];
            result[0] = source;
            RandomNumberGenerator.Fill(result.AsSpan(1, 12));
            using AesGcm aes = new(key, 16);
            aes.Encrypt(result.AsSpan(1, 12), data, result.AsSpan(29), result.AsSpan(13, 16), entropy);
            return "SX1:" + Convert.ToBase64String(result);
        }
    }

    public static string Decrypt(string encrypted, byte[] entropy)
    {
        if (!encrypted.StartsWith("SX1:", StringComparison.Ordinal))
            throw new CryptographicException("Windows DPAPI credentials cannot be decrypted on Linux. Export decrypted settings on Windows before importing them.");
        byte[] data = Convert.FromBase64String(encrypted[4..]);
        if (data.Length < 29) throw new CryptographicException("Invalid encrypted settings.");
        lock (Sync)
        {
            byte[] key = data[0] switch
            {
                (byte)'F' => ReadKeyFile(),
                (byte)'S' => SecretServiceKey(false) ?? throw new CryptographicException("Unlock your Secret Service keyring to read ShareX settings."),
                (byte)'E' when LinuxPaths.Ephemeral => ephemeralKey,
                _ => throw new CryptographicException("Unknown encrypted settings format.")
            };
            byte[] clear = new byte[data.Length - 29];
            using AesGcm aes = new(key, 16);
            aes.Decrypt(data.AsSpan(1, 12), data.AsSpan(29), data.AsSpan(13, 16), clear, entropy);
            return Encoding.UTF8.GetString(clear);
        }
    }

    private static (byte[], byte) EncryptionKey()
    {
        if (LinuxPaths.Ephemeral) return (ephemeralKey, (byte)'E');
        if (cachedProfile == Profile) return (cachedKey, cachedSource);
        byte[] key = File.Exists(KeyPath) ? ReadKeyFile() : SecretServiceKey(true);
        byte source = File.Exists(KeyPath) ? (byte)'F' : (byte)'S';
        if (key == null)
        {
            key = RandomNumberGenerator.GetBytes(32);
            Directory.CreateDirectory(Profile, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string temporary = KeyPath + "." + Guid.NewGuid().ToString("N");
            try
            {
                using (FileStream stream = new(temporary, new FileStreamOptions { Mode = FileMode.CreateNew,
                    Access = FileAccess.Write, UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite })) stream.Write(key);
                try { File.Move(temporary, KeyPath, overwrite: false); }
                catch (IOException) when (File.Exists(KeyPath)) { key = ReadKeyFile(); }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            source = (byte)'F';
        }
        cachedProfile = Profile;
        cachedKey = key;
        cachedSource = source;
        return (key, source);
    }

    private static byte[] ReadKeyFile()
    {
        if (new FileInfo(KeyPath).LinkTarget != null) throw new CryptographicException("The settings key must not be a symbolic link.");
        File.SetUnixFileMode(KeyPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        byte[] key = File.ReadAllBytes(KeyPath);
        if (key.Length != 32) throw new CryptographicException("The ShareX settings key must contain 32 bytes.");
        return key;
    }

    private static byte[] SecretServiceKey(bool create)
    {
        // Sandbox tests must never read or change the real desktop keyring.
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SX_SANDBOX")) || LinuxDesktop.FindCommand("secret-tool") == null) return null;
        string id = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Profile))).ToLowerInvariant();
        var lookup = LinuxDesktop.RunAsync("secret-tool", new[] { "lookup", "application", "ShareX", "profile", id }, 2000).GetAwaiter().GetResult();
        if (lookup.Success && !string.IsNullOrEmpty(lookup.Output)) return Convert.FromBase64String(lookup.Output);
        if (!create) return null;
        byte[] key = RandomNumberGenerator.GetBytes(32);
        var store = LinuxDesktop.RunAsync("secret-tool", new[] { "store", "--label=ShareX settings encryption key", "application", "ShareX", "profile", id },
            2000, standardInput: Convert.ToBase64String(key)).GetAwaiter().GetResult();
        return store.Success ? key : null;
    }
}
