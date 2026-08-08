using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MasterBookWritingSystem.App.ViewModels;
using MasterBookWritingSystem.Core.Hierarchy;

namespace MasterBookWritingSystem.App.Views;

public partial class ManuscriptView : UserControl
{
    private ManuscriptViewModel? _boundViewModel;
    private Point _dragStart;
    private HierarchyNodeViewModel? _dragSource;
    private bool _syncingTreeSelection;

    public ManuscriptView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Unloaded += (_, _) => DetachViewModel();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachViewModel();
        if (e.NewValue is ManuscriptViewModel viewModel)
        {
            _boundViewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
            UpdatePreview(viewModel.PreviewHtml);
            SyncTreeSelection(viewModel.SelectedNode);
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_boundViewModel is null)
        {
            return;
        }

        if (e.PropertyName == nameof(ManuscriptViewModel.PreviewHtml))
        {
            UpdatePreview(_boundViewModel.PreviewHtml);
        }
        else if (e.PropertyName == nameof(ManuscriptViewModel.EditorSessionVersion))
        {
            MarkdownEditor.UndoLimit = 0;
            MarkdownEditor.UndoLimit = 100;
        }
        else if (e.PropertyName == nameof(ManuscriptViewModel.SelectedNode))
        {
            SyncTreeSelection(_boundViewModel.SelectedNode);
        }
    }

    private void SyncTreeSelection(HierarchyNodeViewModel? node)
    {
        if (_syncingTreeSelection)
        {
            return;
        }

        _syncingTreeSelection = true;
        try
        {
            if (node is null)
            {
                return;
            }

            ExpandAncestors(node);
            var item = FindContainer(HierarchyTree, node);
            if (item is not null)
            {
                item.IsSelected = true;
                item.BringIntoView();
            }
        }
        finally
        {
            _syncingTreeSelection = false;
        }
    }

    private void HierarchyTree_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_syncingTreeSelection || _boundViewModel is null)
        {
            return;
        }

        if (e.NewValue is HierarchyNodeViewModel node
            && !ReferenceEquals(_boundViewModel.SelectedNode, node))
        {
            _boundViewModel.SelectedNode = node;
        }
    }

    private void HierarchyTree_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragSource = FindNodeFromVisual(e.OriginalSource as DependencyObject);
    }

    private void HierarchyTree_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragSource is null)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(HierarchyTree, _dragSource, DragDropEffects.Move);
        _dragSource = null;
    }

    private void HierarchyTree_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (_boundViewModel is null
            || e.Data.GetData(typeof(HierarchyNodeViewModel)) is not HierarchyNodeViewModel source)
        {
            e.Handled = true;
            return;
        }

        var target = FindNodeFromVisual(e.OriginalSource as DependencyObject);
        if (target is null)
        {
            e.Handled = true;
            return;
        }

        var action = ManuscriptHierarchyInteractions.ClassifyDrop(
            source.Kind,
            source.Id,
            target.Kind,
            target.Id,
            source.ParentId,
            target.ParentId);
        e.Effects = ManuscriptHierarchyInteractions.IsValidDrop(action)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void HierarchyTree_OnDrop(object sender, DragEventArgs e)
    {
        if (_boundViewModel is null
            || e.Data.GetData(typeof(HierarchyNodeViewModel)) is not HierarchyNodeViewModel source)
        {
            return;
        }

        var target = FindNodeFromVisual(e.OriginalSource as DependencyObject);
        if (target is null)
        {
            return;
        }

        // Gesture translation only — persistence stays in the view-model command.
        if (_boundViewModel.HandleHierarchyDropCommand.CanExecute(null))
        {
            _boundViewModel.HandleHierarchyDropCommand.Execute(new HierarchyDropRequest
            {
                SourceKind = source.Kind,
                SourceId = source.Id,
                TargetKind = target.Kind,
                TargetId = target.Id,
            });
        }
    }

    private void UpdatePreview(string html)
    {
        try
        {
            PreviewBrowser.NavigateToString(string.IsNullOrWhiteSpace(html)
                ? "<html><body></body></html>"
                : html);
        }
        catch
        {
        }
    }

    private void DetachViewModel()
    {
        if (_boundViewModel is not null)
        {
            _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _boundViewModel = null;
        }
    }

    private static HierarchyNodeViewModel? FindNodeFromVisual(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: HierarchyNodeViewModel node })
            {
                return node;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static void ExpandAncestors(HierarchyNodeViewModel node)
    {
        // Expansion is bound on containers; parent IsExpanded is restored via VM reload.
        _ = node;
    }

    private static TreeViewItem? FindContainer(ItemsControl parent, object item)
    {
        if (parent.ItemContainerGenerator.ContainerFromItem(item) is TreeViewItem direct)
        {
            return direct;
        }

        foreach (var child in parent.Items)
        {
            if (parent.ItemContainerGenerator.ContainerFromItem(child) is not TreeViewItem childContainer)
            {
                continue;
            }

            childContainer.IsExpanded = true;
            childContainer.UpdateLayout();
            var match = FindContainer(childContainer, item);
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }
}
