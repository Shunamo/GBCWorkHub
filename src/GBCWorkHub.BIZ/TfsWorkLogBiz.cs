using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.DAC;
using GBCWorkHub.DTO;

namespace GBCWorkHub.BIZ
{
    public class TfsWorkLogBiz
    {
        private readonly ITfsWorkLogRepository _repository;

        public TfsWorkLogBiz()
            : this(new OracleTfsWorkLogRepository())
        {
        }

        public TfsWorkLogBiz(ITfsWorkLogRepository repository)
        {
            _repository = repository;
        }

        public bool IsConfigured
        {
            get { return _repository.IsConfigured; }
        }

        public string LastConnectionError
        {
            get { return _repository.LastConnectionError; }
        }

        public Task<bool> SaveAsync(TfsWorkLogRecord record)
        {
            return _repository.SaveAsync(record);
        }

        public Task<IList<bool>> SaveManyAsync(IList<TfsWorkLogRecord> records)
        {
            return _repository.SaveManyAsync(records);
        }
    }
}
