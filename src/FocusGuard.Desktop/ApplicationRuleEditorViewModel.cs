using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using FocusGuard.Desktop.Resources;
using FocusGuard.Domain;

namespace FocusGuard.Desktop;

public sealed record ApplicationMatchOption(string Label, ApplicationMatchKind Value);

public sealed class ApplicationRuleEditorViewModel : INotifyPropertyChanged
{
    private string _valueInput = string.Empty;
    private string _validationMessage = UiStrings.ApplicationEditorPrompt;
    private RuleAction _selectedAction = RuleAction.Block;
    private ApplicationMatchOption _selectedMatchKind;

    public ApplicationRuleEditorViewModel()
    {
        MatchKinds =
        [
            new(UiStrings.ExactPath, ApplicationMatchKind.ExactPath),
            new(UiStrings.Folder, ApplicationMatchKind.Folder),
            new(UiStrings.FileName, ApplicationMatchKind.FileName),
            new(UiStrings.Sha256Hash, ApplicationMatchKind.Sha256)
        ];
        _selectedMatchKind = MatchKinds[0];
        AddRuleCommand = new DelegateCommand(AddRule, () => true);
        Rules.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<RuleAction> Actions { get; } = [RuleAction.Block, RuleAction.Allow];

    public IReadOnlyList<ApplicationMatchOption> MatchKinds { get; }

    public ObservableCollection<ApplicationRuleRowViewModel> Rules { get; } = [];

    public bool IsEmpty => Rules.Count == 0;

    public RuleAction SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (_selectedAction == value)
            {
                return;
            }

            _selectedAction = value;
            OnPropertyChanged();
        }
    }

    public ApplicationMatchOption SelectedMatchKind
    {
        get => _selectedMatchKind;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_selectedMatchKind, value))
            {
                return;
            }

            _selectedMatchKind = value;
            OnPropertyChanged();
        }
    }

    public string ValueInput
    {
        get => _valueInput;
        set
        {
            if (_valueInput == value)
            {
                return;
            }

            _valueInput = value;
            OnPropertyChanged();
        }
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set
        {
            if (_validationMessage == value)
            {
                return;
            }

            _validationMessage = value;
            OnPropertyChanged();
        }
    }

    public ICommand AddRuleCommand { get; }

    public IReadOnlyList<ApplicationRule> SnapshotRules() =>
        Rules.Select(row => row.Rule).ToArray();

    public void LoadRules(IEnumerable<ApplicationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Rules.Clear();
        foreach (var rule in rules)
        {
            Rules.Add(new ApplicationRuleRowViewModel(rule, RemoveRule));
        }

        ValidationMessage = UiStrings.LoadedApplicationRules;
    }

    private void AddRule()
    {
        var result = ApplicationRule.Create(SelectedAction, SelectedMatchKind.Value, ValueInput);
        if (!result.IsValid)
        {
            ValidationMessage = result.Errors[0].Message;
            return;
        }

        var row = new ApplicationRuleRowViewModel(result.Value!, RemoveRule);
        Rules.Add(row);
        ValueInput = string.Empty;
        ValidationMessage = UiStrings.ApplicationDraftAdded;
    }

    private void RemoveRule(ApplicationRuleRowViewModel row)
    {
        Rules.Remove(row);
        ValidationMessage = UiStrings.ApplicationDraftRemoved;
    }

    private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private sealed class DelegateCommand(Action execute, Func<bool> canExecute) : ICommand
    {
        event EventHandler? ICommand.CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => canExecute();

        public void Execute(object? parameter) => execute();
    }
}

public sealed class ApplicationRuleRowViewModel
{
    public ApplicationRuleRowViewModel(ApplicationRule rule, Action<ApplicationRuleRowViewModel> remove)
    {
        Rule = rule;
        RemoveCommand = new RemoveCommandAdapter(() => remove(this));
    }

    public ApplicationRule Rule { get; }

    public ICommand RemoveCommand { get; }

    private sealed class RemoveCommandAdapter(Action execute) : ICommand
    {
        event EventHandler? ICommand.CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => execute();
    }
}
