namespace AeroGL
{
    public static class SingleProject
    {
        public static string Code
        {
            get { return global::AeroGL.Properties.Settings.Default.SingleProjectCode ?? ""; }
            set { global::AeroGL.Properties.Settings.Default.SingleProjectCode = value ?? ""; }
        }

        public static string Name
        {
            get { return global::AeroGL.Properties.Settings.Default.SingleProjectName ?? ""; }
            set { global::AeroGL.Properties.Settings.Default.SingleProjectName = value ?? ""; }
        }

        public static string Pass
        {
            get { return global::AeroGL.Properties.Settings.Default.SingleProjectPass ?? ""; }
            set { global::AeroGL.Properties.Settings.Default.SingleProjectPass = value ?? ""; }
        }

        public static bool IsInitialized()
        {
            return !string.IsNullOrWhiteSpace(Code) && !string.IsNullOrWhiteSpace(Name);
        }

        public static void Save()
        {
            global::AeroGL.Properties.Settings.Default.Save();
        }
    }
}
