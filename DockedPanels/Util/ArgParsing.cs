using System.Globalization;
using System.Text.RegularExpressions;

namespace SwimEditor
{

  public interface IArgSpec
  {
    string Name { get; }
    Type Type { get; }
    object Default { get; }
    bool TryParse(string token, out object value);
  }

  public sealed class ArgSpec<T> : IArgSpec
  {

    public string Name { get; }
    public Type Type => typeof(T);
    public object Default { get; }

    public ArgSpec(string name, T @default)
    {
      if (string.IsNullOrWhiteSpace(name))
      {
        throw new ArgumentException("Argument name cannot be null/empty/whitespace.", nameof(name));
      }

      Name = name;
      Default = @default!;
    }

    public bool TryParse(string token, out object value)
    {
      // Strings always parse (including empty)
      if (typeof(T) == typeof(string))
      {
        value = token ?? string.Empty;
        return true;
      }

      var inv = CultureInfo.InvariantCulture;

      if (typeof(T) == typeof(int))
      {
        bool ok = int.TryParse(token, System.Globalization.NumberStyles.Integer, inv, out var v);
        value = v;
        return ok;
      }

      if (typeof(T) == typeof(float))
      {
        bool ok = float.TryParse(token, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, inv, out var v);
        value = v;
        return ok;
      }

      if (typeof(T) == typeof(double))
      {
        bool ok = double.TryParse(token, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands, inv, out var v);
        value = v;
        return ok;
      }

      if (typeof(T) == typeof(bool))
      {
        bool ok = bool.TryParse(token, out var v);
        value = v;
        return ok;
      }

      try
      {
        value = Convert.ChangeType(token, typeof(T), inv);
        return true;
      }
      catch
      {
        value = default!;
        return false;
      }
    }
  }

  public static class Arg
  {
    public static IArgSpec Create<T>(string name, T @default) => new ArgSpec<T>(name, @default);
    public static IArgSpec Int(string name, int @default = 0) => new ArgSpec<int>(name, @default);
    public static IArgSpec Float(string name, float @default = 0f) => new ArgSpec<float>(name, @default);
    public static IArgSpec Double(string name, double @default = 0d) => new ArgSpec<double>(name, @default);
    public static IArgSpec Bool(string name, bool @default = false) => new ArgSpec<bool>(name, @default);
    public static IArgSpec Str(string name, string @default = "") => new ArgSpec<string>(name, @default);
  }

  // Holds parsed values with access by index or by arg name.
  public sealed class ArgValues
  {
    private readonly object[] valuesStore;
    private readonly System.Collections.Generic.Dictionary<string, int> nameToIndex;

    internal ArgValues(object[] values, IArgSpec[] specs)
    {
      valuesStore = values ?? Array.Empty<object>();
      nameToIndex = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
      for (int i = 0; i < (specs?.Length ?? 0); i++)
      {
        var name = specs[i].Name ?? string.Empty;
        if (!nameToIndex.ContainsKey(name))
        {
          nameToIndex[name] = i;
        }
      }
    }

    // Positional access (0-based)
    public object this[int index] => valuesStore[index];

    // Named access (case-insensitive)
    public object this[string name] => valuesStore[nameToIndex[name]];

    // Convenience typed getter
    public T Get<T>(string name)
    {
      var val = this[name];
      if (val is T t) return t;
      return (T)Convert.ChangeType(val, typeof(T), CultureInfo.InvariantCulture);
    }
  }

  public static class ArgParser
  {

    /// <summary>
    /// Parse positional args with defaults into an ArgValues object supporting name-based access.
    /// Extra tokens are ignored; missing tokens use defaults.
    /// If parsing fails for a position, invokes onError (if provided) and returns false.
    /// </summary>
    public static bool TryParseArgs(string args, out ArgValues values, Action<string> onError, params IArgSpec[] specs)
    {
      var tokens = Tokenize(args);

      if (specs == null || specs.Length == 0)
      {
        values = new ArgValues(Array.Empty<object>(), Array.Empty<IArgSpec>());
        return true;
      }

      var raw = new object[specs.Length];

      for (int i = 0; i < specs.Length; i++)
      {
        raw[i] = specs[i].Default;
      }

      int count = Math.Min(tokens.Length, specs.Length);
      for (int i = 0; i < count; i++)
      {
        var spec = specs[i];
        string token = tokens[i];

        if (!spec.TryParse(token, out var parsed))
        {
          onError?.Invoke($"failed to parse arg '{spec.Name}' as {TypeDisplayName(spec.Type)} from token '{token}'");
          values = new ArgValues(raw, specs);
          return false;
        }

        raw[i] = parsed;
      }

      values = new ArgValues(raw, specs);
      return true;
    }

    private static string[] Tokenize(string args)
    {
      if (string.IsNullOrWhiteSpace(args))
      {
        return Array.Empty<string>();
      }

      var matches = Regex.Matches(args, @"[^\s]+");
      var result = new string[matches.Count];

      for (int i = 0; i < matches.Count; i++)
      {
        result[i] = matches[i].Value;
      }

      return result;
    }

    private static string TypeDisplayName(Type t)
    {
      if (t == typeof(int)) return "int";

      if (t == typeof(float)) return "float";

      if (t == typeof(double)) return "double";

      if (t == typeof(bool)) return "bool";

      if (t == typeof(string)) return "string";

      return t.Name;
    }

  } // class ArgParser

} // namespace SwimEditor
