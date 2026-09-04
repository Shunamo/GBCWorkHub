using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.DTO;

namespace GBCWorkHub.DAC
{
    /// <summary>XSUP.MSDWHTFS 폐기. 업무기록은 MSDWHTKD_WRK*.</summary>
    public class OracleTfsWorkLogRepository : ITfsWorkLogRepository
    {
        private const string Retired =
            "MSDWHTFS 폐기. 체크인 보관함에서 업무기록(MSDWHTKD_WRK)으로 저장하세요.";

        public bool IsConfigured
        {
            get { return false; }
        }

        public bool LastConnectionOk
        {
            get { return false; }
        }

        public string LastConnectionError
        {
            get { return Retired; }
        }

        public Task<bool> ExistsAsync(string collectionUrl, int changesetId)
        {
            return Task.FromResult(false);
        }

        public Task<bool> SaveAsync(TfsWorkLogRecord record)
        {
            WorkHubFileLogger.Warn("TFS_SAVE_SKIPPED", Retired);
            return Task.FromResult(false);
        }

        public Task<IList<bool>> SaveManyAsync(IList<TfsWorkLogRecord> records)
        {
            var results = new List<bool>();
            if (records == null)
                return Task.FromResult((IList<bool>)results);
            for (int i = 0; i < records.Count; i++)
                results.Add(false);
            return Task.FromResult((IList<bool>)results);
        }
    }
}
