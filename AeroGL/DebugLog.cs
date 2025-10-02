using System;
using System.Diagnostics;

namespace AeroGL
{
    internal static class DebugLog
    {
        [Conditional("DEBUG")]
        public static void Info(string tag, string message)
        {
            Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{tag}] {message}");
        }

        [Conditional("DEBUG")]
        public static void Dump(string tag, object obj)
        {
            try
            {
                // Jika pakai .NET Framework lama tanpa System.Text.Json, ganti sesuai kebutuhan.
                var json = System.Text.Json.JsonSerializer.Serialize(obj);
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{tag}] {json}");
            }
            catch
            {
                Debug.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [{tag}] (dump failed) {obj}");
            }
        }
    }
}
