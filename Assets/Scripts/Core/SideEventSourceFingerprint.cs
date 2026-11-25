using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace WanjieScroll.Core
{
    // Only the closed immutable source-value family may enter this canonical format.
    // External code cannot decorate arbitrary state or extend the internal-ctor unions.
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = true)]
    internal sealed class SideEventSourceCanonicalAttribute : Attribute { }

    /// <summary>
    /// Owns the complete source-specification byte contract, distinct from selection
    /// proof. Property names/types are canonical and sorted ordinally; list order,
    /// duplicates, null presence, unknown presence and every binary32 bit are retained.
    /// It retains no object/raw JSON cache and invokes no eligibility or native engine.
    /// </summary>
    internal static class SideEventSourceFingerprint
    {
        private static readonly Encoding Encoding = new UTF8Encoding(false, true);
        internal static string Compute(string contentId, IReadOnlyList<SideEventSourceRoot> roots,
            IReadOnlyList<SideEventSourceGroup> groups, IReadOnlyList<SideEventSourceLayer> layers)
        {
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, Encoding, leaveOpen: true))
            {
                WriteText(writer, "side-event-source-specification/v1");
                WriteValue(writer, contentId); WriteValue(writer, roots); WriteValue(writer, groups); WriteValue(writer, layers);
            }
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
        }

        internal static string ComputeValue(object value)
        {
            using var bytes = new MemoryStream();
            using (var writer = new BinaryWriter(bytes, Encoding, leaveOpen: true)) WriteValue(writer, value);
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes.ToArray())).Replace("-", "").ToLowerInvariant();
        }

        private static void WriteText(BinaryWriter writer, string text)
        {
            byte[] value = Encoding.GetBytes(text); writer.Write(value.Length); writer.Write(value);
        }
        private static void WriteValue(BinaryWriter writer, object value)
        {
            if (value == null) { writer.Write((byte)0); return; }
            if (value is string text) { writer.Write((byte)1); WriteText(writer, text); return; }
            if (value is bool flag) { writer.Write((byte)2); writer.Write(flag); return; }
            if (value is int integer) { writer.Write((byte)3); writer.Write(integer); return; }
            if (value is long large) { writer.Write((byte)4); writer.Write(large); return; }
            if (value is float number)
            {
                writer.Write((byte)5); writer.Write(BitConverter.ToInt32(BitConverter.GetBytes(number), 0)); return;
            }
            if (value is SideEventOptionalInt optionalInteger)
            {
                writer.Write((byte)6); writer.Write(optionalInteger.IsKnown);
                if (optionalInteger.IsKnown) writer.Write(optionalInteger.Value); return;
            }
            if (value is SideEventOptionalBool optionalFlag)
            {
                writer.Write((byte)7); writer.Write(optionalFlag.IsKnown);
                if (optionalFlag.IsKnown) writer.Write(optionalFlag.Value); return;
            }
            Type type = value.GetType();
            if (type.IsEnum)
            {
                if (!Enum.IsDefined(type, value)) throw new ArgumentException("Unknown source specification enum.");
                writer.Write((byte)8); WriteText(writer, type.FullName); writer.Write(Convert.ToInt32(value)); return;
            }
            if (value is IEnumerable sequence)
            {
                writer.Write((byte)9); var items = new List<object>(); foreach (object item in sequence) items.Add(item);
                writer.Write(items.Count); foreach (object item in items) WriteValue(writer, item); return;
            }
            if (type.Assembly != typeof(SideEventSourceDefinition).Assembly ||
                !type.IsDefined(typeof(SideEventSourceCanonicalAttribute), inherit: true))
                throw new ArgumentException("Only closed immutable side-event source values can enter the specification fingerprint.");
            writer.Write((byte)10); WriteText(writer, type.FullName);
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
            writer.Write(properties.Length);
            foreach (PropertyInfo property in properties)
            {
                if (property.GetIndexParameters().Length != 0 || property.GetMethod == null || property.SetMethod != null)
                    throw new ArgumentException("Source canonical values must expose readonly non-indexed properties.");
                WriteText(writer, property.Name); WriteValue(writer, property.GetValue(value));
            }
        }
    }
}
