using System.ComponentModel;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using FocusGuard.Desktop.Resources;

namespace FocusGuard.Desktop;

public partial class FocusWorkspaceView : UserControl
{
    private INotifyPropertyChanged? _observedViewModel;
    private string? _displayedRingValue;
    private string? _displayedRingCaption;
    private readonly List<IDisposable> _digitFadeCancellations = [];

    public FocusWorkspaceView()
    {
        InitializeComponent();
        KeyDown += (_, e) => { if (e.Key == Key.Escape && DataContext is FocusViewModel { IsSessionSetupOpen: true } vm) { vm.CloseSessionSetup(); e.Handled = true; } };
        DataContextChanged += OnDataContextChanged;
        AttachedToVisualTree += (_, _) => Observe(DataContext as INotifyPropertyChanged);
        DetachedFromVisualTree += (_, _) => Observe(null);
    }

    private void OnDataContextChanged(object? sender, EventArgs e) =>
        Observe(DataContext as INotifyPropertyChanged);

    private void Observe(INotifyPropertyChanged? viewModel)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModelPropertyChanged;
        }

        CancelDigitFades();
        _displayedRingValue = null;
        _displayedRingCaption = null;

        _observedViewModel = viewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged += ViewModelPropertyChanged;
        }

        if (viewModel is FocusViewModel focusViewModel)
        {
            SyncRingCopy(focusViewModel, animate: false);
        }
    }

    private void ViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FocusViewModel.IsSessionSetupOpen) && sender is FocusViewModel setup)
        {
            if (setup.IsSessionSetupOpen) Dispatcher.UIThread.Post(() => SessionSetupPanel.Focus());
            else if (setup.IsFocusVisible && !setup.IsActive && !setup.IsStrictReviewVisible) Dispatcher.UIThread.Post(() => HomeStart.Focus());
        }

        if (e.PropertyName is nameof(FocusViewModel.IsActive)
            or nameof(FocusViewModel.IsCycleMode)
            or nameof(FocusViewModel.RingCenterValue)
            or nameof(FocusViewModel.RingCenterCaption)
            or nameof(FocusViewModel.IsReduceMotionEnabled))
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    if (sender is FocusViewModel focusViewModel)
                    {
                        var ringTextChanged = e.PropertyName is nameof(FocusViewModel.RingCenterValue)
                            or nameof(FocusViewModel.RingCenterCaption);
                        SyncRingCopy(focusViewModel, animate: ringTextChanged);
                        if (e.PropertyName == nameof(FocusViewModel.IsReduceMotionEnabled)
                            && focusViewModel.IsReduceMotionEnabled)
                        {
                            SnapRingCopy();
                        }
                    }
                },
                DispatcherPriority.Loaded);
        }
    }

    private void SyncRingCopy(FocusViewModel viewModel, bool animate)
    {
        var value = viewModel.RingCenterValue;
        var caption = viewModel.RingCenterCaption;
        if (string.Equals(_displayedRingValue, value, StringComparison.Ordinal)
            && string.Equals(_displayedRingCaption, caption, StringComparison.Ordinal))
        {
            return;
        }

        var previous = _displayedRingValue;
        _displayedRingValue = value;
        _displayedRingCaption = caption;
        RingCaptionText.Text = caption;
        AutomationProperties.SetName(RingDigitsPanel, value);
        UpdateRingDigits(previous, value, animate && !viewModel.IsReduceMotionEnabled);
    }

    private void SnapRingCopy()
    {
        CancelDigitFades();
        if (_displayedRingValue is { } value)
        {
            UpdateRingDigits(null, value, animate: false);
        }
    }

    private void UpdateRingDigits(string? previous, string value, bool animate)
    {
        CancelDigitFades();
        var oldSlots = RingDigitsPanel.Children.OfType<Grid>().ToArray();
        var canReuseSlots = previous?.Length == value.Length && oldSlots.Length == value.Length;
        var fontSize = TimerDigitLayout.FontSizeFor(value.Length);
        if (!canReuseSlots)
        {
            RingDigitsPanel.Children.Clear();
            foreach (var character in value)
            {
                RingDigitsPanel.Children.Add(CreateDigitSlot(character, fontSize));
            }

            return;
        }

        for (var index = 0; index < value.Length; index++)
        {
            if (previous![index] == value[index])
            {
                continue;
            }

            var slot = oldSlots[index];
            var outgoing = slot.Children.OfType<TextBlock>().Single();
            var incoming = CreateDigitText(value[index], fontSize);
            slot.Children.Add(incoming);
            if (!animate)
            {
                slot.Children.Remove(outgoing);
                continue;
            }

            incoming.Opacity = 0;
            incoming.Transitions = DigitOpacityTransitions();
            outgoing.Transitions = DigitOpacityTransitions();
            outgoing.Opacity = 0;
            incoming.Opacity = 1;
            IDisposable? cancellation = null;
            cancellation = DispatcherTimer.RunOnce(
                () =>
                {
                    slot.Children.Remove(outgoing);
                    if (cancellation is not null)
                    {
                        _digitFadeCancellations.Remove(cancellation);
                    }
                },
                TimeSpan.FromMilliseconds(180),
                DispatcherPriority.Render);
            _digitFadeCancellations.Add(cancellation);
        }
    }

    private static Grid CreateDigitSlot(char character, double fontSize)
    {
        var slot = new Grid();
        slot.Children.Add(CreateDigitText(character, fontSize));
        return slot;
    }

    private static TextBlock CreateDigitText(char character, double fontSize) => new()
    {
        Text = character.ToString(),
        FontSize = fontSize,
        FontWeight = FontWeight.SemiBold,
        Foreground = Application.Current?.TryFindResource("FgTextPrimary", out var brush) == true
            ? brush as IBrush
            : Brushes.White,
        TextAlignment = TextAlignment.Center,
        MinWidth = character == ':' ? fontSize * 0.34 : fontSize * 0.57,
    };

    private static Transitions DigitOpacityTransitions() =>
    [
        new DoubleTransition
        {
            Property = OpacityProperty,
            Duration = TimeSpan.FromMilliseconds(160),
        },
    ];

    private void CancelDigitFades()
    {
        foreach (var cancellation in _digitFadeCancellations)
        {
            cancellation.Dispose();
        }

        _digitFadeCancellations.Clear();
        foreach (var slot in RingDigitsPanel.Children.OfType<Grid>())
        {
            while (slot.Children.Count > 1)
            {
                slot.Children.RemoveAt(0);
            }

            if (slot.Children.FirstOrDefault() is Control digit)
            {
                digit.Opacity = 1;
                digit.Transitions = null;
            }
        }
    }

    private void ConfigureSession(object? sender, RoutedEventArgs e) => (DataContext as FocusViewModel)?.OpenSessionSetup();
    private void CloseSetup(object? sender, RoutedEventArgs e) => (DataContext as FocusViewModel)?.CloseSessionSetup();
    private void ProfileMenuRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (DataContext is not FocusViewModel { CanNavigateToEditors: true } vm
            || sender is not Button { DataContext: HomeProfileViewModel profile } button) return;
        vm.SelectProfile(profile.Profile);
        var menu = new ContextMenu
        {
            ItemsSource = new[]
            {
                new MenuItem { Header = UiStrings.HomeConfigure, Command = profile.OpenCommand },
                new MenuItem { Header = UiStrings.DuplicateProfile, Command = vm.DuplicateProfileCommand },
                new MenuItem
                {
                    Header = UiStrings.DeleteProfile,
                    Command = new AsyncRuleCommand(() =>
                    {
                        vm.ShowSection(NavigationSection.Profiles);
                        return vm.RequestProfileDeleteAsync();
                    }, () => vm.CanDeleteProfile),
                },
            },
        };
        button.ContextMenu = menu;
        menu.Open(button);
        e.Handled = true;
    }
    private async void OpenSchedule(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FocusViewModel vm) return;
        vm.ShowSection(NavigationSection.Schedule);
        await vm.LoadSchedulesAsync();
    }

    private void OpenSettingsClick(object? sender, RoutedEventArgs e) =>
        (DataContext as FocusViewModel)?.ShowSection(NavigationSection.Settings);

    private void OpenCompact(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as MainWindow)?.OpenCompactWindow();

    private void OpenProfilesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FocusViewModel viewModel)
        {
            viewModel.ShowSection(NavigationSection.Profiles);
        }
    }

    private async void OpenActivityClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is FocusViewModel viewModel)
        {
            await viewModel.ShowActivityAsync();
        }
    }
}

internal static class TimerDigitLayout
{
    internal static double FontSizeFor(int characterCount) => characterCount switch
    {
        <= 5 => 60,
        6 => 54,
        7 => 48,
        _ => 42,
    };
}
