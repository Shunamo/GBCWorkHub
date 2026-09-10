using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.DTO;

namespace GBCWorkHub.DAC
{
    public interface IDirectoryRepository
    {
        bool IsConfigured { get; }

        int UpsertUser(DirectoryUserDto user);
        /// <summary>LOGIN_ID 또는 USER_NM으로 조회. 인증 컬럼 없으면 null.</summary>
        DirectoryUserDto FindUserForAuth(string loginOrName);
        /// <summary>이 PC(이름/IP)에 등록된 사용자. ADMIN 시드 행은 제외.</summary>
        DirectoryUserDto FindUserByLocalEndpoint(string pcName, string pcIp);
        /// <summary>
        /// 이 PC에 묶인 계정(관리자로 덮인 행 포함). LOCAL_PC_NM='ADMIN' 시드만 제외.
        /// 이전 PC MERGE 사고로 USER_NM이 관리자가 된 행 복구용.
        /// </summary>
        DirectoryUserDto FindClaimableUserOnLocalEndpoint(string pcName, string pcIp);
        /// <summary>USR_ID로 로그인 식별자·표시명 갱신(복구).</summary>
        int UpdateUserIdentity(long userId, DirectoryUserDto user);
        /// <summary>LOGIN_ID 또는 USER_NM의 PASSWORD_HASH 갱신.</summary>
        int UpdatePasswordHash(string loginOrName, string passwordHash);
        bool HasUserAuthColumns { get; }
        int UpsertPcMap(PcMapDto map);
        /// <summary>PC_COMMENT만 갱신(빈 문자열 허용). 컬럼 없으면 0.</summary>
        int UpdatePcComment(string siteCode, string pcName, string comment);
        /// <summary>PC_NOTE / PC_COMMENT / PC_DOMAIN 일괄 저장(편집 모드).</summary>
        int UpdatePcAccess(string siteCode, string pcName, string pcNote, string pcComment, string pcDomain);
        IList<string> ResolvePcAliases(string siteCode, string value);
        IList<DirectoryUserDto> GetUsers();
        /// <summary>LOGIN_ID 또는 USER_NM으로 IS_ACTIVE 갱신. 인증 컬럼 없으면 0.</summary>
        int SetUserActive(string loginOrName, bool isActive);
        /// <summary>실제 DELETE 대신 IS_DELETED만 세운다(소프트 삭제). 컬럼 없으면 0.</summary>
        int SetUserDeleted(long userId, bool isDeleted);
        /// <summary>관리자 개인정보 수정 팝업 전용 — 이름/소속/로그인ID만 좁게 갱신.</summary>
        int UpdateUserProfile(long userId, string userName, string teamName, string loginId);
        /// <summary>LOGIN_ID 유니크 제약 위반 여부를 저장 전에 확인.</summary>
        bool LoginIdExists(string loginId, long excludeUserId);
        IList<PcMapDto> GetPcMapsBySite(string siteCode);
        /// <summary>사이트+PC명으로 PCMAP 행 삭제.</summary>
        int DeletePcMap(string siteCode, string pcName);
        /// <summary>가입한 사용자들이 실제로 입력한 소속(TEAM_NM) 중복 제거 목록. 업무기록 소속 필터에 사용.</summary>
        IList<string> GetDistinctTeamNames();

        Task<int> UpsertUserAsync(DirectoryUserDto user);
        Task<DirectoryUserDto> FindUserForAuthAsync(string loginOrName);
        Task<DirectoryUserDto> FindUserByLocalEndpointAsync(string pcName, string pcIp);
        Task<int> UpsertPcMapAsync(PcMapDto map);
        Task<int> UpdatePcCommentAsync(string siteCode, string pcName, string comment);
        Task<int> UpdatePcAccessAsync(string siteCode, string pcName, string pcNote, string pcComment, string pcDomain);
        Task<IList<string>> ResolvePcAliasesAsync(string siteCode, string value);
        Task<IList<DirectoryUserDto>> GetUsersAsync();
        Task<IList<PcMapDto>> GetPcMapsBySiteAsync(string siteCode);
        Task<int> DeletePcMapAsync(string siteCode, string pcName);
        Task<IList<string>> GetDistinctTeamNamesAsync();
    }
}
