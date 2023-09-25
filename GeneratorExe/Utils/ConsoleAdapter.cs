using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.ComponentModel;
using System.Net.Http;
using System.Text.Json;
using Versioning;
using System.Security.Cryptography;

namespace Utils {

  //v 21.09.2023

  internal class ConsoleAdapter : IDisposable {
    private static ConsoleAdapter _Instance = null;

    public static ConsoleAdapter GetInstance() {
      if ((_Instance == null))
        _Instance = new ConsoleAdapter();
      return _Instance;
    }

    private ConsoleAdapter() {
    }

    public void RegisterInvokationTarget<TTargetType>(TTargetType instance) {
      this.RegisterInvokationTarget(typeof(TTargetType), instance);
    }

    public void RegisterInvokationTarget(object instance) {
      this.RegisterInvokationTarget(instance.GetType(), instance);
    }

    private List<Tuple<Type, object>> _Targets = new List<Tuple<Type, object>>();

    public void RegisterInvokationTarget(Type targetType, object instance) {
      _Targets.Add(new Tuple<Type, object>(targetType, instance));
      this.RebuildCommandCache();
    }

    public void EnterConsoleLoop() {
      this.EnterConsoleLoop(new CancellationToken());
    }

    public void EnterConsoleLoop(CancellationToken cancellationToken) {
      this.RebuildCommandCache();
      while (!cancellationToken.IsCancellationRequested) {
        var line = this.ReadLine();
        if ((line.ToLower().Trim() == "exit"))
          return;
        else if ((!string.IsNullOrWhiteSpace(line))) {
          this.InvokeCommand(line);
          Console.WriteLine();
        }
      }
    }

    private class CommandInvoker {
      private MethodInfo _Mi;
      private object _Instance;
      private Type[] _ParamTypes;
      private int _OptionalParamCount;
      private string _XmlComment = null;

      public CommandInvoker(MethodInfo mi, object instance) {
        _Mi = mi;
        _Instance = instance;
        _ParamTypes = _Mi.GetParameters().Select(p => p.ParameterType).ToArray();
        _OptionalParamCount = _Mi.GetParameters().Where((p) => p.IsOptional).Count();
      }

      public int MaxParamCount {
        get {
          return _ParamTypes.Length;
        }
      }

      public int MinParamCount {
        get {
          return this.MaxParamCount - _OptionalParamCount;
        }
      }

      private string FirstToUpper(string input) {
        return input.Substring(0, 1).ToUpper() + input.Substring(1);
      }

      public string GetHelpString(bool includeMethodXml = true,bool includeMethodSignature = true) {
        StringBuilder sb = new StringBuilder();
        string indent = "  ";
        var @params = _Mi.GetParameters();

        if (includeMethodSignature) {
          sb.Append(indent + _Mi.Name);
          if ((@params.Any())) {
            foreach (var p in @params) {
              string paraInfoString = $"{this.FirstToUpper(p.Name)}({p.ParameterType.Name})";
              if (p.IsOptional) {
                string def = "null";
                if(p.DefaultValue != null) {
                  def = p.DefaultValue.ToString();
                  if (p.DefaultValue.GetType() == typeof(string)) {
                    def = "\"" + def + "\"";
                  }
                }
                sb.Append(" [" + paraInfoString + "=" + def + "]");
              }
              else {
                sb.Append(" <" + paraInfoString + ">");
              }          
            }
          }
          else {
            sb.AppendLine();
            sb.Append(indent + indent + "(no parameters expected)");
          }
        }

        if (_XmlComment == null) {
          try {
            _XmlComment = _Mi.GetDocumentation(false);
            foreach (var p in @params) {
              string pDoc = p.GetDocumentation(false);
              if (!String.IsNullOrWhiteSpace(pDoc)) {
                if (!string.IsNullOrWhiteSpace(_XmlComment)) {
                  _XmlComment = _XmlComment + Environment.NewLine;
                }
                //string paraInfoString = $"{this.FirstToUpper(p.Name)}({p.ParameterType.Name})";
                //if (p.IsOptional) {
                //  _XmlComment = _XmlComment + $"- Opt. param {paraInfoString}: " + pDoc;
                //  _XmlComment = _XmlComment + $"- Opt. param {paraInfoString}: " + pDoc;
                //}
                //else {
                //  _XmlComment = _XmlComment + $"- Req. param {paraInfoString}: " + pDoc;
                //}
                _XmlComment = _XmlComment + $"@{this.FirstToUpper(p.Name)} -> " + pDoc;
              }
            }
          }
          catch {
            _XmlComment = string.Empty;
          }
   
        }
        if (!string.IsNullOrWhiteSpace(_XmlComment) && includeMethodXml) {
          sb.AppendLine();
          sb.Append(indent + indent + _XmlComment.Replace(Environment.NewLine, Environment.NewLine + indent + indent));
        }
         
        return sb.ToString();
      }

      public string Invoke(string[] args) {
        try {
          if ((args.Length > this.MaxParamCount || args.Length < this.MinParamCount)) {
            if(this.MaxParamCount == this.MinParamCount) {
              throw new ArgumentException(string.Format("A Count of {1} Arguments ist expected, but {0} were entered!", args.Length, this.MaxParamCount));
            }
            else {
              throw new ArgumentException(string.Format("A Count of {1}-{2} Arguments ist expected, but {0} were entered!", args.Length, this.MinParamCount, this.MaxParamCount));
            }
          }

          object[] parameterValues = new object[_ParamTypes.Length - 1 + 1];
          int i = 0;
          foreach (var p in _ParamTypes) {
            if (i < args.Length) {
              if ((TryParse(p,args[i], ref parameterValues[i]) == false)) { 
                throw new ArgumentException(string.Format("The {1}-Object cannot parse the string '{0}'!", args[i], p.Name));
              }
            }
            else {
              parameterValues[i] = Type.Missing;
            }
            i += 1;
          }
          object result;
          if ((_Mi.IsStatic))
            result = _Mi.Invoke(null, parameterValues);
          else
            result = _Mi.Invoke(_Instance, parameterValues);
          if ((result != null)) {
            var opt = new JsonSerializerOptions();
            opt.PropertyNameCaseInsensitive = true;
            opt.IncludeFields = true;
            opt.WriteIndented = true;
            return JsonSerializer.Serialize(result, opt);
          }
          else
            return string.Empty;
        }
        catch (ArgumentException ex) {
          var oldColor = Console.ForegroundColor;
          Console.ForegroundColor = ConsoleColor.Red;
          Console.WriteLine(ex.Message);
          Console.ForegroundColor = oldColor;
          Console.ForegroundColor = ConsoleColor.White;
          Console.Write("Correct usage:");
          Console.WriteLine(this.GetHelpString(false));
          Console.ForegroundColor = oldColor;
        }
        catch (TargetInvocationException ex) {
          var old = Console.ForegroundColor;
          Console.ForegroundColor = ConsoleColor.Red;
          if (ex.InnerException != null) {
            Console.WriteLine(ex.InnerException.Message);
          }
          else {
            Console.WriteLine(ex.Message);
          }
          Console.ForegroundColor = old;
        }
        catch (Exception ex) {
          var old = Console.ForegroundColor;
          Console.ForegroundColor = ConsoleColor.Red;
          Console.WriteLine(ex.Message);
          Console.ForegroundColor = old;
        }
        return string.Empty;
      }
    }

    private void RebuildCommandCache() {
      _CommandCache.Clear();
      foreach (var target in _Targets) {
        var methods = target.Item1.GetMethods();
        foreach (var m in methods) {
          if ((!m.ContainsGenericParameters)) {
            // no specialnames like proeprty-getters and no functions of system.object (like GetType or GetHashCode)
            if ((!_CommandCache.ContainsKey(m.Name) && !m.IsSpecialName && m.DeclaringType != typeof(object)))
              _CommandCache.Add(m.Name, new CommandInvoker(m, target.Item2));
          }
        }
      }
    }

    private void MoveBack(int charChount) {
      while ((charChount > 0)) {
        int offsetForThisLine;
        if ((Console.CursorLeft < charChount)) {
          offsetForThisLine = Console.CursorLeft;
          Console.CursorLeft = 0;
          Console.Write(new string(' ', offsetForThisLine));
          MoveCursorBackward(1);
          Console.Write(" ");
          MoveCursorBackward(1);
          charChount -= (offsetForThisLine + 1);
        }
        else {
          offsetForThisLine = charChount;

          MoveCursorBackward(charChount);
          // Console.CursorLeft = Console.CursorLeft - offsetForThisLine
          Console.Write(new string(' ', offsetForThisLine));
          Console.CursorLeft = Console.CursorLeft - offsetForThisLine;
          charChount = 0;
        }
      }
    }

    private void MoveCursorBackward(int charChount) {
      while ((charChount > 0)) {
        int offsetForThisLine;
        if ((Console.CursorLeft < charChount)) {
          offsetForThisLine = Console.CursorLeft;
          Console.CursorLeft = Console.BufferWidth - 1;
          Console.CursorTop -= 1;
          charChount -= (offsetForThisLine + 1);
        }
        else {
          offsetForThisLine = charChount;
          Console.CursorLeft = Console.CursorLeft - offsetForThisLine;
          charChount = 0;
        }
      }
    }

    private List<string> _LineInputHistory = new List<string>();
    private Dictionary<string, CommandInvoker> _CommandCache = new Dictionary<string, CommandInvoker>();
    private int _LastFoistLineEndPosition = 0;
    private bool _ReadingLine = false;
    // Private _PromptLineInterrupted As Boolean = False

    public void WriteLine() {
      this.FoistOutput(string.Empty, true, Console.ForegroundColor);
    }
    public void WriteLine(string line) {
      this.FoistOutput(line, true, Console.ForegroundColor);
    }
    public void WriteLine(string line, ConsoleColor color) {
      this.FoistOutput(line, true, color);
    }
    public void Write(string text) {
      this.FoistOutput(text, false, Console.ForegroundColor);
    }
    public void Write(string text, ConsoleColor color) {
      this.FoistOutput(text, false, color);
    }

    /// <summary>
    ///     ''' Used to insert logging output from other threads into the console without corrupting the current input...
    ///     ''' </summary>
    private void FoistOutput(string text, bool writeLinebreak, ConsoleColor color) {
      lock (this) {
        var colorBefore = Console.ForegroundColor;

        if ((_ReadingLine)) {
          _OldLeft = Console.CursorLeft;
          _OldTop = Console.CursorTop;

          // zum begin der akt. input-zeile fahren (weil ja ein promt + pot. input angezeigt wird
          this.MoveBack(_CurrentInputBuffer.Length + 2);

          if ((_LastFoistLineEndPosition > 0)) {
            // wir düren jetzt noch nicht einmal eine neue zeile beginnen, sonder müssen in der alten logzeile weiterschreiben
            int leftSpaceOnLastFoistLine = Console.BufferWidth - _LastFoistLineEndPosition;
            this.MoveBack(leftSpaceOnLastFoistLine);
          }
        }

        Console.ForegroundColor = color;
        // den gewünchten output schreiben
        if ((writeLinebreak))
          Console.WriteLine(text);
        else
          Console.Write(text);
        _LastFoistLineEndPosition = Console.CursorLeft;
        Console.ForegroundColor = colorBefore;

        if ((_ReadingLine)) {

          // wenn es keinen umbruch gab (entweder wia argument oder bereits im string)
          if ((_LastFoistLineEndPosition != 0))
            // brauchen wir trotzdem eine neue zeile für den input
            Console.WriteLine();

          // input wiederherstellen
          Console.Write("> ");
          Console.Write(_CurrentInputBuffer.ToString());

          // cursor-position wiederherstellen
          if ((Console.CursorLeft == 0)) {
            _OldLeft = Console.BufferWidth - 1;
            _OldTop = Console.CursorTop - 1;
          }
          else {
            _OldLeft = Console.CursorLeft - 1;
            _OldTop = Console.CursorTop;
          }
        }
      }
    }

    private StringBuilder _CurrentInputBuffer = new StringBuilder();
    private int _OldLeft = Console.CursorLeft;
    private int _OldTop = Console.CursorTop;

    public string ReadLine() {
      lock (this) {
        Console.Write("> ");
        _CurrentInputBuffer.Clear();
        _ReadingLine = true;
        _OldLeft = Console.CursorLeft;
        _OldTop = Console.CursorTop;
      }

      int currentHistoryIndex = -1;
      int currentAutoCompleteIndex = -1;
      string[] currentAutoCompleteCache = null;

      do {
        var keyInfo = Console.ReadKey();
        lock (this) {
          switch (keyInfo.Key) {
            case ConsoleKey.LeftArrow: {
                Console.SetCursorPosition(_OldLeft, _OldTop);
                break;
              }

            case ConsoleKey.RightArrow: {
                Console.SetCursorPosition(_OldLeft, _OldTop);
                break;
              }

            case ConsoleKey.Delete: {
                Console.SetCursorPosition(_OldLeft, _OldTop);
                currentAutoCompleteIndex = -1;
                break;
              }

            case ConsoleKey.Backspace: {
                currentAutoCompleteIndex = -1;
                if ((_CurrentInputBuffer.Length > 0)) {
                  _CurrentInputBuffer.Remove(_CurrentInputBuffer.Length - 1, 1);
                  Console.SetCursorPosition(_OldLeft, _OldTop);
                  this.MoveBack(1);
                }
                else
                  // recover the deleted blank from the input promt
                  Console.Write(" ");
                break;
              }

            case ConsoleKey.Escape: {
                currentAutoCompleteIndex = -1;
                this.MoveBack(_CurrentInputBuffer.Length + 1);
                _CurrentInputBuffer.Clear();
                break;
              }

            case ConsoleKey.UpArrow: {
                Console.SetCursorPosition(_OldLeft, _OldTop);
                currentAutoCompleteIndex = -1;
                if ((_LineInputHistory.Count > (currentHistoryIndex + 1))) {
                  this.MoveBack(_CurrentInputBuffer.Length);
                  _CurrentInputBuffer.Clear();
                  currentHistoryIndex += 1;
                  string historyEntry = _LineInputHistory[_LineInputHistory.Count - currentHistoryIndex - 1];
                  Console.Write(historyEntry);
                  _CurrentInputBuffer.Append(historyEntry);
                }

                break;
              }

            case ConsoleKey.DownArrow: {
                Console.SetCursorPosition(_OldLeft, _OldTop);
                currentAutoCompleteIndex = -1;
                if ((currentHistoryIndex > 0)) {
                  this.MoveBack(_CurrentInputBuffer.Length);
                  _CurrentInputBuffer.Clear();
                  currentHistoryIndex -= 1;
                  string historyEntry = _LineInputHistory[_LineInputHistory.Count - currentHistoryIndex - 1];
                  Console.Write(historyEntry);
                  _CurrentInputBuffer.Append(historyEntry);
                }
                else if ((currentHistoryIndex == 0)) {
                  this.MoveBack(_CurrentInputBuffer.Length);
                  _CurrentInputBuffer.Clear();
                  currentHistoryIndex = -1;
                }

                break;
              }

            case ConsoleKey.Tab: {
                Console.SetCursorPosition(_OldLeft, _OldTop);
                if ((_CurrentInputBuffer.Length > 0)) {
                  if ((!_CurrentInputBuffer.ToString().Contains(" "))) {
                    if ((currentAutoCompleteIndex == -1)) {
                      string incompleteLine = _CurrentInputBuffer.ToString().ToLower();
                      currentAutoCompleteCache = _CommandCache.Keys.Where(k => k.ToLower().StartsWith(incompleteLine)).ToArray();
                      if ((currentAutoCompleteCache.Length > 0)) {
                        currentAutoCompleteIndex = 0;
                        this.MoveBack(_CurrentInputBuffer.Length);
                        _CurrentInputBuffer.Clear();
                        Console.Write(currentAutoCompleteCache[currentAutoCompleteIndex]);
                        _CurrentInputBuffer.Append(currentAutoCompleteCache[currentAutoCompleteIndex]);
                      }
                    }
                    else if ((currentAutoCompleteCache != null && currentAutoCompleteCache.Length > 1)) {
                      this.MoveBack(_CurrentInputBuffer.Length);
                      _CurrentInputBuffer.Clear();
                      if ((keyInfo.Modifiers == ConsoleModifiers.Shift)) {
                        if ((currentAutoCompleteIndex == 0))
                          currentAutoCompleteIndex = (currentAutoCompleteCache.Length - 1);
                        else
                          currentAutoCompleteIndex -= 1;
                      }
                      else if ((currentAutoCompleteIndex == (currentAutoCompleteCache.Length - 1)))
                        currentAutoCompleteIndex = 0;
                      else
                        currentAutoCompleteIndex += 1;
                      Console.Write(currentAutoCompleteCache[currentAutoCompleteIndex]);
                      _CurrentInputBuffer.Append(currentAutoCompleteCache[currentAutoCompleteIndex]);
                    }
                  }
                }

                break;
              }

            case ConsoleKey.F1: {
                Console.WriteLine(" ~F1~");
                if ((_CurrentInputBuffer.Length > 0)) {
                  var bufferTxt = _CurrentInputBuffer.ToString();
                  CommandInvoker singleMatch = null;
                  if ((bufferTxt.Contains(" "))) {
                    var commandName = bufferTxt.Split(' ')[0].ToLower();
                    var cmd = _CommandCache.Keys.Where(k => k.ToLower() == commandName).SingleOrDefault();
                    if ((cmd != null))
                      singleMatch = _CommandCache[cmd];
                    else
                      Console.WriteLine("no help available");
                  }
                  else {
                    var matches = _CommandCache.Keys.Where(k => k.ToLower().StartsWith(bufferTxt.ToLower())).ToArray();
                    if ((matches.Count() == 1 && matches[0].Length == bufferTxt.Length))
                      singleMatch = _CommandCache[matches.Single()];
                    else {
                      Console.WriteLine("List of matching commands:");
                      Console.WriteLine();
                      foreach (var cmd in matches)
                        Console.WriteLine("  " + cmd);
                    }
                  }

                  if ((singleMatch != null))
                    Console.WriteLine(singleMatch.GetHelpString());
                  Console.WriteLine();
                  Console.Write("> ");
                  Console.Write(bufferTxt);
                }
                else {
                  this.PrintAllComands();
                  Console.Write("> ");
                }

                break;
              }

            case ConsoleKey.Enter: {
                currentAutoCompleteIndex = -1;
                string finalInput = _CurrentInputBuffer.ToString();
                if ((!string.IsNullOrWhiteSpace(finalInput)))
                  _LineInputHistory.Add(finalInput);

                // Workarround - somehow the last line of the real input gets lost from the window, so we need to recover it
                int lastLineStart = 0;
                var finalLintToPrint = "> " + finalInput;
                while (finalLintToPrint.Length > (lastLineStart + Console.BufferWidth - 1))
                  lastLineStart += Console.BufferWidth;
                finalLintToPrint = finalLintToPrint.Substring(lastLineStart);
                if ((!string.IsNullOrWhiteSpace(finalLintToPrint)))
                  Console.WriteLine(finalLintToPrint);
                _ReadingLine = false;
                // _PromptLineInterrupted = False
                return finalInput;
              }

            default: {
                currentAutoCompleteIndex = -1;
                if ((keyInfo.Key == ConsoleKey.V && keyInfo.Modifiers == ConsoleModifiers.Control)) {
                  //var txt = My.Computer.Clipboard.GetText(); //.NET FX
                  //var txt = Windows.ApplicationModel.DataTransfer.Clipboard.SetText(txt); //.NET CORE
                  //_CurrentInputBuffer.Append(txt);
                  //this.MoveCursorBackward(1);
                  //Console.Write(txt);
                }
                else if ((!char.IsControl(keyInfo.KeyChar) && (!char.IsWhiteSpace(keyInfo.KeyChar) || keyInfo.KeyChar == ' ')))
                  _CurrentInputBuffer.Append(keyInfo.KeyChar);
                else
                  this.MoveCursorBackward(1);
                break;
              }
          }

          _OldLeft = Console.CursorLeft;
          _OldTop = Console.CursorTop;
        }
      }
      while (true);
    }

    public void PrintAllComands() {
      var oldColor = Console.ForegroundColor;

      Console.WriteLine("Supported commands:");
      Console.WriteLine();
      foreach (var cmd in _CommandCache.Keys) {
        //Console.WriteLine("  " + cmd);

        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(_CommandCache[cmd].GetHelpString(false, true));
        Console.ForegroundColor = ConsoleColor.DarkGreen;
        Console.WriteLine(_CommandCache[cmd].GetHelpString(true, false));

        Console.WriteLine();
      }
      Console.ForegroundColor = oldColor;
    }

    public void InvokeCommand(string[] command) {
      if (command == null || command.Length < 1) return;
      CommandLineParser cmdParser = new CommandLineParser(command);
      this.InvokeCommand(cmdParser);
    }

    public void InvokeCommand(string command) {
      if ((string.IsNullOrWhiteSpace(command))) return;
      CommandLineParser cmdParser = new CommandLineParser(command);
      this.InvokeCommand(cmdParser);  
    }

    private void InvokeCommand(CommandLineParser cmdParser) {
      var defaultParams = cmdParser.Arguments[0].Params;
      var cmd = _CommandCache.Keys.Where(k => k.ToLower() == defaultParams[0].ToLower()).SingleOrDefault();
      if ((cmd == null)) {
        var old = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine("UNKNOWN COMMAND!");
        Console.ForegroundColor = old;
        this.PrintAllComands();
      }
      else {
        var result = _CommandCache[cmd].Invoke(defaultParams.Skip(1).ToArray());
        Console.WriteLine(result);
        //if ((!string.IsNullOrWhiteSpace(result))) {
        //  My.Computer.Clipboard.SetText(result); //.NET FX
        //  Windows.ApplicationModel.DataTransfer.Clipboard.Clipboard.SetText(result); //.NET CORE
        //}
      }
    }


    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private bool _AlreadyDisposed = false;

    /// <summary>
    ///     ''' Dispose the current object instance
    ///     ''' </summary>
    protected virtual void Dispose(bool disposing) {
      if ((!_AlreadyDisposed)) {
        if ((disposing)) {
        }
        _AlreadyDisposed = true;
      }
    }

    /// <summary>
    ///     ''' Dispose the current object instance and suppress the finalizer
    ///     ''' </summary>
    public void Dispose() {
      this.Dispose(true);
      GC.SuppressFinalize(this);
    }

    private static bool TryParse(Type targetType, string sourceText, ref object target) {
      try {
        if ((targetType.IsEnum)) {
          if ((Enum.GetNames(targetType).Select(n => n.ToLower()).Contains(sourceText))) {
            target = Enum.Parse(targetType, sourceText, true);
            return true;
          }
        }

        // special fixes
        if (targetType == typeof(string)) {
          target = sourceText;
          return true;
        }
        else if (targetType == typeof(bool)) {
          if ((sourceText == "1")) sourceText = "true";
          if ((sourceText == "0")) sourceText = "false";
        }

        var flagMask = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.InvokeMethod;

        var parseMethod = targetType.GetMethods(flagMask).Where(
          m => m.Name == "TryParse" &&
          m.GetParameters().Count() == 2 &&
          m.ReturnType == typeof(bool) &&
          m.GetParameters()[0].ParameterType == typeof(string) &&
          m.GetParameters()[1].ParameterType == targetType.MakeByRefType() &&
          m.GetParameters()[1].IsOut
        ).FirstOrDefault();

        if ((parseMethod == null)) {
          try {
            if (sourceText.StartsWith("{")) {
              var opt = new JsonSerializerOptions();
              opt.PropertyNameCaseInsensitive = true;
              opt.IncludeFields = true;
              opt.WriteIndented = true;
              target = JsonSerializer.Deserialize(sourceText, targetType, opt);
              return true;
            }
          }
          catch {
          }
          return false;
        }
        else {
          object def = null;
          if (targetType.IsValueType) {
            def = Activator.CreateInstance(targetType);
          }
          object[] callParams = new[] { sourceText, def };
          var success = (bool)parseMethod.Invoke(null, callParams);
          if ((success))
            target = callParams[1];
          return success;
        }
      }
      catch (Exception ex) {
        return false;
      }
    }

  }

}
