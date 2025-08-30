using System.Configuration;
using System.Data.SQLite;

namespace AeroGL.Data
{
    public static class Db
    {
        public static SQLiteConnection Open()
        {
            var cs = ConfigurationManager.ConnectionStrings["AeroGL"].ConnectionString;
            var cn = new SQLiteConnection(cs);
            cn.Open();
            return cn;
        }
    }
}
