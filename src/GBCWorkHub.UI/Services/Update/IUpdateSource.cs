using System.Threading;
using System.Threading.Tasks;

namespace GBCWorkHub.UI.Services.Update
{
    public interface IUpdateSource
    {
        Task<UpdateManifest> GetLatestAsync(CancellationToken cancellationToken);
        Task DownloadPackageAsync(UpdateManifest manifest, string destinationFile, CancellationToken cancellationToken);
    }
}
