using System.Threading;
using System.Threading.Tasks;

namespace GBCWorkHub.UI.Services.Update
{
    public interface IUpdateService
    {
        string CurrentVersion { get; }
        Task<UpdateManifest> CheckForUpdateAsync(CancellationToken cancellationToken);
        Task ApplyUpdateAsync(UpdateManifest manifest, CancellationToken cancellationToken);
    }
}
