using System;
using System.Linq;
using System.Threading.Tasks;
using GBCWorkHub.DAC;
using GBCWorkHub.DTO;
using Newtonsoft.Json;

namespace GBCWorkHub.BIZ.DevSession
{
    /// <summary>
    /// Receives the remote SessionAgent's GBCWORKHUB_SESSION_RESULT:: payload (two-point
    /// snapshot diff + TFVC classification for one dev session) and persists it to
    /// XSUP.MSDWHTKH_FILE, keyed by SESSION_TOKEN. No watcher, no live tracking here — this
    /// side is purely the receive+persist half; all classification already happened on the
    /// remote PC before this payload was sent.
    /// </summary>
    public class DevSessionFileBiz
    {
        public const string ResultPrefix = "GBCWORKHUB_SESSION_RESULT::";

        private readonly OracleDevSessionFileRepository _repository;

        public DevSessionFileBiz()
            : this(new OracleDevSessionFileRepository())
        {
        }

        public DevSessionFileBiz(OracleDevSessionFileRepository repository)
        {
            _repository = repository;
        }

        /// <summary>
        /// Parses one GBCWORKHUB_SESSION_RESULT:: clipboard payload and persists its files.
        /// Best-effort: a parse or DB failure is logged by the repository layer, not thrown,
        /// since this runs off a clipboard event with no user waiting on it synchronously.
        /// </summary>
        public async Task<SessionChangeReportDto> HandleReceivedResultAsync(string clipboardText)
        {
            SessionChangeReportDto report = TryParse(clipboardText);
            if (report == null || string.IsNullOrWhiteSpace(report.SessionToken))
                return report;

            var dtos = report.Files.Select(f => new DevSessionFileDto
            {
                SessionToken = report.SessionToken,
                FilePath = f.FilePath,
                ChangeKind = f.Kind,
                TfvcStatus = f.TfvcStatus,
                ChangesetId = f.ChangesetId,
                Confidence = f.Confidence,
                Reason = f.Reason,
                DiffAvailable = f.DiffAvailable,
                DiffSummary = f.DiffSummary
            }).ToList();

            if (dtos.Count > 0)
                await Task.Run(() => _repository.InsertSessionFiles(report.SessionToken, dtos)).ConfigureAwait(false);

            return report;
        }

        public static SessionChangeReportDto TryParse(string clipboardText)
        {
            if (string.IsNullOrWhiteSpace(clipboardText))
                return null;

            string trimmed = clipboardText.TrimStart();
            if (!trimmed.StartsWith(ResultPrefix, StringComparison.Ordinal))
                return null;

            try
            {
                string json = trimmed.Substring(ResultPrefix.Length);
                return JsonConvert.DeserializeObject<SessionChangeReportDto>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
