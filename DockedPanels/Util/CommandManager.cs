using System.Text.RegularExpressions;

namespace SwimEditor
{

  /// <summary>
  /// Generic command registry/dispatcher.
  /// - Register commands with name/aliases/usage + handlers.
  /// - Parse input lines into verb + args and dispatch handlers.
  /// - Format a paged help listing for the registered commands.
  /// </summary>
  public sealed class CommandManager
  {

    private sealed class CommandSpec
    {
      public string Name { get; }
      public IReadOnlyList<string> Aliases { get; }
      public string Usage { get; }
      public Action<string> Handler { get; } // receives raw args string

      public CommandSpec(string name, IEnumerable<string> aliases, string usage, Action<string> handler)
      {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Aliases = (aliases ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Usage = usage ?? string.Empty;
        Handler = handler ?? throw new ArgumentNullException(nameof(handler));
      }

      public IEnumerable<string> AllNames()
      {
        yield return Name;
        foreach (var a in Aliases) yield return a;
      }
    }

    private static readonly Regex CommandRegex = new(@"^\s*(?<verb>\S+)(?:\s+(?<args>.*))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly List<CommandSpec> commands = new List<CommandSpec>();
    private readonly Dictionary<string, CommandSpec> commandMap = new Dictionary<string, CommandSpec>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a command with name, aliases, usage text, and a handler that receives the raw args string.
    /// Last-in wins for duplicate names/aliases.
    /// </summary>
    public void RegisterCommand(string name, IEnumerable<string> aliases, string usage, Action<string> handler)
    {
      var spec = new CommandSpec(name, aliases, usage, handler);

      commands.Add(spec);

      foreach (var key in spec.AllNames())
      {
        if (string.IsNullOrWhiteSpace(key)) continue;
        commandMap[key] = spec;
      }
    }

    /// <summary>
    /// Attempts to parse an input line into a verb and args.
    /// Returns false if the line is empty or does not match the command pattern.
    /// </summary>
    public bool TryParse(string input, out string verb, out string args)
    {
      verb = string.Empty;
      args = string.Empty;

      if (string.IsNullOrWhiteSpace(input))
      {
        return false;
      }

      var m = CommandRegex.Match(input);
      if (!m.Success)
      {
        return false;
      }

      verb = m.Groups["verb"].Value;
      args = m.Groups["args"].Success ? m.Groups["args"].Value : string.Empty;

      return true;
    }

    private CommandSpec Resolve(string verb)
    {
      if (string.IsNullOrWhiteSpace(verb)) return null;
      return commandMap.TryGetValue(verb, out var spec) ? spec : null;
    }

    /// <summary>
    /// Returns true if a command with the given name or alias is registered.
    /// </summary>
    public bool HasCommand(string verb)
    {
      return Resolve(verb) != null;
    }

    /// <summary>
    /// Attempts to invoke a command by verb, passing the raw args string.
    /// Returns false if no such command exists.
    /// Propagates any exception thrown by the handler.
    /// </summary>
    public bool TryInvoke(string verb, string args)
    {
      var spec = Resolve(verb);
      if (spec == null) return false;

      spec.Handler(args);
      return true;
    }

    /// <summary>
    /// Convenience method: parses an input line and, if a known command is found, invokes it.
    /// Returns false if parse fails or no such command exists.
    /// Exceptions from the handler are propagated.
    /// </summary>
    public bool TryExecute(string input)
    {
      if (!TryParse(input, out var verb, out var args))
      {
        return false;
      }

      return TryInvoke(verb, args);
    }

    /// <summary>
    /// Writes a help page (paged overview of commands) using the provided writer.
    /// Page is 1-based. If there are no commands, writes a simple message instead.
    /// </summary>
    public void WriteHelpPage(int page, int pageSize, Action<string> writeLine)
    {
      if (writeLine == null) throw new ArgumentNullException(nameof(writeLine));

      int total = commands.Count;
      if (total == 0)
      {
        writeLine("No commands registered.");
        return;
      }

      pageSize = Math.Max(1, pageSize);
      int totalPages = (total + pageSize - 1) / pageSize;
      page = Math.Max(1, Math.Min(page, totalPages));

      int start = (page - 1) * pageSize;
      int end = Math.Min(start + pageSize, total);

      writeLine($"Commands (page {page}/{totalPages}):");

      for (int i = start; i < end; i++)
      {
        var c = commands[i];
        string aliases = (c.Aliases.Count > 0) ? $" [{string.Join(", ", c.Aliases)}]" : string.Empty;
        // One command per line (compact), followed by usage on the next line
        writeLine($"  {c.Name}{aliases}");
        if (!string.IsNullOrWhiteSpace(c.Usage))
        {
          foreach (var usageLine in c.Usage.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            writeLine($"    {usageLine}");
        }
      }

      if (page < totalPages)
      {
        writeLine($"(Use 'help {page + 1}' for more.)");
      }
    }

  } // class CommandManager

} // Namespace SwimEditor
