using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using GBCWorkHub.DAC;
using GBCWorkHub.DTO;

namespace GBCWorkHub.BIZ
{
    public enum LocalAuthMode
    {
        Login = 0,
        Register = 1
    }

    public sealed class AuthLoginResult
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public string UserName { get; set; }
        public string TeamName { get; set; }
        public bool PasswordPersistedToDb { get; set; }
        public bool IsAdmin { get; set; }
        /// <summary>MSDWHTKD_USR.USR_ID — immutable person identity. 세션 전체에서 이 값으로 사용자를 식별한다.</summary>
        public long? UserId { get; set; }
    }

    /// <summary>
    /// 로그인(기존 계정만) / 회원가입(신규) 분리.
    /// 비밀번호는 MSDWHTKD_USR.PASSWORD_HASH (sql/20 적용 시).
    /// 내장 ADMIN: LOGIN_ID/PASSWORD = ADMIN, 표시명 관리자.
    /// </summary>
    public sealed class AuthBiz
    {
        public const string AdminLoginId = "ADMIN";
        public const string AdminDisplayName = "관리자";
        public const string AdminTeamName = "관리";

        private readonly IDirectoryRepository _repository;

        public AuthBiz()
            : this(new OracleDirectoryRepository())
        {
        }

        public AuthBiz(IDirectoryRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException("repository");
        }

        public static bool IsAdminLoginId(string loginOrName)
        {
            return !string.IsNullOrWhiteSpace(loginOrName)
                && string.Equals(loginOrName.Trim(), AdminLoginId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 이 PC(IP/PC명)에 가입 이력이 있으면 Login, 없으면 Register.
        /// 비밀번호 없는 기존 행은 Register로 유도.
        /// </summary>
        public LocalAuthMode ResolveLocalAuthMode()
        {
            string pcNm = RemotePcShareBiz.LocalClientPc;
            string ip = RemotePcShareBiz.LocalAccessIp;
            DirectoryUserDto existing = _repository.FindUserByLocalEndpoint(pcNm, ip);
            if (existing == null || string.IsNullOrWhiteSpace(existing.UserName))
                return LocalAuthMode.Register;

            bool hasAuth = HasAuthSchema();
            if (hasAuth && string.IsNullOrWhiteSpace(existing.PasswordHash))
                return LocalAuthMode.Register;

            return LocalAuthMode.Login;
        }

        public DirectoryUserDto FindLocalPcAccount()
        {
            return _repository.FindUserByLocalEndpoint(
                RemotePcShareBiz.LocalClientPc,
                RemotePcShareBiz.LocalAccessIp);
        }

        /// <summary>기존 계정만. 없으면 회원가입 유도. ADMIN은 어느 PC에서나 로그인.</summary>
        public AuthLoginResult Login(string userName, string password, string teamName)
        {
            var result = new AuthLoginResult();
            string name;
            string team;
            string hash;
            string pcNm;
            if (!ValidateInputs(userName, password, teamName, requireTeam: false, result, out name, out team, out hash, out pcNm))
                return result;

            bool hasAuth = HasAuthSchema();
            DirectoryUserDto existing = FindAccount(name, hasAuth);
            if (existing == null)
            {
                // Old PC-based upsert may have renamed this PC's row to 관리자 — reclaim if password matches.
                AuthLoginResult reclaimed = TryReclaimCorruptedLocalAccount(
                    name, team, hash, pcNm, hasAuth, result);
                if (reclaimed != null)
                    return reclaimed;

                result.ErrorMessage = "계정이 없습니다. 회원가입을 먼저 해 주세요.";
                return result;
            }
            if (!existing.IsActive)
            {
                result.ErrorMessage = "비활성화된 계정입니다.";
                return result;
            }

            if (hasAuth)
            {
                if (string.IsNullOrWhiteSpace(existing.PasswordHash))
                {
                    result.ErrorMessage = "비밀번호가 없습니다. 회원가입에서 등록해 주세요.";
                    return result;
                }
                if (!SecureEquals(existing.PasswordHash.Trim(), hash))
                {
                    result.ErrorMessage = "비밀번호가 옳지 않습니다. 로그인 화면의 [비밀번호 재설정]을 이용해 주세요.";
                    return result;
                }
            }

            // Admin only when the typed login is ADMIN/관리자.
            // Do NOT elevate just because the matched row still has a corrupted LOGIN_ID=ADMIN
            // from older PC-based upserts (same machine ≠ admin account).
            if (IsAdminLoginId(name)
                || string.Equals(name, AdminDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                result.Success = true;
                result.IsAdmin = true;
                result.UserName = AdminDisplayName;
                // 관리자는 로그인 폼에 남아있던 이전 소속 입력값(예: "진료지원")을 절대 물려받지 않는다 —
                // "관리자 · 진료지원"처럼 일반 사용자로 오인되는 표시를 막기 위해 항상 고정값을 쓴다.
                result.TeamName = AdminTeamName;
                result.PasswordPersistedToDb = hasAuth;
                result.UserId = existing.UserId;
                return result;
            }

            if (string.IsNullOrWhiteSpace(team))
                team = existing.TeamName;

            // Repair corrupted rows: never leave LOGIN_ID=ADMIN on a normal user.
            return PersistSession(name, team, hasAuth ? hash : null, pcNm, hasAuth, result,
                successNoteWithoutAuth: null);
        }

        /// <summary>신규 가입. 이미 비밀번호 있는 계정이면 거부. ADMIN 아이디는 가입 불가.</summary>
        public AuthLoginResult Register(string userName, string password, string teamName)
        {
            var result = new AuthLoginResult();
            string name;
            string team;
            string hash;
            string pcNm;
            if (!ValidateInputs(userName, password, teamName, requireTeam: true, result, out name, out team, out hash, out pcNm))
                return result;

            if (IsAdminLoginId(name) || string.Equals(name, AdminDisplayName, StringComparison.OrdinalIgnoreCase))
            {
                result.ErrorMessage = "관리자 계정은 회원가입할 수 없습니다.";
                return result;
            }

            bool hasAuth = HasAuthSchema();
            DirectoryUserDto existing = FindAccount(name, hasAuth);
            if (existing != null && hasAuth && !string.IsNullOrWhiteSpace(existing.PasswordHash))
            {
                result.ErrorMessage = "이미 가입된 사용자입니다. 로그인해 주세요.";
                return result;
            }

            if (!hasAuth)
            {
                return PersistSession(name, team, null, pcNm, false, result, successNoteWithoutAuth: null);
            }

            return PersistSession(name, team, hash, pcNm, true, result, successNoteWithoutAuth: null);
        }

        /// <summary>로그인된 사용자 비밀번호 변경. 실패 시 메시지, 성공 시 null.</summary>
        public string ChangePassword(string currentPassword, string newPassword)
        {
            string name = OccupancyNameStore.TryGet();
            if (string.IsNullOrWhiteSpace(name))
                return "로그인이 필요합니다.";
            if (string.IsNullOrEmpty(currentPassword))
                return "현재 비밀번호를 입력해 주세요.";
            if (string.IsNullOrEmpty(newPassword))
                return "새 비밀번호를 입력해 주세요.";
            if (!IsAsciiLetterOrDigitOnly(newPassword))
                return "비밀번호는 영문과 숫자만 사용할 수 있습니다.";
            if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
                return "새 비밀번호가 현재와 같습니다.";

            if (!HasAuthSchema())
                return "비밀번호 컬럼이 없습니다.";

            DirectoryUserDto existing = FindAccount(name.Trim(), true);
            if (existing == null)
                return "계정을 찾지 못했습니다.";
            if (string.IsNullOrWhiteSpace(existing.PasswordHash))
                return "등록된 비밀번호가 없습니다. 다시 가입해 주세요.";
            if (!SecureEquals(existing.PasswordHash.Trim(), HashPassword(currentPassword)))
                return "현재 비밀번호가 옳지 않습니다.";

            string key = !string.IsNullOrWhiteSpace(existing.LoginId) ? existing.LoginId : existing.UserName;
            int n = _repository.UpdatePasswordHash(key, HashPassword(newPassword));
            if (n <= 0)
                return "비밀번호 저장에 실패했습니다.";
            DirectoryBiz.InvalidateUserCache();
            return null;
        }

        /// <summary>
        /// 이 PC에 묶인 계정만, 현재 비번 없이 새 비번 설정 (복구용).
        /// </summary>
        public string ResetPasswordOnThisPc(string userName, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(userName))
                return "이름을 입력해 주세요.";
            if (string.IsNullOrEmpty(newPassword))
                return "새 비밀번호를 입력해 주세요.";
            if (!IsAsciiLetterOrDigitOnly(newPassword))
                return "비밀번호는 영문과 숫자만 사용할 수 있습니다.";
            if (!HasAuthSchema())
                return "비밀번호 컬럼이 없습니다.";

            string name = userName.Trim();
            if (IsAdminLoginId(name) || string.Equals(name, AdminDisplayName, StringComparison.OrdinalIgnoreCase))
                return "관리자 비밀번호는 여기서 재설정할 수 없습니다.";

            string pcNm = RemotePcShareBiz.LocalClientPc;
            string ip = RemotePcShareBiz.LocalAccessIp;
            DirectoryUserDto existing = FindAccount(name, true);
            if (existing == null)
            {
                existing = _repository.FindClaimableUserOnLocalEndpoint(pcNm, ip);
                if (existing == null)
                    return "이 PC에서 해당 계정을 찾지 못했습니다.";
            }

            bool onThisPc =
                (!string.IsNullOrWhiteSpace(existing.LocalPcName)
                    && string.Equals(existing.LocalPcName.Trim(), pcNm, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(existing.LocalPcIp)
                    && !string.IsNullOrWhiteSpace(ip)
                    && string.Equals(existing.LocalPcIp.Trim(), ip.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!onThisPc)
                return "이 PC에 등록된 계정만 재설정할 수 있습니다.";

            // Keep display name as typed (repair 관리자 → 김수현).
            if (existing.UserId.HasValue
                && (IsAdminLoginId(existing.LoginId)
                    || IsAdminLoginId(existing.UserName)
                    || string.Equals(existing.UserName, AdminDisplayName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existing.UserName, name, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(existing.LoginId, name, StringComparison.OrdinalIgnoreCase)))
            {
                var dto = new DirectoryUserDto
                {
                    UserName = name,
                    LoginId = name,
                    TeamName = existing.TeamName,
                    LocalPcName = pcNm,
                    LocalPcIp = ip,
                    WindowsAccount = RemotePcShareBiz.LocalWindowsAccount,
                    PasswordHash = HashPassword(newPassword),
                    IsActive = true
                };
                int u = _repository.UpdateUserIdentity(existing.UserId.Value, dto);
                if (u <= 0)
                    return "계정 복구에 실패했습니다.";
            }
            else
            {
                string key = !string.IsNullOrWhiteSpace(existing.LoginId) ? existing.LoginId : existing.UserName;
                int n = _repository.UpdatePasswordHash(key, HashPassword(newPassword));
                if (n <= 0)
                    return "비밀번호 저장에 실패했습니다.";
            }

            DirectoryBiz.InvalidateUserCache();
            return null;
        }

        private static bool IsAsciiLetterOrDigitOnly(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')))
                    return false;
            }
            return true;
        }

        /// <summary>관리자: 사용자 목록.</summary>
        public IList<DirectoryUserDto> ListUsersForAdmin()
        {
            if (!OccupancyNameStore.IsAdmin || !_repository.IsConfigured)
                return new List<DirectoryUserDto>();
            return _repository.GetUsers() ?? new List<DirectoryUserDto>();
        }

        /// <summary>관리자: 계정 활성/비활성 (ADMIN 자신은 불가).</summary>
        public string SetUserActiveForAdmin(string loginOrName, bool isActive)
        {
            if (!OccupancyNameStore.IsAdmin)
                return "관리자만 사용할 수 있습니다.";
            if (string.IsNullOrWhiteSpace(loginOrName))
                return "사용자를 지정해 주세요.";
            if (IsAdminLoginId(loginOrName)
                || string.Equals(loginOrName.Trim(), AdminDisplayName, StringComparison.OrdinalIgnoreCase))
                return "관리자 계정은 비활성화할 수 없습니다.";
            if (!_repository.IsConfigured || !_repository.HasUserAuthColumns)
                return "인증 컬럼이 없습니다. sql/20을 적용하세요.";
            int n = _repository.SetUserActive(loginOrName.Trim(), isActive);
            if (n <= 0)
                return "사용자를 찾지 못했거나 저장에 실패했습니다.";
            DirectoryBiz.InvalidateUserCache();
            return null;
        }

        /// <summary>
        /// 관리자: 계정 소프트 삭제/복구(ADMIN 자신은 불가). 실제 DELETE가 아니라 IS_DELETED만
        /// 세운다 — 요청사항/업무기록이 이 USR_ID를 FK로 참조하고 있어 행 자체를 지울 수 없다.
        /// isBuiltInAdmin은 호출자(AdminUserItemViewModel)가 이미 판정한 값을 그대로 받는다.
        /// </summary>
        public string SetUserDeletedForAdmin(long userId, bool isBuiltInAdmin, bool isDeleted)
        {
            if (!OccupancyNameStore.IsAdmin)
                return "관리자만 사용할 수 있습니다.";
            if (userId <= 0)
                return "사용자를 지정해 주세요.";
            if (isBuiltInAdmin)
                return "관리자 계정은 삭제할 수 없습니다.";
            int n = _repository.SetUserDeleted(userId, isDeleted);
            if (n <= 0)
                return "사용자를 찾지 못했거나 저장에 실패했습니다." +
                    (_repository.IsConfigured ? " (sql/30 적용 여부를 확인하세요.)" : string.Empty);
            DirectoryBiz.InvalidateUserCache();
            return null;
        }

        /// <summary>관리자: 개인정보(이름·소속·로그인ID) 수정 팝업 전용. ADMIN 자신은 불가.</summary>
        public string UpdateUserProfileForAdmin(long userId, bool isBuiltInAdmin, string userName, string teamName, string loginId)
        {
            if (!OccupancyNameStore.IsAdmin)
                return "관리자만 사용할 수 있습니다.";
            if (userId <= 0)
                return "사용자를 지정해 주세요.";
            if (isBuiltInAdmin)
                return "관리자 계정 정보는 이 화면에서 수정할 수 없습니다.";
            if (string.IsNullOrWhiteSpace(userName))
                return "이름을 입력해 주세요.";
            if (string.IsNullOrWhiteSpace(loginId))
                return "로그인 ID를 입력해 주세요.";
            if (_repository.LoginIdExists(loginId.Trim(), userId))
                return "이미 사용 중인 로그인 ID입니다.";
            int n = _repository.UpdateUserProfile(userId, userName.Trim(), teamName, loginId.Trim());
            if (n <= 0)
                return "사용자를 찾지 못했거나 저장에 실패했습니다.";
            DirectoryBiz.InvalidateUserCache();
            return null;
        }

        /// <summary>관리자: 일반 사용자 비밀번호 재설정 (ADMIN 계정 제외).</summary>
        public string ResetPasswordForAdmin(string loginOrName, string newPassword)
        {
            if (!OccupancyNameStore.IsAdmin)
                return "관리자만 사용할 수 있습니다.";
            if (string.IsNullOrWhiteSpace(loginOrName))
                return "사용자를 지정해 주세요.";
            if (string.IsNullOrEmpty(newPassword))
                return "새 비밀번호를 입력해 주세요.";
            if (!IsAsciiLetterOrDigitOnly(newPassword))
                return "비밀번호는 영문과 숫자만 사용할 수 있습니다.";
            if (IsAdminLoginId(loginOrName)
                || string.Equals(loginOrName.Trim(), AdminDisplayName, StringComparison.OrdinalIgnoreCase))
                return "관리자 비밀번호는 여기서 재설정할 수 없습니다.";
            if (!HasAuthSchema())
                return "비밀번호 컬럼이 없습니다. sql/20을 적용하세요.";

            DirectoryUserDto existing = FindAccount(loginOrName.Trim(), true);
            if (existing == null)
                return "사용자를 찾지 못했습니다.";
            if (IsAdminLoginId(existing.LoginId)
                || string.Equals(existing.LoginId, AdminDisplayName, StringComparison.OrdinalIgnoreCase)
                || (string.IsNullOrWhiteSpace(existing.LoginId)
                    && (IsAdminLoginId(existing.UserName)
                        || string.Equals(existing.UserName, AdminDisplayName, StringComparison.OrdinalIgnoreCase))))
                return "관리자 비밀번호는 여기서 재설정할 수 없습니다.";

            string key = !string.IsNullOrWhiteSpace(existing.LoginId) ? existing.LoginId : existing.UserName;
            int n = _repository.UpdatePasswordHash(key, HashPassword(newPassword));
            if (n <= 0)
                return "비밀번호 저장에 실패했습니다.";
            DirectoryBiz.InvalidateUserCache();
            return null;
        }

        private bool HasAuthSchema()
        {
            return _repository.IsConfigured && _repository.HasUserAuthColumns;
        }

        private DirectoryUserDto FindAccount(string name, bool hasAuth)
        {
            if (!_repository.IsConfigured)
                return null;

            if (hasAuth)
                return _repository.FindUserForAuth(name);

            IList<DirectoryUserDto> users = _repository.GetUsers();
            if (users == null)
                return null;
            for (int i = 0; i < users.Count; i++)
            {
                DirectoryUserDto u = users[i];
                if (u == null || string.IsNullOrWhiteSpace(u.UserName))
                    continue;
                if (string.Equals(u.UserName.Trim(), name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(u.LoginId, name, StringComparison.OrdinalIgnoreCase))
                {
                    u.IsActive = true;
                    return u;
                }
            }
            return null;
        }

        private static bool ValidateInputs(
            string userName,
            string password,
            string teamName,
            bool requireTeam,
            AuthLoginResult result,
            out string name,
            out string team,
            out string hash,
            out string pcNm)
        {
            name = null;
            team = null;
            hash = null;
            pcNm = null;

            if (string.IsNullOrWhiteSpace(userName))
            {
                result.ErrorMessage = "이름을 입력해 주세요.";
                return false;
            }
            if (string.IsNullOrEmpty(password))
            {
                result.ErrorMessage = "비밀번호를 입력해 주세요.";
                return false;
            }
            if (requireTeam && string.IsNullOrWhiteSpace(teamName))
            {
                result.ErrorMessage = "소속을 입력해 주세요.";
                return false;
            }

            name = userName.Trim();
            team = string.IsNullOrWhiteSpace(teamName) ? null : teamName.Trim();
            hash = HashPassword(password);
            pcNm = RemotePcShareBiz.LocalClientPc;
            if (string.IsNullOrWhiteSpace(pcNm))
            {
                result.ErrorMessage = "이 PC 이름을 확인할 수 없습니다.";
                return false;
            }
            return true;
        }

        private AuthLoginResult TryReclaimCorruptedLocalAccount(
            string name,
            string team,
            string hash,
            string pcNm,
            bool hasAuth,
            AuthLoginResult result)
        {
            if (IsAdminLoginId(name) || string.Equals(name, AdminDisplayName, StringComparison.OrdinalIgnoreCase))
                return null;
            if (!_repository.IsConfigured)
                return null;

            DirectoryUserDto local = _repository.FindClaimableUserOnLocalEndpoint(
                pcNm, RemotePcShareBiz.LocalAccessIp);
            if (local == null || !local.UserId.HasValue)
                return null;

            // Only reclaim rows that look like the overwritten-admin accident.
            bool looksCorrupted =
                IsAdminLoginId(local.LoginId)
                || IsAdminLoginId(local.UserName)
                || string.Equals(local.UserName, AdminDisplayName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(local.LoginId, AdminDisplayName, StringComparison.OrdinalIgnoreCase);
            if (!looksCorrupted)
                return null;

            if (hasAuth)
            {
                if (string.IsNullOrWhiteSpace(local.PasswordHash))
                {
                    result.ErrorMessage = "비밀번호가 없습니다. 회원가입에서 등록해 주세요.";
                    return result;
                }
                if (!SecureEquals(local.PasswordHash.Trim(), hash))
                {
                    result.ErrorMessage = "비밀번호가 옳지 않습니다.";
                    return result;
                }
            }

            if (string.IsNullOrWhiteSpace(team))
                team = local.TeamName;

            var dto = new DirectoryUserDto
            {
                UserName = name,
                LoginId = name,
                TeamName = team,
                LocalPcName = pcNm.Trim(),
                LocalPcIp = RemotePcShareBiz.LocalAccessIp,
                WindowsAccount = RemotePcShareBiz.LocalWindowsAccount,
                PasswordHash = hasAuth ? hash : local.PasswordHash,
                IsActive = true
            };

            int n = _repository.UpdateUserIdentity(local.UserId.Value, dto);
            if (n < 0)
            {
                result.ErrorMessage = "사용자 정보를 DB에 저장하지 못했습니다.";
                return result;
            }

            DirectoryBiz.InvalidateUserCache();
            result.Success = true;
            result.IsAdmin = false;
            result.UserName = name;
            result.TeamName = team;
            result.PasswordPersistedToDb = hasAuth;
            result.UserId = local.UserId;
            return result;
        }

        private AuthLoginResult PersistSession(
            string name,
            string team,
            string passwordHashOrNull,
            string pcNm,
            bool hasAuth,
            AuthLoginResult result,
            string successNoteWithoutAuth)
        {
            var dto = new DirectoryUserDto
            {
                UserName = name,
                LoginId = name,
                TeamName = team,
                LocalPcName = pcNm.Trim(),
                LocalPcIp = RemotePcShareBiz.LocalAccessIp,
                WindowsAccount = RemotePcShareBiz.LocalWindowsAccount,
                PasswordHash = passwordHashOrNull,
                IsActive = true
            };

            if (_repository.IsConfigured)
            {
                int n = _repository.UpsertUser(dto);
                if (n < 0)
                {
                    result.ErrorMessage = "사용자 정보를 DB에 저장하지 못했습니다.";
                    return result;
                }
                result.PasswordPersistedToDb = hasAuth && !string.IsNullOrEmpty(passwordHashOrNull);
                result.UserId = dto.UserId;
                DirectoryBiz.InvalidateUserCache();
            }
            else
            {
                result.PasswordPersistedToDb = false;
            }

            result.Success = true;
            result.IsAdmin = false;
            result.UserName = name;
            result.TeamName = team;
            if (_repository.IsConfigured && !hasAuth && !string.IsNullOrWhiteSpace(successNoteWithoutAuth))
                result.ErrorMessage = successNoteWithoutAuth;
            else if (!_repository.IsConfigured)
                result.ErrorMessage = "DB 미연결: 이 PC에만 로그인 상태가 저장됩니다.";
            return result;
        }

        public static string HashPassword(string password)
        {
            using (var sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(password ?? string.Empty));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                    sb.Append(bytes[i].ToString("x2"));
                return sb.ToString();
            }
        }

        private static bool SecureEquals(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
