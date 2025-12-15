namespace AeroGL
{
    public static class ProjectCodeUtil
    {
        // Ubah input ke string 3 digit. Jika bukan angka/empty → "" (anggap tidak valid).
        public static string Normalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "";
            input = input.Trim();

            int n;
            if (!int.TryParse(input, out n) || n < 0) return "";
            if (n > 999) n = n % 1000; // jaga-jaga, batasi 3 digit

            return n.ToString("D3");   // "D3" → selalu 3 digit (001, 023, 999)
        }

        // Bandingkan dua input dengan normalisasi dulu
        public static bool Matches(string a, string b)
        {
            return Normalize(a) == Normalize(b);
        }
    }
}
