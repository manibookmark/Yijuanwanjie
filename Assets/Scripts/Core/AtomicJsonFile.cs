using System;
using System.IO;
using System.Text;

namespace WanjieScroll.Core
{
    /// <summary>
    /// Publishes a complete UTF-8 document by replacing the file, so readers of the
    /// previous file may keep their handles or memory mappings without truncation.
    /// Ordinary write or sharing failures preserve the target and clean the temporary
    /// file. If the operating system removes the target during a failed replacement,
    /// the complete staged document is retained for recovery instead of being deleted.
    /// </summary>
    public static class AtomicJsonFile
    {
        public static void Write(string path, string contents)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A destination path is required.", nameof(path));
            if (contents == null)
                throw new ArgumentNullException(nameof(contents));

            string destination = Path.GetFullPath(path);
            string directory = Path.GetDirectoryName(destination);
            string temporary = Path.Combine(directory,
                "." + Path.GetFileName(destination) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            bool replacingExisting = false;

            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew,
                           FileAccess.Write, FileShare.None))
                {
                    using (var writer = new StreamWriter(stream,
                               new UTF8Encoding(false), 4096, true))
                    {
                        writer.Write(contents);
                        writer.Flush();
                    }
                    stream.Flush(true);
                }

                if (File.Exists(destination))
                {
                    replacingExisting = true;
                    File.Replace(temporary, destination, null);
                }
                else
                    File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary) && (!replacingExisting || File.Exists(destination)))
                    File.Delete(temporary);
            }
        }
    }
}
