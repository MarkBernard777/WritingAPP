using MasterBookWritingSystem.Core.Domain.Story;

namespace MasterBookWritingSystem.Core.Story;

public sealed class StoryValidationResult
{
    public required bool IsValid { get; init; }

    public IReadOnlyList<string> Errors { get; init; } = [];

    public static StoryValidationResult Success() => new() { IsValid = true };

    public static StoryValidationResult Failure(params string[] errors)
        => new() { IsValid = false, Errors = errors };
}

public static class StoryDataValidation
{
    public static StoryValidationResult Validate(Character character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (string.IsNullOrWhiteSpace(character.Name))
        {
            return StoryValidationResult.Failure("Character name is required.");
        }

        return StoryValidationResult.Success();
    }

    public static StoryValidationResult Validate(WorldEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Name))
        {
            return StoryValidationResult.Failure("World entry name is required.");
        }

        if (!Enum.IsDefined(entry.Depth))
        {
            return StoryValidationResult.Failure("World depth is invalid.");
        }

        return StoryValidationResult.Success();
    }

    public static StoryValidationResult Validate(Beat beat)
    {
        ArgumentNullException.ThrowIfNull(beat);
        var errors = new List<string>();
        if (beat.Number < 1)
        {
            errors.Add("Beat number must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(beat.Name))
        {
            errors.Add("Beat name is required.");
        }

        if (!Enum.IsDefined(beat.Status))
        {
            errors.Add("Beat status is invalid.");
        }

        return errors.Count == 0
            ? StoryValidationResult.Success()
            : StoryValidationResult.Failure(errors.ToArray());
    }

    public static StoryValidationResult Validate(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var errors = new List<string>();
        if (scene.SequenceNumber < 1)
        {
            errors.Add("Scene sequence number must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(scene.Title))
        {
            errors.Add("Scene title is required.");
        }

        if (scene.WordCount < 0)
        {
            errors.Add("Scene word count cannot be negative.");
        }

        if (!Enum.IsDefined(scene.Status))
        {
            errors.Add("Scene status is invalid.");
        }

        if (scene.NextSceneId.HasValue && scene.NextSceneId.Value == scene.Id)
        {
            errors.Add("A scene cannot link to itself as the next scene.");
        }

        return errors.Count == 0
            ? StoryValidationResult.Success()
            : StoryValidationResult.Failure(errors.ToArray());
    }
}
