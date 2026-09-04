using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.DTO;

namespace GBCWorkHub.DAC
{
    public interface IDirectoryRepository
    {
        bool IsConfigured { get; }

        int UpsertUser(DirectoryUserDto user);
        int UpsertPcMap(PcMapDto map);
        /// <summary>PC_COMMENT만 갱신(빈 문자열 허용). 컬럼 없으면 0.</summary>
        int UpdatePcComment(string siteCode, string pcName, string comment);
        /// <summary>PC_NOTE / PC_COMMENT / PC_DOMAIN 일괄 저장(편집 모드).</summary>
        int UpdatePcAccess(string siteCode, string pcName, string pcNote, string pcComment, string pcDomain);
        IList<string> ResolvePcAliases(string siteCode, string value);
        IList<DirectoryUserDto> GetUsers();
        IList<PcMapDto> GetPcMapsBySite(string siteCode);

        Task<int> UpsertUserAsync(DirectoryUserDto user);
        Task<int> UpsertPcMapAsync(PcMapDto map);
        Task<int> UpdatePcCommentAsync(string siteCode, string pcName, string comment);
        Task<int> UpdatePcAccessAsync(string siteCode, string pcName, string pcNote, string pcComment, string pcDomain);
        Task<IList<string>> ResolvePcAliasesAsync(string siteCode, string value);
        Task<IList<DirectoryUserDto>> GetUsersAsync();
        Task<IList<PcMapDto>> GetPcMapsBySiteAsync(string siteCode);
    }
}
