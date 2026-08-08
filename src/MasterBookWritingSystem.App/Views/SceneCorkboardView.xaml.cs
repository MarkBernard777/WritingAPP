using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MasterBookWritingSystem.App.ViewModels;

namespace MasterBookWritingSystem.App.Views;

public partial class SceneCorkboardView : UserControl
{
    private Point _dragStart;
    private SceneCardViewModel? _dragSource;

    public SceneCorkboardView()
    {
        InitializeComponent();
    }

    private void CardList_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragSource = FindCardFromVisual(e.OriginalSource as DependencyObject);
    }

    private void CardList_OnPreviewMouseMove(object sender, MouseEventArgs e)
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

        DragDrop.DoDragDrop(CardList, _dragSource, DragDropEffects.Move);
        _dragSource = null;
    }

    private void CardList_OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetData(typeof(SceneCardViewModel)) is SceneCardViewModel
            && FindCardFromVisual(e.OriginalSource as DependencyObject) is not null
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void CardList_OnDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not SceneCorkboardViewModel viewModel
            || e.Data.GetData(typeof(SceneCardViewModel)) is not SceneCardViewModel source)
        {
            return;
        }

        var target = FindCardFromVisual(e.OriginalSource as DependencyObject);
        if (target is null || target.Id == source.Id)
        {
            return;
        }

        // Gesture translation only — persistence stays in the view-model command.
        var request = new CorkboardDropRequest
        {
            SourceSceneId = source.Id,
            TargetSceneId = target.Id,
        };
        if (viewModel.HandleCardDropCommand.CanExecute(request))
        {
            viewModel.HandleCardDropCommand.Execute(request);
        }
    }

    private static SceneCardViewModel? FindCardFromVisual(DependencyObject? current)
    {
        while (current is not null)
        {
            if (current is FrameworkElement { DataContext: SceneCardViewModel card })
            {
                return card;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
