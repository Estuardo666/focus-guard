using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using FocusGuard.Desktop.Resources;
using FocusGuard.Domain;

namespace FocusGuard.Desktop;

public sealed class WebsiteRuleEditorViewModel : INotifyPropertyChanged
{
    private string _patternInput = string.Empty;
    private string _validationMessage = UiStrings.WebsiteEditorPrompt;
    private RuleAction _selectedAction = RuleAction.Block;

    public WebsiteRuleEditorViewModel()
    {
        AddRuleCommand = new DelegateCommand(AddRule, () => true);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<RuleAction> Actions { get; } = [RuleAction.Block, RuleAction.Allow];

    public ObservableCollection<WebsiteRuleRowViewModel> Rules { get; } = [];

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

    public string PatternInput
    {
        get => _patternInput;
        set
        {
            if (_patternInput == value)
            {
                return;
            }

            _patternInput = value;
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

    public IReadOnlyList<WebsiteRule> SnapshotRules() =>
        Rules.Select(row => row.Rule).ToArray();

    public void LoadRules(IEnumerable<WebsiteRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        Rules.Clear();
        foreach (var rule in rules)
        {
            Rules.Add(new WebsiteRuleRowViewModel(rule, RemoveRule));
        }

        ValidationMessage = UiStrings.LoadedWebsiteRules;
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void AddRule()
    {
        var result = WebsiteRule.Create(SelectedAction, PatternInput);
        if (!result.IsValid)
        {
            ValidationMessage = result.Errors[0].Message;
            return;
        }

        Rules.Add(new WebsiteRuleRowViewModel(result.Value!, RemoveRule));
        PatternInput = string.Empty;
        ValidationMessage = UiStrings.WebsiteDraftAdded;
        OnPropertyChanged(nameof(IsEmpty));
    }

    private void RemoveRule(WebsiteRuleRowViewModel row)
    {
        Rules.Remove(row);
        ValidationMessage = UiStrings.WebsiteDraftRemoved;
        OnPropertyChanged(nameof(IsEmpty));
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

public sealed class WebsiteRuleRowViewModel
{
    public WebsiteRuleRowViewModel(WebsiteRule rule, Action<WebsiteRuleRowViewModel> remove)
    {
        Rule = rule;
        RemoveCommand = new RemoveCommandAdapter(() => remove(this));
    }

    public WebsiteRule Rule { get; }

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
