using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using GenLauncherGO.Core.Mods.Contracts;
using GenLauncherGO.Core.Mods.Models;
using GenLauncherGO.Tests.Testing;
using GenLauncherGO.UI.Features.Launcher.Support;
using GenLauncherGO.UI.Features.Mods;
using Microsoft.Extensions.Logging.Abstractions;

namespace GenLauncherGO.Tests.UI.Features.Launcher.Support;

public sealed class LauncherDragDropControllerTests
{
    [Fact]
    public void DragGestureCompletesMoveAndSuppressesSelectionToggle()
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            ModificationViewModel target = CreateTile("Target");
            using PointerGestureFixture fixture = new(source, target);

            fixture.Drag(source, target);

            fixture.MoveStarted.Should().BeTrue();
            fixture.MoveCompleted.Should().BeTrue();
            fixture.SelectionToggleCandidate.Should().BeNull();
            fixture.SourceIndex.Should().Be(0);
            fixture.TargetIndex.Should().Be(1);
            fixture.Controller.IsDragging.Should().BeFalse();
            source.IsDragAndDropVisible.Should().BeFalse();
        });
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(20, false)]
    public void SelectedTileClickCompletesOnlyWithinMovementTolerance(
        double horizontalMovement,
        bool shouldComplete)
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            using PointerGestureFixture fixture = new(source);

            fixture.Click(source, horizontalMovement);

            fixture.MoveCompleted.Should().BeFalse();
            fixture.SelectionToggleCandidate.Should().Be(
                shouldComplete ? source : null);
        });
    }

    [Theory]
    [InlineData("Button")]
    [InlineData("ComboBox")]
    [InlineData("MenuItem")]
    public void InteractiveTileChildDoesNotCapturePointerGesture(string childKind)
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
            fixture.SelectionToggleCandidate.Should().BeNull();
            source.IsDragAndDropVisible.Should().BeFalse();
        });
    }

    [Fact]
    public void NestedForeignListItemDoesNotCapturePointerGesture()
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            ListBox? nestedList = null;
            using PointerGestureFixture fixture = new(
                _ => nestedList = new ListBox
                {
                    Height = 40,
                    ItemsSource = new[] { "Nested item" },
                },
                source);

            fixture.Interact(nestedList!);

            fixture.MoveStarted.Should().BeFalse();
            fixture.MoveCompleted.Should().BeFalse();
            fixture.SelectionToggleCandidate.Should().BeNull();
        });
    }

    [Fact]
    public void CancelPointerGestureClearsPendingSelectionAndActiveDragPresentation()
    {
        StaTestRunner.Run(() =>
        {
            ModificationViewModel source = CreateTile("Source");
            ModificationViewModel target = CreateTile("Target");
            using PointerGestureFixture fixture = new(source, target);

            fixture.Press(source);
            fixture.Controller.CancelPointerGesture();
            fixture.Release(source);
            fixture.SelectionToggleCandidate.Should().BeNull();

            fixture.BeginDrag(source);
            fixture.Controller.IsDragging.Should().BeTrue();
            source.IsDragAndDropVisible.Should().BeTrue();

            fixture.Controller.CancelPointerGesture();

            fixture.Controller.IsDragging.Should().BeFalse();
            source.IsDragAndDropVisible.Should().BeFalse();
        });
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
                Content = "Action",
            },
            "ComboBox" => new ComboBox
            {
                Width = 180,
                Height = 40,
                Background = Brushes.Transparent,
                ItemsSource = new[] { "Option" },
                SelectedIndex = 0,
            },
            "MenuItem" => new MenuItem { Header = "Action" },
            _ => throw new ArgumentOutOfRangeException(nameof(childKind), childKind, null),
        };
    }

    private static ModificationViewModel CreateTile(string name)
    {
        LauncherContentVersion version = new()
        {
            Installation = new LauncherContentInstallation { Installed = true, IsSelected = true },
            Name = name,
            Version = "1.0",
            ModificationType = ModificationType.Mod,
        };
        LauncherContent modification = new(version);

        var viewModel = new ModificationViewModel(
            modification,
            new ModificationImageSourceFactory(NullLogger<ModificationImageSourceFactory>.Instance),
            TestLauncherRuntimeContext.Create(colors: TestLauncherTheme.Create()),
            Substitute.For<IModificationImageFileService>(),
            new TestStringLocalizer(),
            new GenLauncherGO.UI.Features.Integrity.LauncherPackageActivityService(),
            NullLogger<ModificationViewModel>.Instance);
        viewModel.IsSelected = true;
        return viewModel;
    }

    private sealed class PointerGestureFixture : IDisposable
    {
        private readonly Window _window;

        public PointerGestureFixture(params ModificationViewModel[] modifications)
            : this(null, modifications)
        {
        }

        public PointerGestureFixture(
            Func<ModificationViewModel, Control>? contentFactory,
            params ModificationViewModel[] modifications)
        {
            Controller = new LauncherDragDropController();
            ModsList = new ListBox
            {
                Width = 240,
                Height = 160,
                ItemsSource = modifications,
                ItemTemplate = new FuncDataTemplate<ModificationViewModel>(
                    (item, _) => new Border
                    {
                        Width = 220,
                        Height = 60,
                        Background = Brushes.Transparent,
                        Child = contentFactory?.Invoke(item) ??
                                new TextBlock { Text = item.ContainerModification.Name },
                    }),
            };
            _window = new Window
            {
                Width = 240,
                Height = 160,
                Content = ModsList,
                ShowInTaskbar = false,
            };

            ModsList.AddHandler(
                InputElement.PointerPressedEvent,
                (_, eventArgs) =>
                {
                    Controller.CapturePointerGesture(
                        ModsList,
                        _window,
                        eventArgs,
                        canReorder: true);
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
                        out ModificationViewModel? selectionToggleCandidate,
                        out int sourceIndex,
                        out int targetIndex);
                    SelectionToggleCandidate = selectionToggleCandidate;
                    SourceIndex = sourceIndex;
                    TargetIndex = targetIndex;
                },
                RoutingStrategies.Tunnel);

            _window.Show();
            Dispatcher.UIThread.RunJobs();
        }

        public LauncherDragDropController Controller { get; }

        public ListBox ModsList { get; }

        public bool MoveStarted { get; private set; }

        public bool MoveCompleted { get; private set; }

        public ModificationViewModel? SelectionToggleCandidate { get; private set; }

        public int SourceIndex { get; private set; } = -1;

        public int TargetIndex { get; private set; } = -1;

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

        public void BeginDrag(ModificationViewModel source)
        {
            Point sourcePoint = GetItemPoint(source);
            ResetOutcomes();
            MouseDown(sourcePoint);
            _window.MouseMove(
                new Point(sourcePoint.X, sourcePoint.Y + 12),
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
                MouseButton.Left,
                RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        public void Dispose()
        {
            Controller.CancelPointerGesture();
            _window.Close();
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
                MouseButton.Left,
                RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
        }

        private void ResetOutcomes()
        {
            MoveStarted = false;
            MoveCompleted = false;
            SelectionToggleCandidate = null;
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
