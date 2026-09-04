using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using GBCWorkHub.DTO;
using Newtonsoft.Json;

namespace GBCWorkHub.BIZ
{
    /// <summary>
    /// 클립보드 GBCWORKHUB RDP 상태 파싱 / 상태 판정
    /// (+ RC: "yyyy-MM-dd HH:mm:ss | DOMAIN\user | HO-BCARE-07 | connect")
    /// </summary>
    public class RdpStatusBiz
    {
        public const string ClipboardPrefix = "GBCWORKHUB::";
        public const string ExpectedType = "GBC_RDP_STATUS";

        /// <summary>
        /// RC 원격 PC 클립보드 한 줄.
        /// 예: 2026-07-31 07:38:34 | RCHSP\sukhoonyoon | HO-BCARE-07 | connect
        /// </summary>
        private static readonly Regex RcStatusLineRegex = new Regex(
            @"^\s*(?<ts>\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2})\s*\|\s*(?<user>[^|\r\n]+?)\s*\|\s*(?<pc>[^|\r\n]+?)\s*\|\s*(?<action>connect|disconnect|logoff|reconnect)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            // 알 수 없는 필드는 무시 (기본값이지만 명시)
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Include
        };

        public enum ClipboardHandleResult
        {
            Ignored,
            UnsupportedQuiet,
            ParseFailed,
            Success
        }

        public class ParseResult
        {
            public ClipboardHandleResult Result { get; set; }
            public string Message { get; set; }
            public RdpStatusPayload Payload { get; set; }
            public string DeterminedStatus { get; set; }
            public string StatusReason { get; set; }
        }

        public class ParsedEventItem
        {
            public long RecordId { get; set; }
            public int EventId { get; set; }
            public string EventTime { get; set; }
            public string User { get; set; }
            public string SessionId { get; set; }
            public string SourceIp { get; set; }
            public string EventDescription { get; set; }
        }

        /// <summary>RC 한 줄 상태 클립보드인지 (접두사 없음).</summary>
        public static bool LooksLikeRcStatusLine(string clipboardText)
        {
            if (string.IsNullOrWhiteSpace(clipboardText))
                return false;

            string line = FirstNonEmptyLine(clipboardText);
            return RcStatusLineRegex.IsMatch(line);
        }

        /// <summary>PC명 동일 여부 (대소문자만 무시, 하이픈 정규화 없음).</summary>
        public static bool ComputerNamesMatch(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 갤러리명 vs 원격 COMPUTERNAME: 도메인/FQDN 제거 후 비교.
        /// 하이픈·언더스코어만 다른 경우(KEB-3VNZVP2 vs KEB3VNZVP2)도 동일로 본다.
        /// </summary>
        public static bool ComputerNamesLooselyMatch(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
                return false;
            string left = NormalizeComputerName(a);
            string right = NormalizeComputerName(b);
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                return true;
            string compactLeft = CompactComputerName(left);
            string compactRight = CompactComputerName(right);
            return !string.IsNullOrEmpty(compactLeft)
                && string.Equals(compactLeft, compactRight, StringComparison.OrdinalIgnoreCase);
        }

        public static string NormalizeComputerName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            string s = name.Trim();
            int slash = s.LastIndexOf('\\');
            if (slash >= 0 && slash < s.Length - 1)
                s = s.Substring(slash + 1);
            int dot = s.IndexOf('.');
            if (dot > 0)
                s = s.Substring(0, dot);
            return s;
        }

        private static string CompactComputerName(string normalized)
        {
            if (string.IsNullOrEmpty(normalized))
                return string.Empty;
            return normalized.Replace("-", string.Empty).Replace("_", string.Empty);
        }

        /// <summary>
        /// 클립보드 텍스트 처리. 일반 텍스트는 조용히 무시.
        /// </summary>
        public ParseResult TryHandleClipboardText(string clipboardText)
        {
            var result = new ParseResult
            {
                Result = ClipboardHandleResult.Ignored,
                Message = null
            };

            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                result.Result = ClipboardHandleResult.Ignored;
                return result;
            }

            string text = clipboardText.TrimStart();

            // RC 한 줄 형식 (GBCWORKHUB:: 접두사 없음)
            RdpStatusPayload rcPayload;
            if (TryParseRcStatusLine(text, out rcPayload))
            {
                string rcReason;
                string rcStatus = DetermineStatus(rcPayload, out rcReason);
                result.Result = ClipboardHandleResult.Success;
                result.Message = "RC 원격 PC 상태 수신 완료";
                result.Payload = rcPayload;
                result.DeterminedStatus = rcStatus;
                result.StatusReason = rcReason;
                return result;
            }

            if (!text.StartsWith(ClipboardPrefix, StringComparison.Ordinal))
            {
                result.Result = ClipboardHandleResult.Ignored;
                return result;
            }

            string json = text.Substring(ClipboardPrefix.Length).Trim();
            if (string.IsNullOrWhiteSpace(json))
            {
                result.Result = ClipboardHandleResult.ParseFailed;
                result.Message = "JSON 파싱 실패";
                return result;
            }

            RdpStatusPayload payload;
            try
            {
                payload = JsonConvert.DeserializeObject<RdpStatusPayload>(json, JsonSettings);
            }
            catch (Exception)
            {
                result.Result = ClipboardHandleResult.ParseFailed;
                result.Message = "JSON 파싱 실패";
                return result;
            }

            if (payload == null)
            {
                result.Result = ClipboardHandleResult.ParseFailed;
                result.Message = "JSON 파싱 실패";
                return result;
            }

            if (!string.Equals(payload.Type, ExpectedType, StringComparison.OrdinalIgnoreCase))
            {
                result.Result = ClipboardHandleResult.UnsupportedQuiet;
                result.Message = "지원하지 않는 클립보드 형식";
                return result;
            }

            string reason;
            string status = DetermineStatus(payload, out reason);

            result.Result = ClipboardHandleResult.Success;
            result.Message = "원격 PC 상태 수신 완료";
            result.Payload = payload;
            result.DeterminedStatus = status;
            result.StatusReason = reason;
            return result;
        }

        /// <summary>
        /// RC: "ts | user | pc | connect|disconnect|..." → GBC_RDP_STATUS 호환 payload
        /// </summary>
        public static bool TryParseRcStatusLine(string clipboardText, out RdpStatusPayload payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(clipboardText))
                return false;

            string line = FirstNonEmptyLine(clipboardText);
            var m = RcStatusLineRegex.Match(line);
            if (!m.Success)
                return false;

            string ts = m.Groups["ts"].Value.Trim();
            string user = m.Groups["user"].Value.Trim();
            string pc = m.Groups["pc"].Value.Trim();
            string action = m.Groups["action"].Value.Trim().ToLowerInvariant();

            DateTime collected;
            if (!DateTime.TryParseExact(
                    ts,
                    "yyyy-MM-dd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out collected))
            {
                if (!DateTime.TryParse(ts, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out collected))
                    collected = DateTime.Now;
            }

            bool isConnect = action == "connect" || action == "reconnect";
            bool isDisconnect = action == "disconnect" || action == "logoff";

            payload = new RdpStatusPayload
            {
                Type = ExpectedType,
                SchemaVersion = 1,
                ComputerName = pc,
                WindowsUser = user,
                ClientName = null,
                CollectedAt = collected.ToString("yyyy-MM-dd HH:mm:ss"),
                LatestRecordId = collected.Ticks,
                LatestEventId = isConnect ? 21 : (isDisconnect ? (action == "logoff" ? 23 : 24) : (int?)null),
                TriggerType = isDisconnect ? "RDP_DISCONNECT" : (isConnect ? "RC_CONNECT" : null),
                IsVerifiedDisconnect = isDisconnect ? true : (bool?)null,
                SessionRaw = isConnect ? "rdp-tcp#0 Active" : null,
                Events = null
            };
            return true;
        }

        private static string FirstNonEmptyLine(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            using (var reader = new System.IO.StringReader(text.TrimStart()))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                        return line.Trim();
                }
            }
            return text.Trim();
        }

        public enum FreshnessDecision
        {
            Accept,
            Duplicate,
            Older
        }

        /// <summary>
        /// 동일 computerName의 기존 승인 상태와 비교해 최신 여부 판정.
        /// existing이 null이면 첫 payload로 Accept.
        /// </summary>
        public FreshnessDecision EvaluateFreshness(
            string newPayloadHash,
            RdpStatusPayload newPayload,
            long? existingLatestRecordId,
            DateTime? existingCollectedAt,
            string existingPayloadHash,
            out string reason)
        {
            reason = null;
            if (newPayload == null)
            {
                reason = "newPayload null";
                return FreshnessDecision.Older;
            }

            // A. hash 동일 → 중복
            if (!string.IsNullOrEmpty(existingPayloadHash)
                && string.Equals(newPayloadHash, existingPayloadHash, StringComparison.OrdinalIgnoreCase))
            {
                reason = "IGNORE_DUPLICATE_PAYLOAD: payload hash identical";
                return FreshnessDecision.Duplicate;
            }

            // 기존 상태 없음 → 첫 수신
            if (existingLatestRecordId == null && existingCollectedAt == null && string.IsNullOrEmpty(existingPayloadHash))
            {
                reason = "first payload for computerName";
                return FreshnessDecision.Accept;
            }

            long? newRecordId = newPayload.LatestRecordId;
            DateTime? newCollectedAt = TryParseCollectedAt(newPayload.CollectedAt);

            // B. 양쪽 LatestRecordId 존재 → 새 값이 더 클 때만
            if (newRecordId.HasValue && existingLatestRecordId.HasValue)
            {
                if (newRecordId.Value > existingLatestRecordId.Value)
                {
                    reason = "LatestRecordId newer: " + newRecordId.Value + " > " + existingLatestRecordId.Value;
                    return FreshnessDecision.Accept;
                }

                reason = "IGNORE_OLDER_PAYLOAD: LatestRecordId " + newRecordId.Value + " <= existing " + existingLatestRecordId.Value;
                return FreshnessDecision.Older;
            }

            // C. RecordId 중 하나라도 null → CollectedAt 비교
            if (newCollectedAt.HasValue && existingCollectedAt.HasValue)
            {
                if (newCollectedAt.Value > existingCollectedAt.Value)
                {
                    reason = "CollectedAt newer: " + newCollectedAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                        + " > " + existingCollectedAt.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    return FreshnessDecision.Accept;
                }

                // D. CollectedAt 같거나 이전이면 무시
                reason = "IGNORE_OLDER_PAYLOAD: CollectedAt " + newCollectedAt.Value.ToString("yyyy-MM-dd HH:mm:ss")
                    + " <= existing " + existingCollectedAt.Value.ToString("yyyy-MM-dd HH:mm:ss");
                return FreshnessDecision.Older;
            }

            if (newCollectedAt.HasValue && !existingCollectedAt.HasValue)
            {
                reason = "CollectedAt present on new payload only";
                return FreshnessDecision.Accept;
            }

            reason = "IGNORE_OLDER_PAYLOAD: cannot prove newer (RecordId/CollectedAt insufficient)";
            return FreshnessDecision.Older;
        }

        public DateTime? TryParseCollectedAt(string collectedAt)
        {
            if (string.IsNullOrWhiteSpace(collectedAt))
                return null;

            DateTime dt;
            string[] formats =
            {
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss.fff",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss.fff",
                "yyyy/MM/dd HH:mm:ss"
            };

            if (DateTime.TryParseExact(
                collectedAt.Trim(),
                formats,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out dt))
            {
                return dt;
            }

            if (DateTime.TryParse(collectedAt.Trim(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out dt))
            {
                return dt;
            }

            return null;
        }

        public string ResolveClientComputerName(string clientName)
        {
            if (string.IsNullOrWhiteSpace(clientName))
                return Environment.MachineName;
            return clientName.Trim();
        }

        /// <summary>
        /// 방금 수신한 payload만으로 상태를 순수 판정한다.
        /// 이전 ViewModel CurrentStatus / SessionRaw를 참조하지 않는다.
        /// </summary>
        public string DetermineStatus(RdpStatusPayload payload)
        {
            string reason;
            return DetermineStatus(payload, out reason);
        }

        public string DetermineStatus(RdpStatusPayload payload, out string reason)
        {
            reason = "확인 필요(기본)";
            if (payload == null)
            {
                reason = "payload null";
                return "확인 필요";
            }

            // 1) 검증된 연결 해제
            if (string.Equals(payload.TriggerType, "RDP_DISCONNECT", StringComparison.OrdinalIgnoreCase)
                && payload.IsVerifiedDisconnect == true)
            {
                reason = "TriggerType=RDP_DISCONNECT && IsVerifiedDisconnect=true";
                return "사용 가능";
            }

            // 2) 이벤트 23/24
            if (payload.LatestEventId == 23 || payload.LatestEventId == 24)
            {
                reason = "LatestEventId=" + payload.LatestEventId;
                return "사용 가능";
            }

            // 3) 현재 payload SessionRaw에 rdp-tcp + Active
            //    null SessionRaw는 이전 값을 쓰지 않음 — 오직 현재 payload만 검사
            if (HasActiveRdpSession(payload.SessionRaw))
            {
                reason = "SessionRaw contains rdp-tcp and Active";
                return "사용 중";
            }

            // 4) 이벤트 21/25
            if (payload.LatestEventId == 21 || payload.LatestEventId == 25)
            {
                reason = "LatestEventId=" + payload.LatestEventId;
                return "사용 중";
            }

            reason = "규칙 미매칭 (TriggerType="
                + (payload.TriggerType ?? "null")
                + ", IsVerifiedDisconnect="
                + (payload.IsVerifiedDisconnect.HasValue ? payload.IsVerifiedDisconnect.Value.ToString() : "null")
                + ", LatestEventId="
                + (payload.LatestEventId.HasValue ? payload.LatestEventId.Value.ToString() : "null")
                + ", SessionRawIsNull="
                + (payload.SessionRaw == null)
                + ")";
            return "확인 필요";
        }

        private static bool HasActiveRdpSession(string sessionRaw)
        {
            if (string.IsNullOrWhiteSpace(sessionRaw))
                return false;

            string raw = sessionRaw;
            bool hasRdpTcp = raw.IndexOf("rdp-tcp", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasActive = raw.IndexOf("Active", StringComparison.OrdinalIgnoreCase) >= 0;
            return hasRdpTcp && hasActive;
        }

        public string GetEventDescription(int eventId)
        {
            switch (eventId)
            {
                case 21: return "로그인";
                case 25: return "재접속";
                case 24: return "연결 해제";
                case 23: return "로그오프";
                default: return "기타";
            }
        }

        public List<ParsedEventItem> ParseEvents(RdpStatusPayload payload)
        {
            var list = new List<ParsedEventItem>();
            if (payload == null || payload.Events == null)
                return list;

            foreach (var ev in payload.Events)
            {
                if (ev == null)
                    continue;

                string user;
                string sessionId;
                string sourceIp;
                ExtractEventFields(ev.EventMessage, out user, out sessionId, out sourceIp);

                list.Add(new ParsedEventItem
                {
                    RecordId = ev.RecordId,
                    EventId = ev.EventId,
                    EventTime = ev.EventTime,
                    User = user,
                    SessionId = sessionId,
                    SourceIp = sourceIp,
                    EventDescription = GetEventDescription(ev.EventId)
                });
            }

            return list;
        }

        public void ExtractEventFields(string eventMessage, out string user, out string sessionId, out string sourceIp)
        {
            user = null;
            sessionId = null;
            sourceIp = null;

            if (string.IsNullOrWhiteSpace(eventMessage))
                return;

            user = MatchFirst(eventMessage,
                @"(?im)^\s*User\s*:\s*(.+)\s*$",
                @"(?im)^\s*사용자\s*:\s*(.+)\s*$");

            sessionId = MatchFirst(eventMessage,
                @"(?im)^\s*Session\s*ID\s*:\s*(.+)\s*$",
                @"(?im)^\s*세션\s*ID\s*:\s*(.+)\s*$");

            sourceIp = MatchFirst(eventMessage,
                @"(?im)^\s*Source\s*Network\s*Address\s*:\s*(.+)\s*$",
                @"(?im)^\s*원본\s*네트워크\s*주소\s*:\s*(.+)\s*$");
        }

        private static string MatchFirst(string text, params string[] patterns)
        {
            foreach (var pattern in patterns)
            {
                var match = Regex.Match(text, pattern);
                if (match.Success && match.Groups.Count > 1)
                {
                    string value = match.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(value))
                        return value;
                }
            }

            return null;
        }
    }
}
