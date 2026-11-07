using System.Threading.Tasks;
using sinmai_launcher.Core.Models;

namespace sinmai_launcher.Core.Services
{
    public interface IGameScannerService
    {
        Task<GameScanResult> ScanGameFolderAsync(string folderPath, GameType? expectedGame = null);
    }
}
