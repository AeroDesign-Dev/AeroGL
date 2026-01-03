using System;
using System.Collections.Generic;
using System.Text;

namespace AeroGL.Core
{
    public static class CurrentCompany
    {
        public static Company Data { get; set; }

        public static bool IsLoaded => Data != null;
    }
}
