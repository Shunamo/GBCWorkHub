using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.BIZ;
using GBCWorkHub.BIZ.Improvement;
using GBCWorkHub.DAC;
using GBCWorkHub.DTO;

namespace GBCWorkHub.BIZ.Tests
{
    /// <summary>
    /// 사용자 식별 정규화 테스트 (USR_ID = 불변 신원, LOGIN_ID = 계정 유일키, LOCAL_PC_NM = 참고정보).
    /// 테스트 프레임워크 없이 Program.Main()에서 직접 호출한다.
    /// </summary>
    public static class IdentityTests
    {
        public static void RunAll(Action<string, bool> assertTrue)
        {
            Test1_SameLoginSamePc_SameUserId(assertTrue);
            Test2_SameLoginDifferentPc_SameUserId(assertTrue);
            Test3_DifferentLoginSamePc_DifferentUserId_FirstUserUntouched(assertTrue);
            Test4_DifferentLoginDifferentPc_DifferentUserId(assertTrue);
            Test5_DisplayNameChange_UserIdUnchanged(assertTrue);
            Test6_AdminAndUserLogin_RolePreserved(assertTrue);
            Test7_ReactionOwnership_UserIdBased_NotNameBased(assertTrue);
        }

        // 1) 같은 LOGIN_ID, 같은 PC → 같은 USR_ID
        private static void Test1_SameLoginSamePc_SameUserId(Action<string, bool> assertTrue)
        {
            var repo = new FakeDirectoryRepository();
            var first = repo.UpsertAndReturn("userA", "userA", "PC01");
            var second = repo.UpsertAndReturn("userA", "userA", "PC01");
            assertTrue("1) 같은 LOGIN_ID/같은 PC -> 같은 USR_ID", first.UserId.HasValue && first.UserId == second.UserId);
        }

        // 2) 같은 LOGIN_ID, 다른 PC → 같은 USR_ID (PC는 식별에 참여하지 않음)
        private static void Test2_SameLoginDifferentPc_SameUserId(Action<string, bool> assertTrue)
        {
            var repo = new FakeDirectoryRepository();
            var onPc01 = repo.UpsertAndReturn("userA", "userA", "PC01");
            var onPc02 = repo.UpsertAndReturn("userA", "userA", "PC02");
            assertTrue("2) 같은 LOGIN_ID/다른 PC -> 같은 USR_ID", onPc01.UserId == onPc02.UserId);
            assertTrue("2) LOCAL_PC_NM은 최신 PC로 갱신됨", string.Equals(repo.FindUserForAuth("userA").LocalPcName, "PC02", StringComparison.OrdinalIgnoreCase));
        }

        // 3) 다른 LOGIN_ID, 같은 PC(공유 PC) → 다른 USR_ID, 기존 사용자는 그대로 유지되어야 함 (핵심 회귀 테스트)
        private static void Test3_DifferentLoginSamePc_DifferentUserId_FirstUserUntouched(Action<string, bool> assertTrue)
        {
            var repo = new FakeDirectoryRepository();
            var userA = repo.UpsertAndReturn("userA", "사용자A", "PC01");
            var userB = repo.UpsertAndReturn("userB", "사용자B", "PC01"); // 같은 PC에서 신규 가입

            assertTrue("3) 다른 LOGIN_ID -> 다른 USR_ID", userA.UserId != userB.UserId);

            var reloadedA = repo.FindUserForAuth("userA");
            assertTrue("3) 기존 사용자 A의 USR_ID 불변", reloadedA.UserId == userA.UserId);
            assertTrue("3) 기존 사용자 A의 표시명이 B로 덮어써지지 않음", string.Equals(reloadedA.UserName, "사용자A", StringComparison.Ordinal));
            assertTrue("3) 기존 사용자 A의 LOGIN_ID가 B로 바뀌지 않음", string.Equals(reloadedA.LoginId, "userA", StringComparison.OrdinalIgnoreCase));
        }

        // 4) 다른 LOGIN_ID, 다른 PC → 다른 USR_ID
        private static void Test4_DifferentLoginDifferentPc_DifferentUserId(Action<string, bool> assertTrue)
        {
            var repo = new FakeDirectoryRepository();
            var userA = repo.UpsertAndReturn("userA", "userA", "PC01");
            var userB = repo.UpsertAndReturn("userB", "userB", "PC02");
            assertTrue("4) 다른 LOGIN_ID/다른 PC -> 다른 USR_ID", userA.UserId != userB.UserId);
        }

        // 5) 표시명 변경 → USR_ID 불변
        private static void Test5_DisplayNameChange_UserIdUnchanged(Action<string, bool> assertTrue)
        {
            var repo = new FakeDirectoryRepository();
            var before = repo.UpsertAndReturn("userA", "김수현", "PC01");
            var after = repo.UpsertAndReturn("userA", "김수현(개명)", "PC01");
            assertTrue("5) 표시명 변경해도 USR_ID 불변", before.UserId == after.UserId);
            assertTrue("5) 표시명은 실제로 갱신됨", string.Equals(after.UserName, "김수현(개명)", StringComparison.Ordinal));
        }

        // 6) 관리자/일반 사용자 로그인 → 기존 권한 동작 보존
        private static void Test6_AdminAndUserLogin_RolePreserved(Action<string, bool> assertTrue)
        {
            var repo = new FakeDirectoryRepository();
            long adminId = repo.SeedWithPassword(AuthBiz.AdminLoginId, AuthBiz.AdminDisplayName, "ADMIN", "ADMIN123");
            long userId = repo.SeedWithPassword("userA", "사용자A", "PC01", "Passw0rd");

            var auth = new AuthBiz(repo);

            var adminResult = auth.Login(AuthBiz.AdminLoginId, "ADMIN123", null);
            assertTrue("6) ADMIN 로그인 성공", adminResult.Success);
            assertTrue("6) ADMIN 로그인 IsAdmin=true", adminResult.IsAdmin);
            assertTrue("6) ADMIN 로그인 UserId 일치", adminResult.UserId == adminId);

            var userResult = auth.Login("userA", "Passw0rd", null);
            assertTrue("6) 일반 사용자 로그인 성공", userResult.Success);
            assertTrue("6) 일반 사용자 IsAdmin=false", !userResult.IsAdmin);
            assertTrue("6) 일반 사용자 UserId 일치", userResult.UserId == userId);

            var wrongPassword = auth.Login("userA", "wrong", null);
            assertTrue("6) 잘못된 비밀번호는 실패", !wrongPassword.Success);
        }

        // 7) 개선사항 반응 소유권 — 이름이 아니라 USER_ID 기준
        private static void Test7_ReactionOwnership_UserIdBased_NotNameBased(Action<string, bool> assertTrue)
        {
            long userAId = 10;
            long userBId = 11;

            // 이름이 같아도(동명이인) USER_ID가 다르면 서로 다른 사람 — 이름 매칭이었다면 여기서 오탐 발생.
            assertTrue(
                "7) 동명이인은 USER_ID로 구분됨(이름 매칭 아님)",
                !ImprovementOwnership.IsOwner(currentUserId: userBId, resourceUserId: userAId));

            assertTrue(
                "7) 본인 USER_ID면 소유자로 판정",
                ImprovementOwnership.IsOwner(currentUserId: userAId, resourceUserId: userAId));

            assertTrue(
                "7) 관리자는 소유자가 아니어도 관리 가능",
                ImprovementOwnership.CanManage(currentUserId: userBId, resourceUserId: userAId, isAdmin: true));

            assertTrue(
                "7) 관리자가 아니고 본인도 아니면 불가",
                !ImprovementOwnership.CanManage(currentUserId: userBId, resourceUserId: userAId, isAdmin: false));
        }

        /// <summary>
        /// UpsertUserWithAuth와 동일한 규칙(LOGIN_ID 우선 매칭, PC 충돌 폴백 없음)을 인메모리로 재현한
        /// 테스트 전용 Repository. 실제 Oracle 없이 식별 로직만 검증한다.
        /// </summary>
        private sealed class FakeDirectoryRepository : IDirectoryRepository
        {
            private readonly List<DirectoryUserDto> _rows = new List<DirectoryUserDto>();
            private long _nextId = 1;

            public bool IsConfigured { get { return true; } }
            public bool HasUserAuthColumns { get { return true; } }

            public DirectoryUserDto UpsertAndReturn(string loginId, string userName, string localPcName)
            {
                var dto = new DirectoryUserDto
                {
                    UserName = userName,
                    LoginId = loginId,
                    LocalPcName = localPcName,
                    TeamName = null,
                    IsActive = true
                };
                UpsertUser(dto);
                return dto;
            }

            public long SeedWithPassword(string loginId, string userName, string localPcName, string password)
            {
                var dto = new DirectoryUserDto
                {
                    UserName = userName,
                    LoginId = loginId,
                    LocalPcName = localPcName,
                    PasswordHash = AuthBiz.HashPassword(password),
                    IsActive = true
                };
                UpsertUser(dto);
                return dto.UserId.Value;
            }

            public int UpsertUser(DirectoryUserDto user)
            {
                if (user == null || string.IsNullOrWhiteSpace(user.UserName) || string.IsNullOrWhiteSpace(user.LocalPcName))
                    return 0;

                string loginId = string.IsNullOrWhiteSpace(user.LoginId) ? user.UserName : user.LoginId;

                // 1) LOGIN_ID로 기존 행 매칭 — 유일한 신원 앵커.
                DirectoryUserDto row = _rows.Find(r => string.Equals(r.LoginId, loginId, StringComparison.OrdinalIgnoreCase));
                if (row != null)
                {
                    row.UserName = user.UserName;
                    row.TeamName = user.TeamName;
                    row.LocalPcName = user.LocalPcName;
                    row.LocalPcIp = user.LocalPcIp;
                    row.WindowsAccount = user.WindowsAccount;
                    if (!string.IsNullOrWhiteSpace(user.PasswordHash))
                        row.PasswordHash = user.PasswordHash;
                    row.IsActive = user.IsActive;
                    user.UserId = row.UserId;
                    return 1;
                }

                // 2) 신규 계정 — LOCAL_PC_NM이 겹쳐도(공유 PC) 절대 기존 행을 재사용하지 않는다.
                row = new DirectoryUserDto
                {
                    UserId = _nextId++,
                    UserName = user.UserName,
                    TeamName = user.TeamName,
                    LocalPcName = user.LocalPcName,
                    LocalPcIp = user.LocalPcIp,
                    WindowsAccount = user.WindowsAccount,
                    LoginId = loginId,
                    PasswordHash = user.PasswordHash,
                    IsActive = user.IsActive
                };
                _rows.Add(row);
                user.UserId = row.UserId;
                return 1;
            }

            public DirectoryUserDto FindUserForAuth(string loginOrName)
            {
                if (string.IsNullOrWhiteSpace(loginOrName))
                    return null;
                string n = loginOrName.Trim();
                DirectoryUserDto row = _rows.Find(r => string.Equals(r.LoginId, n, StringComparison.OrdinalIgnoreCase))
                    ?? _rows.Find(r => string.Equals(r.UserName, n, StringComparison.OrdinalIgnoreCase));
                return row == null ? null : Clone(row);
            }

            public DirectoryUserDto FindUserByLocalEndpoint(string pcName, string pcIp) { return null; }
            public DirectoryUserDto FindClaimableUserOnLocalEndpoint(string pcName, string pcIp) { return null; }
            public int UpdateUserIdentity(long userId, DirectoryUserDto user) { return 0; }
            public int UpdatePasswordHash(string loginOrName, string passwordHash) { return 0; }
            public int UpsertPcMap(PcMapDto map) { return 0; }
            public int UpdatePcComment(string siteCode, string pcName, string comment) { return 0; }
            public int UpdatePcAccess(string siteCode, string pcName, string pcNote, string pcComment, string pcDomain) { return 0; }
            public IList<string> ResolvePcAliases(string siteCode, string value) { return new List<string>(); }
            public IList<DirectoryUserDto> GetUsers() { return new List<DirectoryUserDto>(_rows); }
            public int SetUserActive(string loginOrName, bool isActive) { return 0; }
            public int SetUserDeleted(long userId, bool isDeleted) { return 0; }
            public int UpdateUserProfile(long userId, string userName, string teamName, string loginId) { return 0; }
            public bool LoginIdExists(string loginId, long excludeUserId) { return false; }
            public IList<PcMapDto> GetPcMapsBySite(string siteCode) { return new List<PcMapDto>(); }
            public int DeletePcMap(string siteCode, string pcName) { return 0; }

            public Task<int> UpsertUserAsync(DirectoryUserDto user) { return Task.FromResult(UpsertUser(user)); }
            public Task<DirectoryUserDto> FindUserForAuthAsync(string loginOrName) { return Task.FromResult(FindUserForAuth(loginOrName)); }
            public Task<DirectoryUserDto> FindUserByLocalEndpointAsync(string pcName, string pcIp) { return Task.FromResult(FindUserByLocalEndpoint(pcName, pcIp)); }
            public Task<int> UpsertPcMapAsync(PcMapDto map) { return Task.FromResult(0); }
            public Task<int> UpdatePcCommentAsync(string siteCode, string pcName, string comment) { return Task.FromResult(0); }
            public Task<int> UpdatePcAccessAsync(string siteCode, string pcName, string pcNote, string pcComment, string pcDomain) { return Task.FromResult(0); }
            public Task<IList<string>> ResolvePcAliasesAsync(string siteCode, string value) { return Task.FromResult((IList<string>)new List<string>()); }
            public Task<IList<DirectoryUserDto>> GetUsersAsync() { return Task.FromResult(GetUsers()); }
            public Task<IList<PcMapDto>> GetPcMapsBySiteAsync(string siteCode) { return Task.FromResult((IList<PcMapDto>)new List<PcMapDto>()); }
            public Task<int> DeletePcMapAsync(string siteCode, string pcName) { return Task.FromResult(0); }

            private static DirectoryUserDto Clone(DirectoryUserDto d)
            {
                return new DirectoryUserDto
                {
                    UserId = d.UserId,
                    UserName = d.UserName,
                    TeamName = d.TeamName,
                    LocalPcName = d.LocalPcName,
                    LocalPcIp = d.LocalPcIp,
                    WindowsAccount = d.WindowsAccount,
                    LoginId = d.LoginId,
                    PasswordHash = d.PasswordHash,
                    IsActive = d.IsActive
                };
            }
        }
    }
}
