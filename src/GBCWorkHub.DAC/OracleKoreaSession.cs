using Oracle.ManagedDataAccess.Client;

namespace GBCWorkHub.DAC
{
    /// <summary>Oracle 세션 시계를 한국 표준시로 고정한다. SYSTIMESTAMP 점유 시각이 해외 TZ로 저장되지 않게.</summary>
    internal static class OracleKoreaSession
    {
        public static void Apply(OracleConnection conn)
        {
            if (conn == null)
                return;

            try
            {
                var info = conn.GetSessionInfo();
                info.TimeZone = "Asia/Seoul";
                conn.SetSessionInfo(info);
            }
            catch
            {
            }

            try
            {
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "ALTER SESSION SET TIME_ZONE = 'Asia/Seoul'";
                    cmd.ExecuteNonQuery();
                }
            }
            catch
            {
            }
        }
    }
}
