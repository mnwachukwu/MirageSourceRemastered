using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Mirage.Server.Shell.ViewModels;

public enum ParameterKind { Text, Choice, Number, Paragraph }

/// <summary>One argument of a command. Its kind picks the control, which is also what stops an operator
/// entering something the server would only reject.</summary>
public sealed partial class CommandParameter : ObservableObject
{
    private CommandParameter(string name, ParameterKind kind) { Name = name; Kind = kind; }

    public static CommandParameter Text(string name) => new(name, ParameterKind.Text);

    /// <summary>Multi-line, for values long enough to want the room.</summary>
    public static CommandParameter Paragraph(string name) => new(name, ParameterKind.Paragraph);

    public static CommandParameter Choice(string name, IReadOnlyList<string> options) =>
        new(name, ParameterKind.Choice) { Options = options, Value = options.FirstOrDefault() ?? "" };

    /// <summary>A bounded whole number — map ids and the like.</summary>
    public static CommandParameter Number(string name, decimal min, decimal max, decimal? initial = null) =>
        new(name, ParameterKind.Number) { Minimum = min, Maximum = max, Amount = initial ?? min };

    /// <summary>Shown as the placeholder; matches the name in the command's own usage line.</summary>
    public string Name { get; }
    public ParameterKind Kind { get; }

    public IReadOnlyList<string>? Options { get; private init; }
    public decimal Minimum { get; private init; }
    public decimal Maximum { get; private init; }

    public bool IsText => Kind == ParameterKind.Text;
    public bool IsChoice => Kind == ParameterKind.Choice;
    public bool IsNumber => Kind == ParameterKind.Number;
    public bool IsParagraph => Kind == ParameterKind.Paragraph;

    [ObservableProperty]
    public partial string Value { get; set; } = "";

    /// <summary>The numeric face of <see cref="Value"/>, for the spinner to bind to.</summary>
    public decimal? Amount
    {
        get => decimal.TryParse(Value, out var d) ? d : null;
        set { Value = value?.ToString() ?? ""; OnPropertyChanged(); }
    }

    /// <summary>The value as it goes on the wire. A paragraph is flattened to one line: the console reads
    /// commands with ReadLine, so an embedded newline would arrive as a SECOND command — and the MOTD is
    /// broadcast to players, whose sprite font cannot draw one anyway.</summary>
    public string AsArgument() =>
        IsParagraph ? string.Join(' ', Value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                    : Value.Trim();
}

/// <summary>A command and its arguments. Composes the same text an operator would type and hands it to
/// the console, so there is one execution path and the CLI stays the source of truth.</summary>
public sealed partial class ShellCommand : ObservableObject
{
    private readonly Action<string> _send;

    public ShellCommand(string verb, string description, Action<string> send, params CommandParameter[] parameters)
    {
        Verb = verb;
        Description = description;
        _send = send;
        Parameters = new ObservableCollection<CommandParameter>(parameters);
    }

    public string Verb { get; }
    public string Description { get; }
    public ObservableCollection<CommandParameter> Parameters { get; }
    public bool HasParameters => Parameters.Count > 0;

    /// <summary>True for commands worth a second look before they fire.</summary>
    public bool NeedsConfirmation { get; init; }

    [ObservableProperty]
    public partial bool IsConfirming { get; private set; }

    [RelayCommand]
    private void Run()
    {
        if (NeedsConfirmation && !IsConfirming) { IsConfirming = true; return; }
        IsConfirming = false;

        // No quoting: the console splits on the first space only, so quoting would make the GUI and a
        // hand-typed command parse differently.
        var text = Verb;
        foreach (var p in Parameters)
        {
            string arg = p.AsArgument();
            if (arg.Length > 0) text += " " + arg;
        }
        _send(text);
    }

    [RelayCommand]
    private void CancelConfirm() => IsConfirming = false;
}

/// <summary>Commands grouped by what they act on, which is how an operator looks for one — "the thing
/// that changes the weather", not "the Developer-tier commands".</summary>
public sealed class CommandGroup(string title, IReadOnlyList<ShellCommand> commands)
{
    public string Title { get; } = title;
    public IReadOnlyList<ShellCommand> Commands { get; } = commands;
}
