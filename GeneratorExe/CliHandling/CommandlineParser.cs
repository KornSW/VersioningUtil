using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;

namespace Versioning.CliHandling {

  //v 21.09.2023

  internal class CommandLineParser : IDisposable {

    public class CommandLineArgument {

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      private string _Name;

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      private string[] _Params;

      public CommandLineArgument(string name, params string[] @params) {
        _Name = name;
        _Params = @params;
      }

      public string Name {
        get {
          return _Name;
        }
      }

      public string this[int index] {
        get {
          if (index <= _Params.Length - 1)
            return _Params[index];
          else
            return string.Empty;
        }
      }

      public string[] Params {
        get {
          return _Params;
        }
      }

      public override string ToString() {
        return _Name;
      }
    }

    public static CommandLineParser ForCurrentProcess() {
      return new CommandLineParser(Environment.CommandLine.Trim(), true);
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string _CommandLine;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private CommandLineArgument[] _ParsedArguments;

    public CommandLineParser(IEnumerable<string> commandLine, bool skipFirst = false) {
      _CommandLine = JoinCommandLine(commandLine.ToArray());
      Parse(skipFirst);
    }

    public CommandLineParser(string[] commandLine, bool skipFirst = false) {
      _CommandLine = JoinCommandLine(commandLine);
      Parse(skipFirst);
    }

    public CommandLineParser(string commandLine, bool skipFirst = false) {
      _CommandLine = commandLine;
      Parse(skipFirst);
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    protected const char _StringEncapsulationChar = '"';

    protected virtual bool NeedsEncapsulation(string commandLine) {
      return commandLine.Contains(_ArgumentSeparationChar);
    }

    protected virtual bool IsEncapsulated(string commandLine) {
      if (commandLine.Length < 2)
        return false;
      else
        return commandLine.StartsWith(_StringEncapsulationChar) && commandLine.EndsWith(_StringEncapsulationChar);
    }

    protected virtual void Encapsulate(ref string commandLine) {
      if (!IsEncapsulated(commandLine))
        commandLine = string.Format("{0}{1}{0}", _StringEncapsulationChar, commandLine);
    }

    protected virtual void Unencapsulate(ref string commandLine) {
      if (IsEncapsulated(commandLine))
        commandLine = commandLine.Substring(1, commandLine.Length - 2);
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    protected const char _TerminatorChar = '\\';

    protected virtual void Terminate(ref string commandLine) {
      if (commandLine.Contains(_TerminatorChar))
        commandLine = commandLine.Replace(C2s(_TerminatorChar), C2s(_TerminatorChar, _TerminatorChar));
      if (commandLine.Contains(_StringEncapsulationChar))
        commandLine = commandLine.Replace(C2s(_StringEncapsulationChar), C2s(_TerminatorChar, _StringEncapsulationChar));
      if (commandLine.Contains(_ArgumentSeparationChar))
        commandLine = commandLine.Replace(C2s(_ArgumentSeparationChar), C2s(_TerminatorChar, _ArgumentSeparationChar));
    }

    private string C2s(params char[] chars) {
      string result = string.Empty;
      foreach (char c in chars) {
        result = result + new string(c, 1);
      }
      return result;
    }

    protected virtual void Unterminate(ref string commandLine) {
      if (commandLine.Contains(C2s(_TerminatorChar, _TerminatorChar)))
        commandLine = commandLine.Replace(C2s(_TerminatorChar, _TerminatorChar), C2s(_TerminatorChar));
      if (commandLine.Contains(C2s(_TerminatorChar, _StringEncapsulationChar)))
        commandLine = commandLine.Replace(C2s(_TerminatorChar, _StringEncapsulationChar), C2s(_StringEncapsulationChar));
      if (commandLine.Contains(C2s(_TerminatorChar, _ArgumentSeparationChar)))
        commandLine = commandLine.Replace(C2s(_TerminatorChar, _ArgumentSeparationChar), C2s(_ArgumentSeparationChar));
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    protected const char _ArgumentSeparationChar = ' ';
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    protected static char[] _ArgParamSeparators = new[] { '=', ':' };

    protected virtual string JoinCommandLine(string[] commandLineArgumentArray) {
      for (int i = 0; i <= commandLineArgumentArray.Length - 1; i++) {
        Unencapsulate(ref commandLineArgumentArray[i]);

        Terminate(ref commandLineArgumentArray[i]);

        if (NeedsEncapsulation(commandLineArgumentArray[i]))
          Encapsulate(ref commandLineArgumentArray[i]);
      }

      return string.Join(_ArgumentSeparationChar, commandLineArgumentArray);
    }

    protected virtual string[] SplitCommandLine(string commandLine, bool skipFirst) {
      List<string> commandLineArgumentList = new List<string>();
      bool terminationActive = false;
      bool encapsulationActive = false;
      bool encapsulationWasJustActive = false;
      System.Text.StringBuilder currentArg = new System.Text.StringBuilder();

      foreach (char currentChar in commandLine) {

        if (currentChar != _ArgumentSeparationChar) {
          //the case for _ArgumentSeparationChar is the only one who will need to take a look at
          //encapsulationWasJustActive, and only if it was set during the last iteration!
          //so we can reset it in all other cases
          encapsulationWasJustActive = false;
        }

        switch (currentChar) {
          case _TerminatorChar: {
            if (terminationActive) {
              currentArg.Append(_TerminatorChar);
              terminationActive = false;
            } else
              terminationActive = true;
            break;
          }
          case _ArgumentSeparationChar: {
            if (encapsulationActive || terminationActive) {
              currentArg.Append(_ArgumentSeparationChar);
              terminationActive = false;
            } else {
              // to begin a new argument, we need to submit the current argument
              if (currentArg.Length > 0 || encapsulationWasJustActive) {
                commandLineArgumentList.Add(currentArg.ToString());
                currentArg.Clear();
              }
            }
            break;
          }

          case _StringEncapsulationChar: {
            if (terminationActive) {
              currentArg.Append(_StringEncapsulationChar);
              terminationActive = false;
            } else
              encapsulationActive = !encapsulationActive;
            encapsulationWasJustActive = true;
            break;
          }

          default: {
            if (terminationActive) {
              currentArg.Append(_TerminatorChar);
              terminationActive = false;
            }
            currentArg.Append(currentChar);
            break;
          }
        }
      }

      //special case if terminator is LAST char!
      if (terminationActive) {
        currentArg.Append(_TerminatorChar);
      }

      // submit the last argument
      if (currentArg.Length > 0) {
        commandLineArgumentList.Add(currentArg.ToString());
        currentArg.Clear();
      }

      if (skipFirst && commandLineArgumentList.Count > 0)
        commandLineArgumentList.RemoveAt(0);

      return commandLineArgumentList.ToArray();
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    protected static string[] _ArgBeginMarkers = new[] { "/", "-", "--" };

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    protected const string _DefaultName = "Default";

    protected virtual void Parse(bool skipFirst) {
      List<CommandLineArgument> parsedArgs = new List<CommandLineArgument>();
      string currentName = _DefaultName;
      List<string> currentParams = new List<string>();
      string[] splittedCommandLine;

      splittedCommandLine = SplitCommandLine(_CommandLine, skipFirst);

      foreach (string arg in splittedCommandLine) {
        string newArgName = string.Empty;

        foreach (string argBeginMarker in _ArgBeginMarkers) {
          if (arg.StartsWith(argBeginMarker)) {
            newArgName = arg.Substring(argBeginMarker.Length, arg.Length - argBeginMarker.Length);
            break;
          }
        }

        if (newArgName != string.Empty) {
          if (currentName != _DefaultName || currentParams.Count > 0)
            parsedArgs.Add(new CommandLineArgument(currentName, currentParams.ToArray()));

          // make "/A:123 /B:345" >> "/A 123 /B 345"
          foreach (char argParamSeparator in _ArgParamSeparators) {
            if (newArgName.Contains(argParamSeparator))
              newArgName = newArgName.Replace(argParamSeparator, _ArgParamSeparators[0]);
          }

          string[] argNameSplit = newArgName.Split(_ArgParamSeparators[0]);
          currentName = argNameSplit[0];
          currentParams.Clear();

          // all additional params which were directly appended on the paramname
          // shall be transfered into the param list
          for (int i = 1; i <= argNameSplit.Length - 1; i++) {

            // this check removes the phenom that the string "/R: 123"
            // would have two values {"","123"} because of the blank
            if (!string.IsNullOrEmpty(argNameSplit[i]))
              currentParams.Add(argNameSplit[i]);
          }
        } else
          currentParams.Add(arg);
      }

      if (currentName != _DefaultName || currentParams.Count > 0)
        parsedArgs.Add(new CommandLineArgument(currentName, currentParams.ToArray()));

      _ParsedArguments = parsedArgs.ToArray();
    }

    public virtual string CommandLine {
      get {
        return _CommandLine;
      }
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public virtual CommandLineArgument[] Arguments {
      get {
        return _ParsedArguments;
      }
    }

    /// <summary>
    ///  Returns the argument matching the specified name. If na argument was found,
    ///  nothing will be returned.
    /// </summary>
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    public CommandLineArgument this[string name] {
      get {
        return (from arg in _ParsedArguments
                where arg.Name.ToLower() == name.ToLower()
                select arg
              ).FirstOrDefault();
      }
    }

    public bool ContainsArgument(string name) {
      return this[name] != null;
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public override string ToString() {
      return _CommandLine;
    }

    public bool TryGetNamelessParam(int paramIndex, ref string value) {
      return TryGetParam(_DefaultName, paramIndex, ref value);
    }
    public bool TryGetParam(string argumentName, int paramIndex, ref string value) {
      if (ContainsArgument(argumentName)) {
        if (paramIndex <= this[argumentName].Params.Length - 1) {
          value = this[argumentName].Params[paramIndex];
          return true;
        }
      }
      return false;
    }

    public string GetParam(string argumentName, int paramIndex, string defaultValue = "") {
      string returnValue = defaultValue;
      TryGetParam(argumentName, paramIndex, ref returnValue);
      return returnValue;
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void IfArgument(string name, Action thenDo) {
      if (ContainsArgument(name))
        thenDo.Invoke();
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void IfArgument(string name, Action<string[]> thenDo) {
      if (ContainsArgument(name))
        thenDo.Invoke(this[name].Params);
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void IfArgument(string name, Action<string> thenDo) {
      if (ContainsArgument(name))
        thenDo.Invoke(this[name].Params[0]);
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void IfArgument(string name, Action<string, string> thenDo) {
      if (ContainsArgument(name))
        thenDo.Invoke(this[name].Params[0], this[name].Params[1]);
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void IfArgument(string name, Action<string, string, string> thenDo) {
      if (ContainsArgument(name))
        thenDo.Invoke(this[name].Params[0], this[name].Params[1], this[name].Params[2]);
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void IfArgument(string name, Action thenDo, Action elseDo) {
      if (ContainsArgument(name))
        thenDo.Invoke();
      else
        elseDo.Invoke();
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void IfArgument(string name, Action<string[]> thenDo, Action elseDo) {
      if (ContainsArgument(name))
        thenDo.Invoke(this[name].Params);
      else
        elseDo.Invoke();
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void IfArgument(string shortName, string longName, Action thenDo) {
      if (ContainsArgument(shortName))
        thenDo.Invoke();
      else if (ContainsArgument(longName))
        thenDo.Invoke();
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void IfArgument(string shortName, string longName, Action<string[]> thenDo) {
      if (ContainsArgument(shortName))
        thenDo.Invoke(this[shortName].Params);
      else if (ContainsArgument(longName))
        thenDo.Invoke(this[longName].Params);
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void IfArgument(string shortName, string longName, Action thenDo, Action elseDo) {
      if (ContainsArgument(shortName) || ContainsArgument(longName))
        thenDo.Invoke();
      else
        elseDo.Invoke();
    }

    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public void IfArgument(string shortName, string longName, Action<string[]> thenDo, Action elseDo) {
      if (ContainsArgument(shortName))
        thenDo.Invoke(this[shortName].Params);
      else if (ContainsArgument(longName))
        thenDo.Invoke(this[longName].Params);
      else
        elseDo.Invoke();
    }

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private bool _AlreadyDisposed = false;

    /// <summary>
    ///   ''' Dispose the current object instance
    ///   ''' </summary>
    protected virtual void Dispose(bool disposing) {
      if (!_AlreadyDisposed) {
        if (disposing) {
        }
        _AlreadyDisposed = true;
      }
    }

    /// <summary>
    ///   ''' Dispose the current object instance and suppress the finalizer
    ///   ''' </summary>
    public void Dispose() {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

  }

}
