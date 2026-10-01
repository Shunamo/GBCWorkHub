using System.Collections.Generic;
using System.Threading.Tasks;
using GBCWorkHub.DAC;
using GBCWorkHub.DTO;

namespace GBCWorkHub.BIZ
{
    /// <summary>"엑셀 시트" 탭 바로가기 목록 — 조회/추가. UI는 DAC를 직접 보지 않고 이걸 통해서만 접근한다.</summary>
    public class ExcelShortcutBiz
    {
        public Task<List<ExcelShortcutDto>> GetAllAsync()
        {
            return Task.Run(() => new OracleExcelShortcutRepository().GetAll());
        }

        /// <summary>성공하면 null, 실패하면 에러 메시지.</summary>
        public Task<string> AddAsync(string name, string url, string iconKey, string createdBy)
        {
            return Task.Run(() => new OracleExcelShortcutRepository().Insert(name, url, iconKey, createdBy));
        }

        /// <summary>성공하면 null, 실패하면 에러 메시지.</summary>
        public Task<string> UpdateAsync(long shortcutId, string name, string url)
        {
            return Task.Run(() => new OracleExcelShortcutRepository().Update(shortcutId, name, url));
        }

        /// <summary>성공하면 null, 실패하면 에러 메시지.</summary>
        public Task<string> DeleteAsync(long shortcutId)
        {
            return Task.Run(() => new OracleExcelShortcutRepository().Delete(shortcutId));
        }
    }
}
