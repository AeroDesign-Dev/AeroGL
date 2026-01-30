using System;
using System.IO;

namespace AeroGL.Data
{
    public class BackupService
    {
        public void CopyDatabase(string sourcePath, string destPath)
        {
            if (string.IsNullOrEmpty(sourcePath)) throw new ArgumentNullException(nameof(sourcePath));
            if (string.IsNullOrEmpty(destPath)) throw new ArgumentNullException(nameof(destPath));

            // Copy file database aktif ke tujuan. 'true' untuk overwrite.
            File.Copy(sourcePath, destPath, true);
        }
    }
}