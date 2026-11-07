using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using sinmai_launcher.Core.Models;

namespace sinmai_launcher.Core.Services
{
    public class ProfilesContainer
    {
        public string? ActiveProfileId { get; set; }
        public List<Profile> Profiles { get; set; } = new();
    }

    public class ProfileService : IProfileService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _profilesFilePath;
        private readonly string _activeProfileMarkerPath;

        public string ProfilesFilePath => _profilesFilePath;

        public ProfileService(string? profilesFilePath = null)
        {
            if (!string.IsNullOrEmpty(profilesFilePath))
            {
                _profilesFilePath = profilesFilePath;
                var dir = Path.GetDirectoryName(_profilesFilePath) ?? AppContext.BaseDirectory;
                _activeProfileMarkerPath = Path.Combine(dir, "active_profile.txt");
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var folder = Path.Combine(appData, "sinmai-launcher");
                Directory.CreateDirectory(folder);
                _profilesFilePath = Path.Combine(folder, "profiles.json");
                _activeProfileMarkerPath = Path.Combine(folder, "active_profile.txt");
            }
        }

        public async Task<List<Profile>> LoadProfilesAsync()
        {
            if (!File.Exists(_profilesFilePath))
            {
                return new List<Profile>();
            }

            try
            {
                var json = await File.ReadAllTextAsync(_profilesFilePath);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return new List<Profile>();
                }

                // Check if container or list
                if (json.TrimStart().StartsWith("{"))
                {
                    var container = JsonSerializer.Deserialize<ProfilesContainer>(json, JsonOptions);
                    return container?.Profiles ?? new List<Profile>();
                }
                else
                {
                    var list = JsonSerializer.Deserialize<List<Profile>>(json, JsonOptions);
                    return list ?? new List<Profile>();
                }
            }
            catch
            {
                return new List<Profile>();
            }
        }

        public async Task SaveProfilesAsync(List<Profile> profiles)
        {
            var dir = Path.GetDirectoryName(_profilesFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string? activeId = null;
            if (File.Exists(_activeProfileMarkerPath))
            {
                try { activeId = (await File.ReadAllTextAsync(_activeProfileMarkerPath)).Trim(); } catch { }
            }

            if (string.IsNullOrEmpty(activeId) && profiles.Any())
            {
                activeId = profiles.First().Id;
            }

            var container = new ProfilesContainer
            {
                ActiveProfileId = activeId,
                Profiles = profiles
            };

            var json = JsonSerializer.Serialize(container, JsonOptions);
            await File.WriteAllTextAsync(_profilesFilePath, json);
        }

        public async Task<Profile?> GetProfileByIdAsync(string id)
        {
            var profiles = await LoadProfilesAsync();
            return profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<Profile> CreateProfileAsync(Profile profile)
        {
            if (string.IsNullOrEmpty(profile.Id))
            {
                profile.Id = Guid.NewGuid().ToString("N");
            }

            var profiles = await LoadProfilesAsync();
            profiles.Add(profile);
            await SaveProfilesAsync(profiles);

            // If active profile is not set, set this one
            var currentActive = await GetActiveProfileAsync();
            if (currentActive == null)
            {
                await SetActiveProfileAsync(profile.Id);
            }

            return profile;
        }

        public async Task UpdateProfileAsync(Profile profile)
        {
            var profiles = await LoadProfilesAsync();
            var index = profiles.FindIndex(p => string.Equals(p.Id, profile.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                profiles[index] = profile;
                await SaveProfilesAsync(profiles);
            }
            else
            {
                profiles.Add(profile);
                await SaveProfilesAsync(profiles);
            }
        }

        public async Task DeleteProfileAsync(string id)
        {
            var profiles = await LoadProfilesAsync();
            profiles.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            await SaveProfilesAsync(profiles);

            var active = await GetActiveProfileAsync();
            if (active != null && string.Equals(active.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                var next = profiles.FirstOrDefault();
                if (next != null)
                {
                    await SetActiveProfileAsync(next.Id);
                }
                else if (File.Exists(_activeProfileMarkerPath))
                {
                    File.Delete(_activeProfileMarkerPath);
                }
            }
        }

        public async Task<Profile?> GetActiveProfileAsync()
        {
            var profiles = await LoadProfilesAsync();
            if (!profiles.Any())
            {
                return null;
            }

            string? activeId = null;
            if (File.Exists(_activeProfileMarkerPath))
            {
                try { activeId = (await File.ReadAllTextAsync(_activeProfileMarkerPath)).Trim(); } catch { }
            }

            if (!string.IsNullOrEmpty(activeId))
            {
                var found = profiles.FirstOrDefault(p => string.Equals(p.Id, activeId, StringComparison.OrdinalIgnoreCase));
                if (found != null)
                {
                    return found;
                }
            }

            return profiles.First();
        }

        public async Task SetActiveProfileAsync(string id)
        {
            var dir = Path.GetDirectoryName(_activeProfileMarkerPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            await File.WriteAllTextAsync(_activeProfileMarkerPath, id);
        }
    }
}
