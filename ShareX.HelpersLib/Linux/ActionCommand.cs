// ShareX - GPL-3.0-or-later. Linux action argument handling.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ShareX.HelpersLib.Linux;

public static class ActionCommand
{
    public static string FindExecutable(string executable)
    {
        if (string.IsNullOrWhiteSpace(executable)) return null;
        if (executable.Contains('/')) return File.Exists(executable) ? executable : null;
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(directory)) continue;
            string candidate = Path.Combine(directory, executable);
            if (File.Exists(candidate) && (File.GetUnixFileMode(candidate) &
                (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0) return candidate;
        }
        return null;
    }

    public static IEnumerable<string> Arguments(string template, string input, string output)
    {
        // Split the template BEFORE substitution, so filenames containing quotes, spaces,
        // backslashes, dollar signs or shell metacharacters remain one literal argument.
        foreach (string argument in Split(string.IsNullOrWhiteSpace(template) ? "$input" : template))
        {
            yield return argument.Replace("$input", input).Replace("%input", input)
                .Replace("$output", output).Replace("%output", output);
        }
    }

    private static IEnumerable<string> Split(string template)
    {
        StringBuilder value = new();
        char quote = '\0';
        bool started = false;
        for (int i = 0; i < template.Length; i++)
        {
            char c = template[i];
            if (c == '\\' && quote != '\'' && i + 1 < template.Length &&
                (quote == '\0' || template[i + 1] is '"' or '\\'))
            {
                value.Append(template[++i]); started = true;
            }
            else if (c == quote) { quote = '\0'; }
            else if (quote == '\0' && c is '\'' or '"') { quote = c; started = true; }
            else if (quote == '\0' && char.IsWhiteSpace(c))
            {
                if (started) { yield return value.ToString(); value.Clear(); started = false; }
            }
            else { value.Append(c); started = true; }
        }
        if (quote != '\0') throw new FormatException("Unclosed quote in action arguments.");
        if (started) yield return value.ToString();
    }
}
