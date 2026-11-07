using System.Collections.Generic;
using System.Threading.Tasks;
using sinmai_launcher.Core.Models;

namespace sinmai_launcher.Core.Services
{
    public interface IProfileService
    {
        string ProfilesFilePath { get; }
        Task<List<Profile>> LoadProfilesAsync();
        Task SaveProfilesAsync(List<Profile> profiles);
        Task<Profile?> GetProfileByIdAsync(string id);
        Task<Profile> CreateProfileAsync(Profile profile);
        Task UpdateProfileAsync(Profile profile);
        Task DeleteProfileAsync(string id);
        Task<Profile?> GetActiveProfileAsync();
        Task SetActiveProfileAsync(string id);
    }
}
