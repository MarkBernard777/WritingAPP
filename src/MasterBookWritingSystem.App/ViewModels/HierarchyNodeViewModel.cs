using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MasterBookWritingSystem.Core.Domain.Story;
using MasterBookWritingSystem.Core.Hierarchy;

namespace MasterBookWritingSystem.App.ViewModels;

public partial class HierarchyNodeViewModel : ObservableObject
{
    public HierarchyNodeViewModel(
        HierarchyNodeKind kind,
        Guid id,
        string title,
        int sequenceNumber,
        Guid? parentId,
        Guid? editorChapterId = null,
        Scene? scene = null,
        string? relativeMarkdownPath = null,
        int wordCount = 0)
    {
        Kind = kind;
        Id = id;
        ParentId = parentId;
        EditorChapterId = editorChapterId;
        Scene = scene;
        RelativeMarkdownPath = relativeMarkdownPath ?? string.Empty;
        _title = title;
        _sequenceNumber = sequenceNumber;
        _wordCount = wordCount;
    }

    public HierarchyNodeKind Kind { get; }

    public Guid Id { get; }

    public Guid? ParentId { get; }

    /// <summary>Chapter id to load in the editor when this node (or its scene) is selected.</summary>
    public Guid? EditorChapterId { get; }

    public Scene? Scene { get; }

    public string RelativeMarkdownPath { get; }

    public ObservableCollection<HierarchyNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private int _sequenceNumber;

    [ObservableProperty]
    private int _wordCount;

    [ObservableProperty]
    private bool _isExpanded = true;

    [ObservableProperty]
    private bool _isSelectedForCompile = true;

    [ObservableProperty]
    private bool _isVisible = true;

    public bool IsChapter => Kind == HierarchyNodeKind.Chapter;

    public string DisplayName => Kind switch
    {
        HierarchyNodeKind.UnassignedGroup => Title,
        HierarchyNodeKind.Book => $"Book {SequenceNumber}: {Title}",
        HierarchyNodeKind.Part => $"Part {SequenceNumber}: {Title}",
        HierarchyNodeKind.Chapter => $"{SequenceNumber}. {Title}",
        HierarchyNodeKind.Scene => $"{SequenceNumber}. {Title}",
        _ => Title,
    };

    public string AccessibleName => Kind switch
    {
        HierarchyNodeKind.Book => $"Book {Title}",
        HierarchyNodeKind.Part => $"Part {Title}",
        HierarchyNodeKind.Chapter => $"Chapter {Title}, {WordCount} words",
        HierarchyNodeKind.Scene => $"Scene {Title}, status {Scene?.Status}",
        HierarchyNodeKind.UnassignedGroup => "Unassigned scenes",
        HierarchyNodeKind.UngroupedChaptersGroup => "Ungrouped chapters",
        _ => Title,
    };

    public string Glyph => Kind switch
    {
        HierarchyNodeKind.Book => "B",
        HierarchyNodeKind.Part => "P",
        HierarchyNodeKind.Chapter => "C",
        HierarchyNodeKind.Scene => "S",
        _ => "·",
    };

    partial void OnTitleChanged(string value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnSequenceNumberChanged(int value) => OnPropertyChanged(nameof(DisplayName));

    partial void OnWordCountChanged(int value) => OnPropertyChanged(nameof(AccessibleName));
}
