using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.UI.Features.Launcher.Support;
using GenLauncherGO.UI.Features.Mods;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Support;

[Collection("Avalonia")]
public sealed class LauncherDragDropControllerTests
{
    [Fact]
    public void DragGesture_CompletesMoveAndSuppressesSelectionToggle()
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            ModificationViewModel target = CreateTile("Target");
            using PointerGestureFixture fixture = new(source, target);

            fixture.Drag(source, target);

            fixture.MoveStarted.Should().BeTrue();
            fixture.MoveCompleted.Should().BeTrue();
            fixture.SelectionCleared.Should().BeFalse();
            fixture.SourceIndex.Should().Be(0);
            fixture.TargetIndex.Should().Be(1);
            fixture.Controller.IsDragging.Should().BeFalse();
            source.IsDragAndDropVisible.Should().BeFalse();
        });
    }

    [Fact]
    public void DragGesture_WhenReorderIsNotAllowed_LeavesTheListOrderUntouched()
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            ModificationViewModel target = CreateTile("Target");
            using PointerGestureFixture fixture = new(false, source, target);
            bool dragPresentationShown = false;
            source.PropertyChanged += (_, args) =>
                dragPresentationShown |=
                    args.PropertyName == nameof(ModificationViewModel.IsDragAndDropVisible) &&
                    source.IsDragAndDropVisible;

            fixture.Drag(source, target);

            fixture.MoveStarted.Should().BeFalse();
            fixture.MoveCompleted.Should().BeFalse();
            fixture.SourceIndex.Should().Be(-1);
            fixture.TargetIndex.Should().Be(-1);
            dragPresentationShown.Should().BeFalse();
        });
    }

    [Fact]
    public void DragGesture_DoesNotChangeSemanticSelection()
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel advertising = CreateTile(
                "Sponsor",
                ModificationType.Advertising,
                false);
            ModificationViewModel selectedModification = CreateTile("Selected");
            using PointerGestureFixture fixture = new(advertising, selectedModification);

            fixture.Drag(advertising, selectedModification);

            fixture.MoveCompleted.Should().BeTrue();
            advertising.IsSelected.Should().BeFalse();
            selectedModification.IsSelected.Should().BeTrue();
            fixture.ModsList.SelectedItem.Should().BeNull();
        });
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(7, true)]
    public void DragGesture_StartsOnlyBeyondTheMovementTolerance(
        double verticalMovement,
        bool shouldStart)
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            ModificationViewModel target = CreateTile("Target");
            using PointerGestureFixture fixture = new(source, target);

            fixture.BeginDrag(source, verticalMovement);

            fixture.MoveStarted.Should().Be(shouldStart);
            fixture.Controller.IsDragging.Should().Be(shouldStart);
            source.IsDragAndDropVisible.Should().Be(shouldStart);
        });
    }

    [Fact]
    public void ClickGesture_SelectsOnlyAfterPointerRelease()
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source", isSelected: false);
            using PointerGestureFixture fixture = new(source);

            fixture.Press(source);

            fixture.ModsList.SelectedItem.Should().BeNull();

            fixture.Release(source);

            fixture.ModsList.SelectedItem.Should().BeSameAs(source);
        });
    }

    [Fact]
    public void CapturePointerGesture_WithTheRightButton_LeavesSelectionAndTheGestureUntouched()
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            using PointerGestureFixture fixture = new(source);

            fixture.RightClick(source);

            fixture.Controller.IsDragging.Should().BeFalse();
            fixture.SelectionCleared.Should().BeFalse();
            fixture.PressHandled.Should().BeFalse();
            source.IsSelected.Should().BeTrue();
        });
    }

    [Fact]
    public void AutoScroll_AtTopLimit_DoesNotRewriteOffset()
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            using PointerGestureFixture fixture = new(source);

            bool scrolled = LauncherDragDropController.ScrollDuringDrag(fixture.ModsList, 0);

            scrolled.Should().BeFalse();
        });
    }

    [Fact]
    public void ScrollDuringDrag_NearTheTopEdge_MovesTheOffsetTowardsTheTop()
    {
        StaTestRunner.Run(() =>
        {
            using PointerGestureFixture fixture = CreateScrollableFixture();
            LauncherDragDropController.RestoreVerticalScrollOffset(fixture.ModsList, 60);

            bool scrolled = LauncherDragDropController.ScrollDuringDrag(fixture.ModsList, 0);

            scrolled.Should().BeTrue();
            LauncherDragDropController.GetVerticalScrollOffset(fixture.ModsList)
                .Should().BeApproximately(45, 0.1);
        });
    }

    [Fact]
    public void ScrollDuringDrag_NearTheBottomEdge_MovesTheOffsetTowardsTheBottom()
    {
        StaTestRunner.Run(() =>
        {
            using PointerGestureFixture fixture = CreateScrollableFixture();
            LauncherDragDropController.RestoreVerticalScrollOffset(fixture.ModsList, 60);

            bool scrolled = LauncherDragDropController.ScrollDuringDrag(
                fixture.ModsList,
                fixture.ModsList.Bounds.Height - 10);

            scrolled.Should().BeTrue();
            LauncherDragDropController.GetVerticalScrollOffset(fixture.ModsList)
                .Should().BeApproximately(75, 0.1);
        });
    }

    [Fact]
    public void ScrollDuringDrag_AwayFromBothEdges_KeepsTheOffset()
    {
        StaTestRunner.Run(() =>
        {
            using PointerGestureFixture fixture = CreateScrollableFixture();
            LauncherDragDropController.RestoreVerticalScrollOffset(fixture.ModsList, 60);

            bool scrolled = LauncherDragDropController.ScrollDuringDrag(
                fixture.ModsList,
                fixture.ModsList.Bounds.Height / 2);

            scrolled.Should().BeFalse();
            LauncherDragDropController.GetVerticalScrollOffset(fixture.ModsList)
                .Should().BeApproximately(60, 0.1);
        });
    }

    [Fact]
    public void ScrollDuringDrag_AtTheBottomLimit_DoesNotRewriteOffset()
    {
        StaTestRunner.Run(() =>
        {
            using PointerGestureFixture fixture = CreateScrollableFixture();
            LauncherDragDropController.RestoreVerticalScrollOffset(fixture.ModsList, double.MaxValue);
            double bottomOffset = LauncherDragDropController.GetVerticalScrollOffset(fixture.ModsList);

            bool scrolled = LauncherDragDropController.ScrollDuringDrag(
                fixture.ModsList,
                fixture.ModsList.Bounds.Height - 10);

            scrolled.Should().BeFalse();
            bottomOffset.Should().BeGreaterThan(0);
            LauncherDragDropController.GetVerticalScrollOffset(fixture.ModsList).Should().Be(bottomOffset);
        });
    }

    [Fact]
    public void RestoreVerticalScrollOffset_RestoresSavedPosition()
    {
        StaTestRunner.Run(() =>
        {
            using PointerGestureFixture fixture = new(
                CreateTile("One"),
                CreateTile("Two"),
                CreateTile("Three"),
                CreateTile("Four"),
                CreateTile("Five"));

            LauncherDragDropController.RestoreVerticalScrollOffset(fixture.ModsList, 75);

            LauncherDragDropController.GetVerticalScrollOffset(fixture.ModsList)
                .Should().BeApproximately(75, 0.1);
        });
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RestoreVerticalScrollOffset_WhenSavedPositionIsNotFinite_UsesTop(double verticalOffset)
    {
        StaTestRunner.Run(() =>
        {
            using PointerGestureFixture fixture = CreateScrollableFixture();
            LauncherDragDropController.RestoreVerticalScrollOffset(fixture.ModsList, 75);

            LauncherDragDropController.RestoreVerticalScrollOffset(fixture.ModsList, verticalOffset);

            LauncherDragDropController.GetVerticalScrollOffset(fixture.ModsList).Should().Be(0);
        });
    }

    [Theory]
    [InlineData(6, true)]
    [InlineData(7, false)]
    public void SelectedTileClick_ClearsSelectionOnlyWithinMovementTolerance(
        double horizontalMovement,
        bool shouldClear)
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            using PointerGestureFixture fixture = new(source);

            fixture.Click(source, horizontalMovement);

            fixture.MoveCompleted.Should().BeFalse();
            fixture.SelectionCleared.Should().Be(shouldClear);
            source.IsSelected.Should().Be(!shouldClear);
        });
    }

    [Theory]
    [InlineData("Button")]
    [InlineData("ComboBox")]
    [InlineData("MenuItem")]
    public void InteractiveTileChild_DoesNotCapturePointerGesture(string childKind)
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            Control? interactiveChild = null;
            using PointerGestureFixture fixture = new(
                _ => interactiveChild = CreateInteractiveChild(childKind),
                source);

            fixture.Interact(interactiveChild!);

            fixture.MoveStarted.Should().BeFalse();
            fixture.MoveCompleted.Should().BeFalse();
            fixture.SelectionCleared.Should().BeFalse();
            source.IsDragAndDropVisible.Should().BeFalse();
        });
    }

    [Fact]
    public void NestedForeignListItem_DoesNotCapturePointerGesture()
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            ListBox? nestedList = null;
            using PointerGestureFixture fixture = new(
                _ => nestedList = new ListBox
                {
                    Height = 40,
                    ItemsSource = new[] { "Nested item" }
                },
                source);

            fixture.Interact(nestedList!);

            fixture.MoveStarted.Should().BeFalse();
            fixture.MoveCompleted.Should().BeFalse();
            fixture.SelectionCleared.Should().BeFalse();
        });
    }

    [Fact]
    public void CancelPointerGesture_WhenSelectionTogglePending_SuppressesToggleOnRelease()
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            using PointerGestureFixture fixture = new(source);
            fixture.Press(source);

            fixture.Controller.CancelPointerGesture();
            fixture.Release(source);

            fixture.SelectionCleared.Should().BeFalse();
            source.IsSelected.Should().BeTrue();
        });
    }

    [Fact]
    public void CancelPointerGesture_WhenDragging_ClearsDragPresentation()
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            ModificationViewModel target = CreateTile("Target");
            using PointerGestureFixture fixture = new(source, target);
            fixture.BeginDrag(source);

            fixture.Controller.CancelPointerGesture();

            fixture.MoveStarted.Should().BeTrue();
            fixture.Controller.IsDragging.Should().BeFalse();
            source.IsDragAndDropVisible.Should().BeFalse();
        });
    }

    private static PointerGestureFixture CreateScrollableFixture()
    {
        return new PointerGestureFixture(
            Enumerable.Range(1, 10)
                .Select(number => CreateTile($"Mod {number}"))
                .ToArray());
    }

    private static Control CreateInteractiveChild(string childKind)
    {
        return childKind switch
        {
            "Button" => new Button
            {
                Width = 180,
                Height = 40,
                Background = Brushes.Transparent,
                Content = "Action"
            },
            "ComboBox" => new ComboBox
            {
                Width = 180,
                Height = 40,
                Background = Brushes.Transparent,
                ItemsSource = new[] { "Option" },
                SelectedIndex = 0
            },
            "MenuItem" => new MenuItem { Header = "Action" },
            _ => throw new ArgumentOutOfRangeException(nameof(childKind), childKind, null)
        };
    }

    private static ModificationViewModel CreateTile(
        string name,
        ModificationType modificationType = ModificationType.Mod,
        bool isSelected = true)
    {
        ModificationViewModel tile = TestModificationTile.Create(
            new LauncherContent(TestLauncherContent.Version(
                name,
                type: modificationType,
                installed: true,
                isSelected: true)));
        tile.IsSelected = isSelected;
        return tile;
    }

    private sealed class PointerGestureFixture : IDisposable
    {
        private readonly Window _window;

        public PointerGestureFixture(params ModificationViewModel[] modifications)
            : this(null, true, modifications)
        {
        }

        public PointerGestureFixture(bool canReorder, params ModificationViewModel[] modifications)
            : this(null, canReorder, modifications)
        {
        }

        public PointerGestureFixture(
            Func<ModificationViewModel, Control>? contentFactory,
            params ModificationViewModel[] modifications)
            : this(contentFactory, true, modifications)
        {
        }

        private PointerGestureFixture(
            Func<ModificationViewModel, Control>? contentFactory,
            bool canReorder,
            params ModificationViewModel[] modifications)
        {
            Controller = new LauncherDragDropController();
            ModsList = new ListBox
            {
                Width = 240,
                Height = 160,
                ItemsSource = modifications,
                ItemTemplate = new FuncDataTemplate<ModificationViewModel>((item, _) => new Border
                {
                    Width = 220,
                    Height = 60,
                    Background = Brushes.Transparent,
                    Child = contentFactory?.Invoke(item) ??
                            new TextBlock { Text = item.ContainerModification.Name }
                })
            };
            _window = new Window
            {
                Width = 240,
                Height = 160,
                Content = ModsList,
                ShowInTaskbar = false
            };

            ModsList.AddHandler(
                InputElement.PointerPressedEvent,
                (_, eventArgs) =>
                {
                    Controller.CapturePointerGesture(
                        ModsList,
                        _window,
                        eventArgs,
                        canReorder);
                    PressHandled = eventArgs.Handled;
                },
                RoutingStrategies.Tunnel);
            ModsList.AddHandler(
                InputElement.PointerMovedEvent,
                (_, eventArgs) =>
                    MoveStarted |= Controller.HandlePointerMove(ModsList, eventArgs),
                RoutingStrategies.Tunnel);
            ModsList.AddHandler(
                InputElement.PointerReleasedEvent,
                (_, eventArgs) =>
                {
                    MoveCompleted = Controller.TryCompletePointerGesture(
                        ModsList,
                        eventArgs,
                        out bool selectionCleared,
                        out int sourceIndex,
                        out int targetIndex);
                    SelectionCleared = selectionCleared;
                    SourceIndex = sourceIndex;
                    TargetIndex = targetIndex;
                },
                RoutingStrategies.Tunnel);
            ModsList.PointerCaptureLost += (_, _) => Controller.CancelPointerGesture();

            _window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public LauncherDragDropController Controller { get; }

        public ListBox ModsList { get; }

        public bool MoveStarted { get; private set; }

        public bool MoveCompleted { get; private set; }

        public bool PressHandled { get; private set; }

        public bool SelectionCleared { get; private set; }

        public int SourceIndex { get; private set; } = -1;

        public int TargetIndex { get; private set; } = -1;

        public void Dispose()
        {
            Controller.CancelPointerGesture();
            _window.Close();
        }

        public void Click(ModificationViewModel source, double horizontalMovement)
        {
            Point sourcePoint = GetItemPoint(source);
            ResetOutcomes();
            MouseDown(sourcePoint);
            MouseUp(new Point(sourcePoint.X + horizontalMovement, sourcePoint.Y));
        }

        public void Interact(Control child)
        {
            Point start = GetControlPoint(child);
            ResetOutcomes();
            MouseDown(start);
            _window.MouseMove(
                new Point(start.X, start.Y + 12),
                RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
            MouseUp(start);
        }

        public void Press(ModificationViewModel source)
        {
            ResetOutcomes();
            MouseDown(GetItemPoint(source));
        }

        public void Release(ModificationViewModel target)
        {
            MouseUp(GetItemPoint(target));
        }

        public void RightClick(ModificationViewModel source)
        {
            Point sourcePoint = GetItemPoint(source);
            ResetOutcomes();
            _window.MouseDown(
                sourcePoint,
                MouseButton.Right,
                RawInputModifiers.RightMouseButton);
            Dispatcher.UIThread.RunJobs();
            _window.MouseUp(sourcePoint, MouseButton.Right);
            Dispatcher.UIThread.RunJobs();
        }

        public void BeginDrag(ModificationViewModel source, double verticalMovement = 12)
        {
            Point sourcePoint = GetItemPoint(source);
            ResetOutcomes();
            MouseDown(sourcePoint);
            _window.MouseMove(
                new Point(sourcePoint.X, sourcePoint.Y + verticalMovement),
                RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
        }

        public void Drag(
            ModificationViewModel source,
            ModificationViewModel target)
        {
            BeginDrag(source);
            _window.MouseUp(
                GetItemPoint(target),
                MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        private void MouseDown(Point point)
        {
            _window.MouseDown(
                point,
                MouseButton.Left,
                RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
        }

        private void MouseUp(Point point)
        {
            _window.MouseUp(
                point,
                MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        private void ResetOutcomes()
        {
            MoveStarted = false;
            MoveCompleted = false;
            PressHandled = false;
            SelectionCleared = false;
            SourceIndex = -1;
            TargetIndex = -1;
        }

        private Point GetControlPoint(Control control)
        {
            Point? translated = control.TranslatePoint(
                new Point(
                    Math.Max(1, control.Bounds.Width / 2),
                    Math.Max(1, control.Bounds.Height / 2)),
                _window);
            return translated ??
                   throw new InvalidOperationException(
                       "The interactive child position could not be resolved.");
        }

        private Point GetItemPoint(ModificationViewModel item)
        {
            Control container = ModsList.ContainerFromItem(item)
                                ?? throw new InvalidOperationException(
                                    "The list item container was not generated.");
            Point? translated = container.TranslatePoint(
                new Point(10, Math.Min(10, container.Bounds.Height / 2)),
                _window);
            return translated ??
                   throw new InvalidOperationException(
                       "The list item position could not be resolved.");
        }
    }
}
