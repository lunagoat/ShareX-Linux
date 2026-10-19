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

using Newtonsoft.Json.Serialization;
using System.Reflection;

namespace ShareX.HelpersLib
{
    public class DPAPIEncryptedStringValueProvider : IValueProvider
    {
        private const string encryptedTag = "$ShareXEncrypted$";
        private const string legacyTag = "$DPAPIEncrypted$";

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, System.Collections.Generic.Dictionary<string, string>> Unavailable = new();
        private PropertyInfo targetProperty;

        public DPAPIEncryptedStringValueProvider(PropertyInfo targetProperty)
        {
            this.targetProperty = targetProperty;
        }

        public object GetValue(object target)
        {
            string value = (string)targetProperty.GetValue(target);

            var unavailable = Unavailable.GetOrCreateValue(target);
            if (string.IsNullOrEmpty(value) && unavailable.TryGetValue(targetProperty.Name, out string retained)) return retained;
            if (!string.IsNullOrEmpty(value))
            {
                unavailable.Remove(targetProperty.Name);
                try
                {
                    value = encryptedTag + DPAPI.Encrypt(value);
                }
                catch (System.Exception ex)
                {
                    throw new Newtonsoft.Json.JsonSerializationException("Unable to encrypt ShareX credentials.", ex);
                }
            }

            return value;
        }

        public void SetValue(object target, object value)
        {
            string text = (string)value;

            if (!string.IsNullOrEmpty(text) && (text.StartsWith(encryptedTag) || text.StartsWith(legacyTag)))
            {
                try
                {
                    string encryptedString = text.Substring(text.StartsWith(encryptedTag) ? encryptedTag.Length : legacyTag.Length);
                    text = DPAPI.Decrypt(encryptedString);
                }
                catch (System.Exception ex)
                {
                    DebugHelper.WriteException(ex, "ShareX credentials unavailable; preserving encrypted value. Restore settings.key or unlock the keyring. Windows DPAPI imports must first be decrypted on Windows.");
                    Unavailable.GetOrCreateValue(target)[targetProperty.Name] = text;
                    text = null;
                }
            }

            targetProperty.SetValue(target, text);
        }
    }
}