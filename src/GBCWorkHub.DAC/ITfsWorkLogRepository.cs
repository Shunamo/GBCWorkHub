using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.DTO;

namespace GBCWorkHub.DAC
{
    public interface ITfsWorkLogRepository
    {
        bool IsConfigured { get; }
        bool LastConnectionOk { get; }
        string LastConnectionError { get; }

        Task<bool> ExistsAsync(string collectionUrl, int changesetId);
        Task<bool> SaveAsync(TfsWorkLogRecord record);
        Task<IList<bool>> SaveManyAsync(IList<TfsWorkLogRecord> records);
    }
}
