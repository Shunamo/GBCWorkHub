using System.Collections.Generic;
using GBCWorkHub.DAC;
using GBCWorkHub.DTO;

namespace GBCWorkHub.BIZ
{
    /// <summary>
    /// 원격 PC 조회
    /// </summary>
    public class RemotePcBiz
    {
        private readonly RemotePcDac _dac = new RemotePcDac();

        public List<RemoteSiteDto> GetSiteList()
        {
            return _dac.GetSiteList();
        }

        public List<RemotePcDto> GetRemotePcListBySite(string siteCode)
        {
            List<RemotePcDto> list = _dac.GetRemotePcListBySite(siteCode) ?? new List<RemotePcDto>();
            var directory = new DirectoryBiz();
            directory.MergePcMapsIntoRemoteList(siteCode, list);
            if (list.Count > 0)
                directory.ApplyPcTeams(siteCode, list);
            return list;
        }
    }
}
