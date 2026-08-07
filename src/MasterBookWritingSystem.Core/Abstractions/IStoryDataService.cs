using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Story;

namespace MasterBookWritingSystem.Core.Abstractions;

public interface IStoryDataService
{
    Task<IReadOnlyList<Character>> GetCharactersAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<Character> GetCharacterAsync(
        Guid projectId,
        Guid characterId,
        CancellationToken cancellationToken = default);

    Task<Character> CreateCharacterAsync(
        Guid projectId,
        Character character,
        CancellationToken cancellationToken = default);

    Task<Character> UpdateCharacterAsync(
        Guid projectId,
        Character character,
        CancellationToken cancellationToken = default);

    Task DeleteCharacterAsync(
        Guid projectId,
        Guid characterId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorldEntry>> GetWorldEntriesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<WorldEntry> GetWorldEntryAsync(
        Guid projectId,
        Guid entryId,
        CancellationToken cancellationToken = default);

    Task<WorldEntry> CreateWorldEntryAsync(
        Guid projectId,
        WorldEntry entry,
        CancellationToken cancellationToken = default);

    Task<WorldEntry> UpdateWorldEntryAsync(
        Guid projectId,
        WorldEntry entry,
        CancellationToken cancellationToken = default);

    Task DeleteWorldEntryAsync(
        Guid projectId,
        Guid entryId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Beat>> GetBeatsAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<Beat> GetBeatAsync(
        Guid projectId,
        Guid beatId,
        CancellationToken cancellationToken = default);

    Task<Beat> CreateBeatAsync(
        Guid projectId,
        Beat beat,
        CancellationToken cancellationToken = default);

    Task<Beat> UpdateBeatAsync(
        Guid projectId,
        Beat beat,
        CancellationToken cancellationToken = default);

    Task DeleteBeatAsync(
        Guid projectId,
        Guid beatId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Scene>> GetScenesAsync(
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<Scene> GetSceneAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default);

    Task<Scene> CreateSceneAsync(
        Guid projectId,
        Scene scene,
        CancellationToken cancellationToken = default);

    Task<Scene> UpdateSceneAsync(
        Guid projectId,
        Scene scene,
        CancellationToken cancellationToken = default);

    Task DeleteSceneAsync(
        Guid projectId,
        Guid sceneId,
        CancellationToken cancellationToken = default);

    StoryValidationResult ValidateCharacter(Character character);

    StoryValidationResult ValidateWorldEntry(WorldEntry entry);

    StoryValidationResult ValidateBeat(Beat beat);

    StoryValidationResult ValidateScene(Scene scene);
}
